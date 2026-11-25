# Plano de releases Windows e evolução Linux

**Objetivo:** distribuir o MultAgent TOTVS em instalador EXE por usuário e ZIP autocontido para Windows x64, com geração local e releases no GitHub.

**Arquitetura aprovada:** Inno Setup 6, publicação .NET 10 autocontida, GitHub Actions acionado por tag `vX.Y.Z`. A versão vem do `.csproj`; a tag deve corresponder exatamente. Nenhum WebAgent, banco ou dado de cliente entra nos pacotes. Linux permanece planejado, não anunciado como suportado.

## Mapa de contexto revisado

| Arquivo | Responsabilidade |
| --- | --- |
| `scripts/Gerar-Release.ps1` | Validar versão, publicar em pasta isolada, compilar instalador, gerar ZIP e SHA-256. |
| `packaging/windows/MultAgentTOTVS.iss` | Instalação em LocalAppData, atalhos, início opcional, atualização e desinstalação preservando dados. |
| `packaging/windows/portable/*` | Iniciar o pacote já publicado sem exigir SDK e orientar parada/atualização. |
| `.github/workflows/release.yml` | Testar, gerar e publicar assets após aprovação do job de build. |
| `tests/release/*` | Contratos do empacotamento, validações do gerador, smoke do ZIP e instalação em runner descartável. |
| `tests/publish-smoke.ps1` | Reutilizar teste do executável em diretório arbitrário. |
| `src/WebAgentManager/Program.cs`, `tests/WebAgentManager.Tests/ApiTests.cs` | Obter versão da API pelo assembly e testar publicação com versão diferente. |
| `README.md`, `docs/releases.md` | Instruções de geração, distribuição, atualização e roteiro Linux. |

Dependências: `.csproj` determina versão e runtime; `Program.cs` preserva a pasta `ProtheusWebAgentManager`; `WindowsPlatform.cs` ainda depende de processos, Authenticode, navegadores e executáveis Windows. Não há migração de banco ou mudança de API necessária. Preservar a correção de cadastro já presente no workspace.

## Tarefas e verificação

1. Escrever testes com `node --test tests/release/*.test.js`; observar falha antes de implementar gerador/instalador/workflow.
2. Criar gerador com rejeição de tag divergente, falha em comando externo, saída já existente e compilador ausente. Publicar somente arquivos de saída do .NET e recursos portáteis explícitos.
3. Criar instalador sem elevação, sem distribuição de agentes, sem exclusão da pasta de dados. Início no login desmarcado por padrão; não encerrar processos `web-agent`.
4. Gerar ZIP, instalador quando ISCC estiver disponível e checksums. Validar extração e execução fora da árvore do projeto, com dados temporários.
5. Criar workflow: testes .NET/JS/UI, Inno Setup no Windows, empacotamento, smoke do instalador, upload; publicar somente em tags válidas após todos os testes. Execução manual gera artifacts, sem publicar release.
6. Documentar uso e limitações. Executar novamente testes .NET, JS e UI, contratos e smoke disponíveis; informar explicitamente validações não realizadas.

## Segurança e atualização

- Pacotes não assinados podem apresentar SmartScreen; assinatura futura requer certificado próprio e segredos do CI.
- Atualizações e desinstalações não removem `%LOCALAPPDATA%\ProtheusWebAgentManager`.
- Fechar o gerenciador e concluir/cancelar preparações antes de atualizar. Não encerrar WebAgents automaticamente.
- Não criar tag, commit, push ou release remota durante a implementação sem solicitação explícita.

## Etapa posterior: Linux

1. Confirmar distribuição oficial e contrato de execução do WebAgent Linux nas versões suportadas.
2. Implementar `LinuxPlatform` e selecionar plataforma; substituir verificação PE/Authenticode, descoberta de navegador, instalação e diretórios padrão.
3. Testar coexistência, abertura de URL, portas e permissões em desktop Linux real; definir distribuições suportadas.
4. Gerar inicialmente `linux-x64.tar.gz` autocontido e launcher; só depois considerar `.deb`, RPM e autostart XDG por usuário.
5. Adicionar runner e matriz Linux apenas após testes de comportamento. Não reutilizar instaladores EXE/MSI no Linux.

## Evidências desta implementação

- 60 testes .NET, 8 testes de contratos/validações de release e 5 testes Playwright aprovados.
- Regressão da versão reproduzida com `-p:Version=0.0.123`: a API inicialmente respondeu `0.1.2`; depois da correção respondeu a versão do assembly e o teste passou.
- ZIP autocontido `artifacts/releases/0.1.2/MultAgent-TOTVS-0.1.2-win-x64-portable.zip` gerado; checksum, conteúdo, versão da API e execução real em pasta com espaços aprovados.
- Revisão técnica identificou a ausência da versão 6.7.4 no Chocolatey. Confirmado HTTP 404 e disponibilidade da 6.7.1; workflow ajustado para a versão disponível.
- Pendente: compilar o EXE com Inno Setup, executar smoke de instalação/desinstalação em runner descartável e executar o workflow no GitHub. Nenhuma release remota foi criada; Linux permanece apenas planejado.
