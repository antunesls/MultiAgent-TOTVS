'use strict';

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
const e = escapeHtml;
let token = '';
let state = { environments: [], agents: [], sessions: [], preparations: [], history: [], browsers: [], settings: {}, diagnostics: {} };
let refreshing = false;
let busy = 0;
let settingsDirty = false;

async function api(path, { method = 'GET', body, raw = false } = {}) {
  const headers = { 'X-WAM-Token': token };
  if (body !== undefined) headers['Content-Type'] = raw ? 'application/octet-stream' : 'application/json';
  const response = await fetch('/api' + path, { method, headers, body: body === undefined ? undefined : raw ? body : JSON.stringify(body), credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) {
    let message = `Operação não concluída (${response.status}).`;
    try { message = (await response.json()).error || message; } catch { /* Resposta não JSON, como limite HTTP. */ }
    throw new Error(message);
  }
  return response;
}

function notice(message, error = false) {
  const element = $('#notice');
  element.textContent = message;
  element.classList.toggle('error', error);
  element.hidden = false;
}

async function work(button, action, errorElement) {
  if (button?.disabled) return;
  const original = button?.textContent;
  if (button) { button.disabled = true; button.textContent = 'Aguarde…'; }
  busy++;
  if (errorElement) errorElement.textContent = '';
  try { await action(); }
  catch (error) {
    if (errorElement) errorElement.textContent = error.message;
    else notice(error.message, true);
  } finally {
    busy--;
    if (button) { button.disabled = false; button.textContent = original; }
  }
}

function showView(name) {
  $$('.view').forEach(view => { view.hidden = view.id !== 'view-' + name; });
  $$('[data-view]').forEach(button => {
    const selected = button.dataset.view === name;
    button.classList.toggle('selected', selected);
    if (selected) button.setAttribute('aria-current', 'page');
    else button.removeAttribute('aria-current');
  });
}

function empty(title, text, button = '') {
  return `<div class="empty"><div class="empty-symbol" aria-hidden="true">◇</div><h2>${e(title)}</h2><p>${e(text)}</p>${button}</div>`;
}

function renderEnvironments() {
  const query = $('#search').value.trim().toLocaleLowerCase('pt-BR');
  const entries = state.environments.filter(item => (!$('#only-favorites').checked || item.favorite) && `${item.client} ${item.name} ${item.url}`.toLocaleLowerCase('pt-BR').includes(query));
  $('#environment-count').textContent = state.environments.length;
  if (!entries.length) {
    $('#environment-list').innerHTML = state.environments.length ? empty('Nenhum ambiente encontrado', 'Tente outro termo ou desmarque o filtro de favoritos.') : empty('Seu próximo cliente começa aqui', 'Prepare um WebAgent e cadastre a URL do primeiro ambiente. Você poderá abrir vários clientes ao mesmo tempo.', '<button class="primary" data-action="prepare">Preparar primeiro WebAgent</button>');
    return;
  }
  const clients = [...new Set(entries.map(item => item.client))].sort((a, b) => a.localeCompare(b, 'pt-BR'));
  $('#environment-list').innerHTML = clients.map(client => {
    const items = entries.filter(item => item.client === client).sort((a, b) => Number(b.favorite) - Number(a.favorite) || a.name.localeCompare(b.name, 'pt-BR'));
    return `<section class="client-group"><h2 class="client-heading">${e(client)} <small>${items.length} ${items.length === 1 ? 'ambiente' : 'ambientes'}</small></h2>${items.map(item => {
      const agent = state.agents.find(agent => agent.id === item.agentId);
      const running = state.sessions.some(session => session.environmentId === item.id && session.running);
      return `<article class="environment-row"><div><h3 class="environment-name">${e(item.name)}</h3><span class="badge ${item.kind === 'Produção' ? 'production' : ''}">${e(item.kind)}</span> ${running ? '<span class="badge running">Processo aberto</span>' : ''}${item.notes ? `<p class="environment-notes">${e(item.notes)}</p>` : ''}</div><div class="environment-info"><strong>${e(agent?.name || 'Agente indisponível')} <span class="badge">${e(agent?.version)} ${e(agent?.architecture)}</span></strong><span class="mono" title="${e(item.url)}">${e(item.url)}</span></div><div class="row-actions"><button class="icon-button ${item.favorite ? 'favorite' : ''}" data-action="favorite" data-id="${e(item.id)}" aria-label="${item.favorite ? 'Remover dos' : 'Adicionar aos'} favoritos" title="Favorito">${item.favorite ? '★' : '☆'}</button><button class="icon-button" data-action="edit" data-id="${e(item.id)}" aria-label="Editar ${e(item.name)}" title="Editar">⋯</button><button class="primary" data-action="launch" data-id="${e(item.id)}">Abrir</button></div></article>`;
    }).join('')}</section>`;
  }).join('');
}

function renderAgents() {
  $('#agent-list').innerHTML = state.agents.length ? state.agents.map(agent => `<article class="agent-card"><div><h3>${e(agent.name)}</h3><p><span class="badge">${e(agent.version)}</span> <span class="badge">${e(agent.architecture)}</span> · ${state.environments.filter(item => item.agentId === agent.id).length} vínculos</p><p class="mono">${e(agent.directory)}</p><p class="mono">SHA-256 ${e(agent.sha256)}</p></div><div class="button-row"><button data-action="refresh-agent" data-id="${e(agent.id)}">Atualizar identificação</button><button data-action="use-agent" data-id="${e(agent.id)}">Criar ambiente</button></div></article>`).join('') : empty('Nenhum WebAgent preparado', 'Adicione um instalador oficial EXE/MSI ou importe uma pasta já instalada.');
  const pending = state.preparations.filter(item => !['Concluído', 'Cancelado'].includes(item.stage));
  $('#preparation-list').innerHTML = pending.length ? pending.map(item => `<article class="preparation-card"><div><h3>${e(item.name)}</h3><p><span class="badge">${e(item.stage)}</span></p><p>Assinatura válida: ${e(item.inspection.signer)}</p><p class="mono">SHA-256 ${e(item.inspection.sha256)}</p>${item.backup ? `<p>Backup preservado em:</p><p class="mono">${e(item.backup)}</p>` : ''}${item.stage !== 'Verificado' ? '<p>Conclua o instalador, feche o WebAgent pela bandeja e capture a instalação padrão. Se escolheu outro destino, cancele e use “Pasta instalada”.</p>' : '<p>A instalação pode substituir a pasta padrão. As cópias gerenciadas são preservadas.</p>'}</div><div class="button-row">${item.stage === 'Verificado' ? `<button class="primary" data-action="install" data-id="${e(item.id)}">Abrir instalador</button>` : `<button class="primary" data-action="capture" data-id="${e(item.id)}">Capturar instalação</button>`}<button data-action="cancel-preparation" data-id="${e(item.id)}">Cancelar preparação</button></div></article>`).join('') : '<p class="hint">Nenhuma preparação em andamento.</p>';
}

const date = value => new Date(value).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });

