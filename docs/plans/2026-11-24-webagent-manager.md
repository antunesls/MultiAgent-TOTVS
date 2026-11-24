# Gerenciador de WebAgents — plano de implementação

> Execução: seguir a skill executing-plans, com testes antes da implementação e validação por etapa.

**Objetivo:** gerenciar ambientes Protheus por usuário Windows e abrir simultaneamente instalações isoladas do WebAgent.

**Arquitetura:** aplicação ASP.NET Core local, executada na sessão do usuário, servindo painel HTML/CSS/JavaScript e API no loopback. SQLite em LocalApplicationData guarda os cadastros. Instaladores são preparados de forma assistida; nenhuma extração ou instalação silenciosa é presumida.

**Tecnologias:** .NET 10, ASP.NET Core, Microsoft.Data.Sqlite, xUnit, HTML/CSS/JavaScript sem CDN.

## Contexto e arquivos

Workspace inicialmente vazio, sem Git, padrões prévios ou testes. Não há worktree ou commits a realizar. Nenhum fonte AdvPL/TLPP será gerado.

| Arquivo | Responsabilidade |
|---|---|
| src/WebAgentManager/Program.cs | Servidor loopback, proteção da API, rotas e arquivos estáticos |
| src/WebAgentManager/Models.cs | Contratos, validações, construção segura dos argumentos |
| src/WebAgentManager/Store.cs | SQLite, cadastros, histórico e backup lógico |
| src/WebAgentManager/AgentService.cs | Cópias isoladas, inspeção, instalação assistida e sessões |
| src/WebAgentManager/WindowsPlatform.cs | Assinatura digital, processos e navegadores Windows |
| src/WebAgentManager/wwwroot/* | Interface e assistente em português |
| tests/WebAgentManager.Tests/* | Validação, persistência, cópia e proteção HTTP |
| scripts/* | Publicação, execução e início no login opcional |
| README.md | Operação, limitações e critérios de aceite reais |

## Decisões e riscos

- Acesso apenas em 127.0.0.1; validação de Host/Origin, token de sessão em todas as APIs e sem CORS. Sem API de shell arbitrário.
- Versão/arquitetura identificadas por metadados PE; assinatura Authenticode deve ser válida e emitida para TOTVS antes da execução/importação.
- Cada ambiente recebe cópia própria dos arquivos. Origens e destinos com reparse points são rejeitados. Exclusão de cadastro não apaga arquivos automaticamente.
- Preparação: importar EXE/MSI → verificar assinatura/hash → confirmar instalação → fazer backup da instalação padrão → abrir instalador interativo → usuário fecha agente recém-iniciado → capturar instalação → vincular ambiente.
- Preparação fica bloqueada se qualquer web-agent estiver em execução ou outra preparação estiver pendente. Não encerrar processos globalmente.
- O comando launch é sempre o primeiro argumento, seguido de URL e --browser opcional, via ArgumentList, sem cmd.exe.
- Não prometer isolar credenciais/cookies do navegador nem controlar atualizações do AppServer. Instalações antigas permanecem disponíveis; não alterar AppServer.ini.
- Sem instaladores reais/URLs disponíveis, os testes reais de assinatura, instalação, DLLs e simultaneidade são pendências explícitas, não resultados presumidos.

## Direção visual

Paleta: azul petróleo #15394a, azul ação #176b98, fundo #f3f6f8, branco #ffffff, texto #203644, produção #a53f35. Tipografia Segoe UI para operação Windows, Cascadia Mono somente para caminhos e URLs. Layout com navegação lateral e lista de ambientes com busca; sessão ativa possui indicador próprio. Sem fontes remotas, telemetria ou exemplos fictícios salvos como cadastros reais. Teclado, foco visível, diálogos com labels e layout responsivo.

## Tarefa 1 — Domínio e segurança dos argumentos

1. Criar projetos e testes de URL HTTP/HTTPS, campos obrigatórios, porta inválida, argumentos launch e caminhos de navegador.
2. Executar `dotnet test tests/WebAgentManager.Tests` e confirmar falha pela ausência do comportamento.
3. Implementar Models.cs, sem dependência do Windows para validação.
4. Reexecutar testes: todos os casos do domínio devem passar.

## Tarefa 2 — Persistência e cópias isoladas

1. Escrever testes para CRUD SQLite, edição preservando identificador, referência inválida, exportação/importação com validação atômica e cópia sem alteração da origem.
2. Executar testes e observar falhas antes de implementar Store.cs/AgentService.cs.
3. Implementar esquema inicial, consultas parametrizadas, histórico e cópias para diretórios novos.
4. Reexecutar testes; verificar que cópias por ambiente são distintas e recusam links de sistema de arquivos.

## Tarefa 3 — API e assistente Windows

1. Escrever testes HTTP: Host externo, Origin externo, token ausente, CRUD autenticado e execução inválida recusada.
2. Confirmar falhas; implementar Program.cs e WindowsPlatform.cs, injetando fronteiras do sistema operacional.
3. Executar testes e `dotnet build src/WebAgentManager` sem avisos.
4. Assistente não instalará nada durante testes. Confirmar bloqueios e backup por testes com plataforma substituta.

## Tarefa 4 — Painel funcional

1. Implementar ambientes, agentes, sessões, histórico e configurações; formulários reais, busca, edição e favoritos.
2. Implementar importação assistida com consentimento explícito e mensagens de bloqueio.
3. Validar recursos HTTP, sintaxe JavaScript e fluxo de CRUD com servidor temporário e armazenamento isolado.
4. Se navegador de teste estiver disponível, verificar renderização, interação, diálogos e responsividade.

## Tarefa 5 — Distribuição e aceite

1. Adicionar scripts de publicação e início no login por usuário, apenas por ação explícita do usuário.
2. Documentar preparação, restauração manual, backups, segurança e diferenças entre porta WebApp/porta WebAgent.
3. Executar `dotnet test tests/WebAgentManager.Tests`, `dotnet build src/WebAgentManager -c Release` e publicação local.
4. Apresentar matriz de requisitos: implementado, pendente de teste real ou limitação documentada.

## Aceite real obrigatório (pendente de insumos)

- Preparar dois instaladores reais com assinatura válida e versões distintas.
- Abrir os dois ambientes via launch e conferir caminho, versão, arquitetura e portas diferentes.
- Executar acesso local/Office/DLL em cada ambiente; fechar um e confirmar funcionamento do outro.
- Validar certificados e permissões de acesso local dos navegadores sem desabilitar proteções.
- Testar atualização solicitada pelo servidor: confirmar que não corrompe outros ambientes e documentar qualquer limitação encontrada.

## Fonte oficial

https://tdn.totvs.com/display/tec/2.+WebApp+-+WebAgent

O TDN documenta launch com portas aleatórias, cópia da instalação para coexistência x86/x64 (a partir de 1.0.16), navegador padrão a partir de 1.0.24, certificados e assinatura/hash. O exemplo de arquiteturas não substitui testes entre as versões específicas de clientes.

## Registro da execução

- [x] Tarefa 1: domínio, URLs, argumentos e compatibilidade mínima de versão.
- [x] Tarefa 2: SQLite, CRUD, importação atômica, cópias isoladas e proteção contra reparse points.
- [x] Tarefa 3: API protegida, assinatura Authenticode, backup e instalação assistida, sessões.
- [x] Tarefa 4: painel, formulários, favoritos, confirmação de produção, diagnóstico e testes no Edge desktop/mobile.
- [x] Tarefa 5: scripts de distribuição/início no login, README, publicação e teste do executável.
- [ ] Aceite real: dois instaladores oficiais e URLs de clientes, integrações locais e atualizações do AppServer. Insumos ainda não disponibilizados.

### Evidências automatizadas

- `dotnet test tests/WebAgentManager.Tests`: 47 testes aprovados, sem ignorados.
- `npm run test:ui`: 3 testes aprovados em Edge headless; um cenário usa API simulada explicitamente para testar cadastro/produção sem executar agentes.
- `npm run check:js`: sintaxe válida.
- `scripts/Publicar.ps1`: publicação Windows x64 em `artifacts/app`.
- `tests/publish-smoke.ps1`: executável iniciado fora da pasta do projeto, painel/JavaScript/API respondendo e dados temporários isolados.
- `dotnet list src/WebAgentManager package --vulnerable --include-transitive`: nenhuma vulnerabilidade conhecida nas fontes consultadas.

### Revisão e ajustes

- Atualizada SQLitePCLRaw para 2.1.14 após alerta de vulnerabilidade na dependência transitiva original.
- Cópia passa por diretório temporário exclusivo e promoção por rename; falhas não deixam destino parcial utilizável.
- Cancelamento/erro do instalador impede captura automática; preparação concorrente é bloqueada.
- IDs de ambientes normalizados para GUID minúsculo; colisões por caixa recusadas antes de importar.
- Limite de 2.000 cadastros consistente entre criação/importação; interface aceita JSON de até 128 MB, cobrindo exportações válidas nos limites dos campos.
- Preferência de navegador não salva é preservada durante polling.
- Handles de processos encerrados são liberados, preservando estado final; encerrar o gerenciador não encerra agentes.
- Nenhum agente existente do usuário foi encerrado, nenhum instalador real foi executado e início no login não foi ativado.
