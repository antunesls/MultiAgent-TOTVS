# Estação Protheus

Gerenciador web **local e por usuário Windows** para organizar clientes, ambientes e instalações do TOTVS WebAgent. Não exige IIS. Projeto independente, sem vínculo com a TOTVS.

## Abrir

Requisitos para desenvolver/publicar: Windows 10/11 x64 e SDK .NET 10. Para executar a publicação padrão, é necessário o runtime ASP.NET Core 10; ele já acompanha o SDK instalado nesta máquina.

1. Execute `Iniciar.cmd` na raiz do projeto. Na primeira execução, ele publica a aplicação.
2. O painel abre em **http://127.0.0.1:17643**.
3. A aplicação continua funcionando depois de fechar a página. Para voltar, execute novamente `Iniciar.cmd`.

Se a política corporativa bloquear scripts PowerShell, solicite a liberação ao administrador. Os scripts não desativam nem contornam essa política.

Publicação manual:

```powershell
.\scripts\Publicar.ps1
# Opcional: inclui o runtime, para distribuir sem instalar .NET na máquina destino.
.\scripts\Publicar.ps1 -Autocontido
```

Executável publicado: `artifacts\app\WebAgentManager.exe`. Mantenha **toda a pasta publicada**, incluindo `wwwroot`, DLLs e demais arquivos. O executável é um aplicativo Windows sem janela de console.

```powershell
# Servidor em segundo plano, sem abrir o painel:
.\artifacts\app\WebAgentManager.exe
# Abrir o painel junto com o servidor:
.\artifacts\app\WebAgentManager.exe --open
# Encerrar apenas o gerenciador publicado neste projeto (não encerra WebAgents):
.\scripts\Parar-Gerenciador.ps1
```

Para desenvolvimento, com logs no terminal:

```powershell
dotnet run --project src/WebAgentManager --no-launch-profile
```

Não execute duas cópias do gerenciador ao mesmo tempo. A porta é exclusiva. `Iniciar.cmd` reutiliza o servidor local já aberto.

## Iniciar no login

Após publicar:

```powershell
.\scripts\Instalar-InicioUsuario.ps1
# Desfazer:
.\scripts\Instalar-InicioUsuario.ps1 -Remover
```

Cria um atalho na pasta Inicializar **do usuário atual**, sem criar serviço ou tarefa com privilégios elevados. No login, o servidor abre em segundo plano; o painel não é aberto automaticamente. A pasta publicada não pode ser movida sem recriar o atalho. **Esta configuração não é ativada automaticamente pelo desenvolvimento.**

## Preparar um WebAgent com EXE/MSI

1. Em **WebAgents → Preparar WebAgent → Instalador EXE / MSI**, dê um nome e selecione um instalador oficial de até 512 MB.
2. Aguarde a validação Authenticode. A assinatura deve estar válida no Windows, com organização TOTVS (ou Microsiga) reconhecida. O hash SHA-256 é exibido; confira-o com a lista oficial da TOTVS quando disponível. O aplicativo calcula o hash, mas não baixa nem compara automaticamente essa lista.
3. Clique em **Abrir instalador** e confirme. O gerenciador exige que todos os processos `web-agent` detectados estejam fechados. Nenhum é encerrado automaticamente.
4. Se existir `%LOCALAPPDATA%\Programs\web-agent`, uma cópia integral será guardada em `backups` **antes** da abertura do instalador. O instalador pode sobrescrever essa pasta padrão; as cópias gerenciadas permanecem separadas.
5. Conclua no Windows, incluindo eventuais confirmações de segurança. Feche o WebAgent recém-iniciado pela bandeja.
6. Clique em **Capturar instalação**. O gerenciador valida o executável instalado, identifica versão/arquitetura e copia a pasta para sua biblioteca, sem alterar a origem.
7. Confira a versão capturada. Crie um ambiente e selecione esse agente.

**Não há extração genérica de EXE/MSI, instalação silenciosa, promessa de instalação portátil ou alteração do AppServer.ini.** O procedimento é assistido conforme o fluxo de instalar/copiar documentado no TDN. Alguns instaladores executam subprocessos e podem retornar antes do término completo: só capture depois de verificar manualmente que a instalação terminou. Códigos de erro/cancelamento impedem captura pelo assistente; 3010/1641 indicam necessidade de reinício, que deve ser resolvida antes de usar o ambiente.

Se escolheu outra pasta durante a instalação, cancele a preparação **após fechar o instalador** e use a aba **Pasta instalada**, informando o caminho que contém `web-agent.exe`. Também é possível importar uma instalação já preparada com suas DLLs. Todos os agentes devem estar fechados para a cópia. Links simbólicos/junctions são recusados.

Preparações e identificadores de processos são mantidos em memória. Se reiniciar o gerenciador durante uma preparação, conclua/feche o instalador e use **Pasta instalada**; não abra um segundo instalador em paralelo. Os backups continuam no disco e no histórico.