function render() {
  renderEnvironments();
  renderAgents();
  $('#session-count').textContent = state.sessions.filter(session => session.running).length;
  $('#session-list').innerHTML = state.sessions.length ? state.sessions.map(session => `<article class="session-card"><div><h3>${e(session.name)}</h3><p>Iniciado em ${e(date(session.startedAt))} · PID ${e(session.pid)}</p></div><span class="badge ${session.running ? 'running' : ''}">${session.running ? 'Processo aberto' : 'Processo finalizado'}</span></article>`).join('') : empty('Nenhuma sessão iniciada', 'Os processos abertos pelo painel aparecerão aqui. Nenhum WebAgent externo será encerrado.');
  $('#history-list').innerHTML = state.history.length ? state.history.map(item => `<div class="history-row"><time datetime="${e(item.time)}">${e(date(item.time))}</time><strong>${e(item.action)}</strong><p>${e(item.detail)}</p></div>`).join('') : empty('Histórico vazio', 'Cadastros, preparações e aberturas serão registrados aqui.');
  $('#browser-paths').innerHTML = state.browsers.map(browser => `<option value="${e(browser.path)}">${e(browser.name)}</option>`).join('');
  if (!settingsDirty && !$('#settings-form').contains(document.activeElement)) $('#settings-form').elements.browser.value = state.settings.browser || '';
  const diagnostics = state.diagnostics;
  const fields = [['Usuário do Windows', diagnostics.user], ['Pasta de dados', diagnostics.dataDirectory], ['Instalação padrão', diagnostics.defaultAgentDirectory], ['Agentes na máquina', diagnostics.anyAgentRunning ? 'Há WebAgents em execução; feche-os antes de preparar uma instalação.' : 'Nenhum WebAgent detectado.'], ['Teste simultâneo real', diagnostics.simultaneousValidation]];
  $('#diagnostics').innerHTML = fields.map(([title, value]) => `<dt>${e(title)}</dt><dd>${e(value)}</dd>`).join('');
  $('#current-user').textContent = `${diagnostics.user} · somente nesta máquina`;
}

