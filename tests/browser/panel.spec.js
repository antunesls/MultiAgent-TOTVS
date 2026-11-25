const { test, expect } = require('@playwright/test');

test('painel local, navegação, formulários e assinatura inválida', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await expect(page).toHaveTitle('MultAgent TOTVS · WebAgents');
  await expect(page.getByRole('link', { name: 'MultAgent TOTVS, início' })).toBeVisible();
  await expect(page.getByText('Conectado localmente')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Seus ambientes' })).toBeVisible();
  await expect(page.getByText('Seu próximo cliente começa aqui')).toBeVisible();
  await page.screenshot({ path: 'test-results/painel-desktop.png', fullPage: true });

  await page.getByRole('button', { name: 'Novo ambiente' }).click();
  await expect(page.getByRole('dialog', { name: '' }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Pasta instalada', exact: true }).click();
  await page.locator('#folder-form [name=name]').fill('Teste sem agente');
  await page.locator('#folder-form [name=path]').fill('C:\\pasta-inexistente-wam');
  await page.getByRole('button', { name: 'Importar pasta', exact: true }).click();
  await expect(page.locator('#folder-error')).toContainText(/não contém web-agent\.exe|Feche os WebAgents pela bandeja/);
  await page.getByRole('button', { name: 'Instalador EXE / MSI' }).click();
  await page.locator('#installer-form [name=name]').fill('Instalador inválido de teste');
  await page.locator('#installer-form [name=file]').setInputFiles({ name: 'unsigned.exe', mimeType: 'application/octet-stream', buffer: Buffer.from('Arquivo de teste; não é executável.') });
  const uploadResponse = page.waitForResponse(response => response.url().includes('/api/preparations/upload') && response.request().method() === 'POST', { timeout: 65000 });
  await page.getByRole('button', { name: 'Verificar instalador' }).click();
  expect((await uploadResponse).status()).toBe(400);
  // O provedor Authenticode pode recusar o formato antes de devolver NotSigned.
  await expect(page.locator('#installer-error')).toContainText(/assinatura TOTVS válida exigida|O Windows não conseguiu verificar a assinatura digital do arquivo\./, { timeout: 65000 });
  const bootstrap = await (await page.request.get('/api/bootstrap')).json();
  const rejectedState = await (await page.request.get('/api/state', { headers: { 'X-WAM-Token': bootstrap.token } })).json();
  expect(rejectedState.preparations).toEqual([]);
  expect(rejectedState.agents).toEqual([]);
  await page.locator('#agent-dialog [aria-label=Fechar]').click();

  await page.getByRole('button', { name: 'Configurações', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Diagnóstico local' })).toBeVisible();
  await page.getByRole('button', { name: 'Salvar preferência' }).click();
  await expect(page.getByRole('status')).toContainText('Preferência de navegador salva');
  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exportar cadastros' }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/^ambientes-.*\.json$/);
  await page.getByRole('button', { name: 'Histórico', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Histórico de operações' })).toBeVisible();
  await page.getByRole('button', { name: /Sessões/ }).click();
  await expect(page.getByRole('heading', { name: 'Nenhuma sessão iniciada' })).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: /Ambientes/ }).click();
  await expect(page.getByRole('heading', { name: 'Seus ambientes' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/painel-mobile.png', fullPage: true });
  expect(errors).toEqual([]);
});

test('formulário de ambiente e confirmação de produção (API simulada, sem executar programas)', async ({ page }) => {
  const agent = { id: 'agent-ui-test', name: 'Cliente Alfa — 1.1.1', version: '1.1.1', architecture: 'x64', directory: 'C:\\Agentes\\teste', sha256: 'teste' };
  const fixture = { environments: [], agents: [agent], sessions: [], preparations: [], history: [], settings: { browser: '' }, browsers: [], diagnostics: { user: 'Teste', defaultAgentDirectory: 'C:\\Agentes', simultaneousValidation: 'Teste visual simulado' } };
  let launched = false;
  await page.route('**/api/state', route => route.fulfill({ json: fixture }));
  await page.route('**/api/environments', async route => {
    const entry = route.request().postDataJSON();
    fixture.environments = [{ ...entry, id: 'ui-test' }];
    await route.fulfill({ json: fixture.environments[0] });
  });
  await page.route('**/api/environments/ui-test/launch', route => { launched = true; return route.fulfill({ json: {} }); });
  await page.goto('/');
  await expect(page.getByText('Conectado localmente')).toBeVisible();
  await page.getByRole('button', { name: 'Novo ambiente' }).click();
  await page.locator('#environment-form [name=client]').fill('Cliente Alfa');
  await page.locator('#environment-form [name=name]').fill('Produção');
  await page.locator('#environment-form [name=kind]').selectOption('Produção');
  await page.locator('#environment-form [name=url]').fill('https://cliente-alfa:8080/webapp/');
  await page.locator('#environment-form [name=notes]').fill('<img src=x onerror=alert(1)>');
  await page.getByRole('button', { name: 'Salvar ambiente' }).click();
  await expect(page.locator('.environment-name')).toHaveText('Produção');
  await expect(page.locator('.environment-notes')).toHaveText('<img src=x onerror=alert(1)>');
  await expect(page.locator('.environment-notes img')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/ambiente-preenchido.png', fullPage: true });
  await page.getByRole('button', { name: 'Abrir', exact: true }).click();
  await expect(page.locator('#confirm-dialog')).toBeVisible();
  expect(launched).toBe(false);
  await page.locator('#confirm-dialog').getByRole('button', { name: 'Cancelar' }).click();
  expect(launched).toBe(false);
  await page.getByRole('button', { name: 'Abrir', exact: true }).click();
  await page.locator('#confirm-dialog').getByRole('button', { name: 'Abrir produção' }).click();
  await expect.poll(() => launched).toBe(true);
  await page.route('**/api/agents/agent-ui-test/refresh', route => {
    agent.version = '1.1.2';
    return route.fulfill({ json: agent });
  });
  await page.getByRole('button', { name: 'WebAgents', exact: true }).click();
  await page.getByRole('button', { name: 'Atualizar identificação' }).click();
  await expect(page.locator('.agent-card .badge').first()).toHaveText('1.1.2');
  await expect(page.getByRole('status')).toContainText('Arquivos e vínculos preservados');
});

for (const origin of ['Novo ambiente', 'Criar ambiente']) {
  test(`novo cadastro após edição não reutiliza identificador anterior (${origin})`, async ({ page }) => {
    const original = { id: '11111111111111111111111111111111', client: 'LOCAL', name: 'TOTVS12', kind: 'Desenvolvimento', url: 'http://localhost:4321/', agentId: 'agent-test', browser: '', notes: 'Original', favorite: true };
    const fixture = { environments: [{ ...original }], agents: [{ id: 'agent-test', name: 'CPQ Produção', version: '1.1.1', architecture: 'x64' }], sessions: [], preparations: [], history: [], settings: {}, browsers: [], diagnostics: {} };
    const submissions = [];
    await page.route('**/api/state', route => route.fulfill({ json: fixture }));
    await page.route('**/api/environments', async route => {
      const entry = route.request().postDataJSON();
      submissions.push(entry);
      const saved = { ...entry, id: entry.id || '22222222222222222222222222222222' };
      const index = fixture.environments.findIndex(item => item.id === saved.id);
      if (index === -1) fixture.environments.push(saved);
      else fixture.environments[index] = saved;
      await route.fulfill({ json: saved });
    });
    await page.goto('/');
    await page.getByRole('button', { name: 'Editar TOTVS12', exact: true }).click();
    await page.locator('#environment-form [name=notes]').fill('Editado');
    await page.getByRole('button', { name: 'Salvar ambiente' }).click();
    await expect(page.locator('#environment-dialog')).not.toBeVisible();
    expect(submissions[0].id).toBe(original.id);
    await expect(page.locator('.environment-notes')).toHaveText('Editado');

    if (origin === 'Criar ambiente') await page.getByRole('button', { name: 'WebAgents', exact: true }).click();
    if (origin === 'Novo ambiente') await page.locator('#new-environment').click();
    else await page.getByRole('button', { name: origin, exact: true }).click();
    await expect(page.locator('#environment-title')).toHaveText('Novo ambiente');
    await expect(page.locator('#environment-form [name=id]')).toHaveValue('');
    await expect(page.locator('#environment-form [name=notes]')).toHaveValue('');
    await expect(page.locator('#environment-form [name=favorite]')).not.toBeChecked();
    await page.locator('#environment-form [name=client]').fill('LOCAL');
    await page.locator('#environment-form [name=name]').fill('Outro ambiente');
    await page.locator('#environment-form [name=url]').fill('http://localhost:5432/');
    await page.getByRole('button', { name: 'Salvar ambiente' }).click();
    await expect(page.locator('.environment-row')).toHaveCount(2);
    expect(submissions[1].id).toBe('');
    expect(fixture.environments[0]).toEqual({ ...original, notes: 'Editado' });
    await page.reload();
    await expect(page.locator('.environment-row')).toHaveCount(2);
    await expect(page.getByRole('heading', { name: 'TOTVS12', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Outro ambiente', exact: true })).toBeVisible();
  });
}

test('atualização automática preserva preferência não salva após perder foco', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText('Conectado localmente')).toBeVisible();
  await page.getByRole('button', { name: 'Configurações', exact: true }).click();
  const input = page.locator('#settings-form [name=browser]');
  await input.fill('C:\\PreferenciaNaoSalva\\chrome.exe');
  await page.getByRole('heading', { name: 'Configurações', exact: true }).click();
  await page.waitForResponse(response => response.url().endsWith('/api/state') && response.status() === 200, { timeout: 12000 });
  await expect(input).toHaveValue('C:\\PreferenciaNaoSalva\\chrome.exe');
});