### Restaurar a instalação padrão

Se for necessário desfazer uma instalação, feche todos os WebAgents e instaladores. Localize o caminho do backup no histórico. Renomeie a pasta padrão atual para preservá-la e copie o backup completo para `%LOCALAPPDATA%\Programs\web-agent`. Isso restaura os arquivos, **não** os registros, certificados ou entradas de desinstalação alterados pelo instalador; para estes, utilize o instalador oficial correspondente. O aplicativo não faz rollback automático do Windows.

## Cadastrar e abrir ambientes

- Informe cliente, nome, tipo, **URL completa** (`http(s)://IP-ou-dominio:porta/webapp/`) e WebAgent.
- Parâmetros de URL como `?E=ambiente&P=SIGAMDI` são preservados. Não inclua credenciais; usuário/senha na autoridade da URL são recusados. Não salve segredos em parâmetros ou observações.
- Escolha um navegador no ambiente ou configure uma preferência geral. Há sugestões dos executáveis locais de Edge, Chrome, Firefox e Brave; a homologação depende da versão do WebApp, não deste aplicativo.
- Deixe o navegador vazio para usar o padrão do Windows, permitido a partir do WebAgent **1.0.24**. Versões anteriores exigem um navegador explícito.
- Este gerenciador exige WebAgent **1.0.16 ou superior** e versão identificável no executável para usar o fluxo de coexistência documentado.
- Na primeira abertura, cada ambiente recebe **sua própria cópia** em `instances`. A chamada é `web-agent.exe launch URL --browser=CAMINHO`, usando argumentos separados, sem shell.
- A **porta do WebApp** faz parte do cadastro. A **porta local do WebAgent** é escolhida automaticamente pelo próprio `launch`.
- Produção tem identificação visual e confirmação antes de abrir. Favoritos, busca e edição estão disponíveis.
- A exclusão de cadastro **não apaga pastas nem encerra sessões**. Arquivos não utilizados ficam preservados; a versão inicial não possui coletor automático de lixo ou exclusão de agentes.

Abra os ambientes pelo gerenciador. A TOTVS recomenda não misturar `launch` com acesso direto ao WebApp na mesma estação, para evitar conflito de portas. Perfis/cookies/senhas do navegador **não são isolados** por cliente.

### Sessões, atualizações e limites

“Processo aberto” indica apenas que o processo iniciado ainda está vivo: não confirma login, handshake ou uso de periféricos. Alguns executáveis podem delegar a execução a subprocessos, limitando o rastreio pelo PID inicial. A conexão deve ser conferida no WebApp. Não há encerramento forçado pelo painel.

A TOTVS informa que o agente em `launch` pode levar 2–3 minutos para se encerrar após o fechamento do navegador. Sessões anteriores à inicialização do gerenciador não são recuperadas; agentes externos ainda são detectados para bloquear instalações.

O gerenciador **não desativa atualizações solicitadas pelo servidor**. Se o hash do executável de uma instância mudar, a próxima abertura será bloqueada. Prepare explicitamente a versão nova e altere o vínculo do ambiente; uma nova instância será criada. DLLs e arquivos adicionais são copiados, mas não têm integridade/compatibilidade verificadas individualmente. Teste o comportamento de atualização real de cada cliente.

## Dados e segurança

Pasta padrão: `%LOCALAPPDATA%\ProtheusWebAgentManager`.

```text
manager.db       Cadastros, preferências e histórico SQLite
agents/          Cópias de referência identificadas por ID
instances/       Cópias exclusivas por ambiente e agente
installers/      Instaladores enviados e verificados
backups/         Cópias da instalação padrão antes de instalar
```

- Servidor restrito a **127.0.0.1**, com validação estrita de Host, Origin, Fetch Metadata e token em todas as APIs de dados/ação. Sem CORS, CDN, telemetria ou download automático de programas.
- O token protege contra páginas de outros sites. **Não é autenticação contra outros processos/usuários locais** que consigam acessar a porta; o produto foi projetado para uma estação de uso pessoal, não como serviço multiusuário hostil. Não publique a porta via IIS/reverse proxy, túnel ou encaminhamento.
- Usa a sessão e as permissões do usuário atual. Não execute o gerenciador como administrador; o instalador pode solicitar elevação separadamente.
- O bloqueio de preparação é conservador: qualquer processo `web-agent` detectado na máquina impede instalação/captura, inclusive de outra sessão Windows.
- Assinatura digital é conferida antes de importar/iniciar e novamente no executável da instância. Não há desativação de antivírus ou certificados.
- Certificados TOTVS e permissões de acesso a serviços/rede local do navegador precisam ser autorizados manualmente. O aplicativo não contorna avisos.
- SQLite usa consultas parametrizadas. Textos da interface são escapados antes da renderização.
- Limite de 2.000 ambientes, aplicado tanto na criação quanto na mesclagem de importação. O JSON aceita até 128 MB, suficiente para exportações com todos os campos nos limites permitidos; caminhos de navegadores têm limite de 1.024 caracteres.
- **Exportar cadastros** gera JSON de ambientes/preferências, não um backup dos binários. **Importar** mescla IDs e valida tudo antes de gravar; não substitui pastas. IDs de agentes precisam existir na mesma instalação. Para migrar de máquina, prefira backup completo da pasta de dados, com o gerenciador fechado; caminhos absolutos de agentes precisarão permanecer válidos (migração automática de caminhos não está implementada).