async function refresh() {
  if (refreshing) return;
  refreshing = true;
  try {
    state = await (await api('/state')).json();
    render();
    $('#connection-label').textContent = 'Conectado localmente';
    $('#connection-dot').classList.remove('offline');
  } catch (error) {
    $('#connection-label').textContent = 'Conexão interrompida';
    $('#connection-dot').classList.add('offline');
    throw error;
  } finally { refreshing = false; }
}

function editEnvironment(entry = null, agentId = '') {
  if (!state.agents.length) {
    notice('Prepare um WebAgent antes de cadastrar o primeiro ambiente.');
    showView('agents');
    openAgentDialog();
    return;
  }
  const form = $('#environment-form');
  form.reset();
  $('#environment-error').textContent = '';
  $('#environment-title').textContent = entry ? 'Editar ambiente' : 'Novo ambiente';
  $('#environment-agent').innerHTML = state.agents.map(agent => `<option value="${e(agent.id)}">${e(agent.name)} (${e(agent.version)} ${e(agent.architecture)})</option>`).join('');
  for (const name of ['id', 'client', 'name', 'kind', 'url', 'agentId', 'browser', 'notes']) {
    if (entry) form.elements[name].value = entry[name];
  }
  if (agentId) form.elements.agentId.value = agentId;
  form.elements.favorite.checked = entry?.favorite || false;
  let deleteButton = $('#delete-environment');
  if (deleteButton) deleteButton.remove();
  if (entry) {
    deleteButton = document.createElement('button');
    deleteButton.id = 'delete-environment';
    deleteButton.type = 'button';
    deleteButton.textContent = 'Excluir cadastro';
    deleteButton.addEventListener('click', () => work(deleteButton, async () => {
      if (!await confirmAction('Excluir ambiente?', `Excluir o cadastro de ${entry.client} / ${entry.name}? As pastas e sessões existentes serão preservadas.`, 'Excluir cadastro')) return;
      await api('/environments/' + entry.id, { method: 'DELETE' });
      $('#environment-dialog').close();
      notice('Cadastro excluído. Arquivos preservados.');
      await refresh();
    }, $('#environment-error')));
    $('.dialog-actions', form).prepend(deleteButton);
  }
  $('#environment-dialog').showModal();
}

function openAgentDialog() {
  $('#installer-error').textContent = '';
  $('#folder-error').textContent = '';
  if (!$('#folder-form').elements.path.value) $('#folder-form').elements.path.value = state.diagnostics.defaultAgentDirectory || '';
  $('#agent-dialog').showModal();
}