Configuração opcional por variáveis de ambiente, antes de iniciar:

```powershell
$env:WAM_PORT = '17643' # 1024 a 65535
$env:WAM_DATA_DIR = 'C:\MinhaPasta\DadosWebAgents' # pasta local, exclusiva do usuário
```

Não utilize uma pasta compartilhada/rede. O diretório padrão em LocalAppData é o recomendado.

## Testes

```powershell
dotnet test tests/WebAgentManager.Tests
dotnet build src/WebAgentManager -c Release
.\tests\publish-smoke.ps1 # depois de publicar
npm ci
npm run check:js
npm run test:ui
```

Os testes de interface usam **Edge instalado**, em modo headless, porta 17644 e uma pasta temporária de dados independente. Eles não instalam programas, não executam agentes, não encerram processos do usuário e não alteram os cadastros reais. Um caso testa o painel/API reais e recusa de assinatura; outro simula a API para testar cadastro visual, escape de HTML e confirmação de produção; o terceiro verifica preservação de preferências não salvas durante atualização automática. O teste da publicação usa a porta 17645 e encerra somente o processo de teste que ele próprio iniciou. Diretórios temporários e evidências ficam disponíveis para inspeção; nenhuma limpeza ampla é realizada.

## Matriz de entrega e validação

| Requisito | Situação |
|---|---|
| Plano e mapa de arquivos | Implementados em `docs/plans/2026-11-24-webagent-manager.md` |
| Clientes/ambientes, URL com IP e porta, versão/arquitetura, navegador | Implementados |
| Painel local e dados por usuário, sem IIS | Implementados |
| Importação assistida EXE/MSI e pasta instalada | Implementada; teste com instaladores oficiais reais pendente |
| Backup anterior à instalação e bloqueio com agentes abertos | Implementados, com testes automatizados |
| Cópias independentes e launch sem encerrar outros ambientes | Implementados; fronteira de processos simulada nos testes de unidade |
| Busca, favoritos, edição, identificação e confirmação de produção | Implementados |
| Histórico, diagnóstico e exportação/importação | Implementados |
| Início no login | Script disponibilizado; não ativado automaticamente |
| Simultaneidade entre versões reais de clientes | **Pendente**: exige dois instaladores e duas URLs acessíveis |
| Integração com Office, DLLs, certificados e atualizações do AppServer | **Pendente** de validação nos ambientes reais |
| Isolamento de cookies, credenciais e perfis de navegador | Fora do escopo desta versão |
| Extração/instalação silenciosa e rollback completo de instaladores | Não implementados; preparação assistida e backup de arquivos |

### Roteiro de aceite real

1. Preparar dois agentes oficiais de versões diferentes; conferir versão, assinatura, arquitetura e DLLs.
2. Vincular cada um à URL correta; abrir ambos pelo painel, conferir processos e portas locais distintas.
3. Em cada cliente, realizar operação que efetivamente acione o WebAgent (arquivo local, Office ou DLL compatível).
4. Fechar um ambiente e confirmar que o outro continua funcionando.
5. Validar versão exigida e comportamento de atualização automática no cliente, sem comprometer outras cópias.
6. Testar reinício do Windows após habilitar o atalho de início por usuário.

**A aprovação dos testes automatizados não comprova a simultaneidade real do Protheus.** Nenhum instalador ou URL de cliente foi disponibilizado durante a implementação.

## Referências

- [TOTVS — WebApp / WebAgent](https://tdn.totvs.com/display/tec/2.+WebApp+-+WebAgent): `launch`, portas aleatórias, coexistência x86/x64, certificados, hash e compatibilidade.
- [FBS — Alternância de agentes](https://fbsconsultoria.com.br/erp-totvs-protheus/framework/web-agent-protheus-pedindo-reinstalar-varios-clientes/): contexto do problema. O espelhamento sobre uma pasta ativa **não** é utilizado aqui.
- [Publicação de Luciana Koch](https://www.linkedin.com/posts/lucianakoch-analistadenegocios_cansado-de-reinstalar-o-webagent-toda-vez-share-7491272122695335936-S9Jm/): pastas separadas e abertura por `launch`.