function confirmAction(title, message, accept) {
  const dialog = $('#confirm-dialog');
  $('#confirm-title').textContent = title;
  $('#confirm-message').textContent = message;
  $('#confirm-accept').textContent = accept;
  dialog.returnValue = 'cancel';
  dialog.showModal();
  return new Promise(resolve => dialog.addEventListener('close', () => resolve(dialog.returnValue === 'accept'), { once: true }));
}

document.addEventListener('click', event => {
  const button = event.target.closest('button');
  if (!button || button.disabled) return;
  if (button.dataset.view) showView(button.dataset.view);
  if (button.dataset.close) $('#' + button.dataset.close).close();
  if (button.dataset.agentTab) {
    const installer = button.dataset.agentTab === 'installer';
    $('#installer-form').hidden = !installer;
    $('#folder-form').hidden = installer;
    $$('[data-agent-tab]').forEach(tab => tab.classList.toggle('selected', tab === button));
  }
  const action = button.dataset.action;
  const id = button.dataset.id;
  if (action === 'prepare') { showView('agents'); openAgentDialog(); }
  if (action === 'edit') editEnvironment(state.environments.find(item => item.id === id));
  if (action === 'use-agent') editEnvironment(null, id);
  if (action === 'refresh-agent') work(button, async () => {
    const agent = await (await api('/agents/' + id + '/refresh', { method: 'POST', body: {} })).json();
    notice(`Identificação atualizada: ${agent.name} (${agent.version} ${agent.architecture}). Arquivos e vínculos preservados.`);
    await refresh();
  });
  if (action === 'favorite') work(button, async () => {
    const entry = state.environments.find(item => item.id === id);
    await api('/environments', { method: 'POST', body: { ...entry, favorite: !entry.favorite } });
    await refresh();
  });
  if (action === 'launch') work(button, async () => {
    const entry = state.environments.find(item => item.id === id);
    if (entry.kind === 'Produção' && !await confirmAction('Abrir produção?', `Você vai abrir ${entry.client} / ${entry.name}.\n${entry.url}\n\nOs outros ambientes continuarão funcionando.`, 'Abrir produção')) return;
    await api('/environments/' + id + '/launch', { method: 'POST', body: {} });
    notice(`Abertura solicitada: ${entry.client} / ${entry.name}. Confira a conexão na janela do WebApp.`);
    await refresh();
  });
  if (action === 'install') work(button, async () => {
    if (!await confirmAction('Preparar instalação no Windows?', 'Feche todos os WebAgents antes de continuar.\n\nO instalador pode substituir a instalação padrão. Uma cópia de segurança será feita antes da abertura, se a pasta padrão existir. As pastas gerenciadas não serão substituídas.\n\nConclua o instalador e feche o agente pela bandeja antes de capturar a instalação.', 'Fazer backup e instalar')) return;
    await api('/preparations/' + id + '/install', { method: 'POST', body: { consent: true } });
    notice('Instalador aberto. Conclua no Windows, feche o WebAgent pela bandeja e clique em Capturar instalação.');
    await refresh();
  });
  if (action === 'capture') work(button, async () => {
    const agent = await (await api('/preparations/' + id + '/capture', { method: 'POST', body: {} })).json();
    notice(`WebAgent preparado: ${agent.name} (${agent.version} ${agent.architecture}). Agora vincule a um ambiente.`);
    await refresh();
  });
  if (action === 'cancel-preparation') work(button, async () => {
    await api('/preparations/' + id + '/cancel', { method: 'POST', body: {} });
    notice('Preparação cancelada. Backups e instalações foram preservados.');
    await refresh();
  });
});

$('#new-environment').addEventListener('click', () => editEnvironment());
$('#new-agent').addEventListener('click', openAgentDialog);
$('#search').addEventListener('input', renderEnvironments);
$('#only-favorites').addEventListener('change', renderEnvironments);
$('#refresh').addEventListener('click', event => work(event.currentTarget, refresh));

$('#environment-form').addEventListener('submit', event => {
  event.preventDefault();
  const form = event.currentTarget;
  work($('[type=submit]', form), async () => {
    const entry = Object.fromEntries(new FormData(form));
    entry.favorite = form.elements.favorite.checked;
    await api('/environments', { method: 'POST', body: entry });
    $('#environment-dialog').close();
    notice('Ambiente salvo. Uma cópia exclusiva do agente será criada na primeira abertura.');
    showView('environments');
    await refresh();
  }, $('#environment-error'));
});

$('#installer-form').addEventListener('submit', event => {
  event.preventDefault();
  const form = event.currentTarget;
  work($('[type=submit]', form), async () => {
    const file = form.elements.file.files[0];
    if (!file || file.size < 1 || file.size > 512 * 1024 * 1024) throw new Error('Selecione um instalador de até 512 MB.');
    await api('/preparations/upload?' + new URLSearchParams({ name: form.elements.name.value, fileName: file.name }), { method: 'POST', body: file, raw: true });
    $('#agent-dialog').close();
    form.reset();
    showView('agents');
    notice('Instalador verificado. Confira a preparação e clique em Abrir instalador para continuar.');
    await refresh();
  }, $('#installer-error'));
});

$('#folder-form').addEventListener('submit', event => {
  event.preventDefault();
  const form = event.currentTarget;
  work($('[type=submit]', form), async () => {
    await api('/agents/folder', { method: 'POST', body: Object.fromEntries(new FormData(form)) });
    $('#agent-dialog').close();
    form.elements.name.value = '';
    showView('agents');
    notice('WebAgent importado. A pasta de origem foi preservada.');
    await refresh();
  }, $('#folder-error'));
});

$('#settings-form').addEventListener('submit', event => {
  event.preventDefault();
  const form = event.currentTarget;
  work($('[type=submit]', form), async () => {
    await api('/settings', { method: 'PUT', body: { browser: form.elements.browser.value.trim() } });
    settingsDirty = false;
    notice('Preferência de navegador salva.');
    await refresh();
  });
});
$('#settings-form').addEventListener('input', () => { settingsDirty = true; });

$('#export-catalog').addEventListener('click', event => work(event.currentTarget, async () => {
  const response = await api('/export');
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = `ambientes-${new Date().toISOString().slice(0, 10)}.json`;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}));

$('#import-catalog').addEventListener('click', () => $('#catalog-file').click());
$('#catalog-file').addEventListener('change', event => work($('#import-catalog'), async () => {
  const file = event.target.files[0];
  if (!file) return;
  try {
    if (file.size > 128 * 1024 * 1024) throw new Error('O JSON de cadastros deve ter no máximo 128 MB.');
    const catalog = JSON.parse(await file.text());
    if (!await confirmAction('Importar cadastros?', 'Os cadastros serão mesclados por identificador. Entradas existentes com o mesmo identificador serão atualizadas. Os WebAgents precisam existir nesta instalação. Exporte um backup antes de continuar.', 'Importar cadastros')) return;
    await api('/import', { method: 'POST', body: catalog });
    notice('Cadastros importados. Nenhum arquivo de WebAgent foi alterado.');
    await refresh();
  } finally { event.target.value = ''; }
}));

async function start() {
  try {
    const bootstrap = await (await fetch('/api/bootstrap', { cache: 'no-store', credentials: 'same-origin' })).json();
    if (!bootstrap.token) throw new Error('Não foi possível autenticar no servidor local.');
    token = bootstrap.token;
    $('#local-address').textContent = new URL(bootstrap.localUrl).host;
    await refresh();
  } catch (error) { notice(error.message + ' Confira se o gerenciador está em execução e recarregue a página.', true); }
}
setInterval(() => {
  if (!document.hidden && !busy && !$$('dialog[open]').length && token) refresh().catch(error => notice(error.message + ' Se o gerenciador foi reiniciado, recarregue esta página.', true));
}, 7000);
start();
