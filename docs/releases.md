# Distribuição e releases

## Pacotes Windows

Os dois pacotes são **Windows x64, autocontidos em .NET 10**: o destino não precisa de SDK, runtime separado ou IIS.

- `MultAgent-TOTVS-X.Y.Z-win-x64-setup.exe`: instalador Inno Setup por usuário, sem solicitar administrador, com entrada de desinstalação, atalho no menu Iniciar e opções de atalho no desktop e início no login.
- `MultAgent-TOTVS-X.Y.Z-win-x64-portable.zip`: extraia **todo** o conteúdo em uma pasta e execute `Iniciar.cmd`. Não execute dentro do visualizador de ZIP.
- `SHA256SUMS.txt`: SHA-256 dos pacotes daquela release.

O gerenciador instalado fica em `%LOCALAPPDATA%\Programs\MultAgent TOTVS`. Os dados continuam em `%LOCALAPPDATA%\ProtheusWebAgentManager`, inclusive no pacote portátil. **Portátil não significa dados ao lado do executável.** Usar ZIP e instalador não cria cadastros separados; ambos acessam a mesma pasta de dados por padrão. Não abra duas cópias ao mesmo tempo.

O início no login é opcional e vem desmarcado em uma instalação nova; executa somente o servidor, sem abrir navegador. Não há serviço Windows nem tarefa elevada. Atalhos de abertura usam o launcher para reutilizar o servidor já aberto. A política PowerShell da empresa é respeitada. Se ela impedir o launcher, solicite liberação ao administrador; é possível iniciar `WebAgentManager.exe` diretamente e acessar `http://127.0.0.1:17643`.

**WebAgents e instaladores TOTVS não são redistribuídos.** Cada usuário deve obter os pacotes oficiais e prepará-los no painel. Nenhum banco, cadastro, credencial ou arquivo de cliente deve ser anexado à release.

## Gerar localmente

Requisitos do computador de build:

1. Windows x64 e SDK .NET 10.
2. [Inno Setup 6](https://jrsoftware.org/isdl.php), instalado separadamente. O workflow fixa a versão [6.7.1 disponível no Chocolatey](https://community.chocolatey.org/packages/innosetup/6.7.1); a versão mais recente do site oficial pode ainda não estar publicada nesse repositório. Revise os termos de licença, especialmente em uso comercial.
3. Node.js e `npm ci` para os testes; Edge para Playwright.

Execute na raiz:

```powershell
dotnet test tests/WebAgentManager.Tests -c Release
npm run check:js
npm run test:release
npm run test:ui

# EXE + ZIP + SHA256SUMS.txt
.\scripts\Gerar-Release.ps1

# Caso o compilador não esteja no caminho padrão:
.\scripts\Gerar-Release.ps1 -IsccPath 'C:\Ferramentas\Inno Setup 6\ISCC.exe'
```

A saída é `artifacts\releases\X.Y.Z\`. O script **não sobrescreve** um diretório existente. Para repetir a geração, escolha um destino novo com `-OutputDirectory`. A publicação intermediária é isolada de `artifacts\app`, portanto não substitui o executável em uso no desenvolvimento.

Sem Inno Setup é possível gerar **apenas o ZIP**, explicitamente:

```powershell
.\scripts\Gerar-Release.ps1 -SomenteZip -OutputDirectory artifacts\releases\teste-zip
.\tests\release\archive-smoke.ps1 -ReleaseDirectory artifacts\releases\teste-zip
```

O gerador não executa a suíte de testes nem envia arquivos ao GitHub. Na geração completa, valide o diretório produzido com `archive-smoke.ps1`. Esse teste confere os hashes, arquivos do runtime e inicialização real em pasta com espaços, usando dados temporários e porta própria.

O teste `installer-smoke.ps1` é **restrito a runners descartáveis do GitHub Actions**, pois instala, cria atalhos no perfil e altera o registro HKCU. Ele testa instalação, execução, reinstalação da mesma versão e desinstalação, verificando preservação de um arquivo de dados. Não comprova migração entre todas as versões anteriores nem assinatura digital.

## Publicar pelo GitHub

O workflow `.github/workflows/release.yml` tem dois modos:

- **Actions → Release Windows → Run workflow**: executa testes e empacotamento, e disponibiliza os arquivos como artifact por 14 dias. **Não cria release pública.**
- **Push de tag `vX.Y.Z`**: após todos os testes e smokes passarem, cria uma GitHub Release com instalador, ZIP, checksums e notas automáticas.

Antes de publicar:

1. Atualize `<Version>` em `src/WebAgentManager/WebAgentManager.csproj` e a versão informada no README. A API obtém sua versão do assembly; não exige editar `Program.cs`.
2. Revise o código e confirme que não há dados sensíveis versionados.
3. Faça o commit das alterações revisadas e envie a branch para o GitHub.
4. Crie e envie uma tag nova que corresponda **exatamente** à versão do `.csproj`. Exemplo, somente depois de definir a versão `0.1.3` e confirmar que a tag ainda não existe:

```powershell
git tag -a v0.1.3 -m 'Release 0.1.3'
git push origin v0.1.3
```

Não mova tags de releases já distribuídas. Para corrigir uma release, publique uma nova versão. O workflow não sobrescreve uma release existente. O job de build tem somente leitura; apenas o job final de publicação recebe `contents: write`, utilizando o token temporário do GitHub. Nenhum PAT é necessário.

## Conferir um download

Na pasta em que baixou os arquivos:

```powershell
Get-FileHash .\MultAgent-TOTVS-0.1.3-win-x64-setup.exe -Algorithm SHA256
```

Compare o resultado com a linha correspondente em `SHA256SUMS.txt` da release correta. O checksum detecta alteração/corrupção, mas não substitui assinatura ou confiança na origem.

Os pacotes ainda não têm assinatura Authenticode. O Windows pode apresentar SmartScreen ou editor desconhecido. Não desative proteções; valide a origem conforme a política da empresa. Assinar releases exige certificado apropriado e uma etapa futura de assinatura **antes** da geração dos hashes. A assinatura dos WebAgents TOTVS continua sendo validada independentemente pelo aplicativo.

## Atualização e remoção

1. Conclua ou cancele preparações de instalador abertas no painel.
2. Exporte os cadastros; com o gerenciador fechado, faça backup completo da pasta de dados.
3. Execute `Parar-Gerenciador.ps1` na pasta do pacote com PowerShell. O script encerra apenas `WebAgentManager.exe` daquela pasta, não os processos `web-agent`.
4. Instale o novo EXE no mesmo perfil ou extraia o novo ZIP em uma pasta nova. Evite trocar entre pastas com versões abertas.
5. Inicie o gerenciador e recarregue o painel: o token local muda a cada execução.

O instalador pode pedir autorização para fechar o gerenciador se seus arquivos estiverem em uso. Não usa `taskkill` nem encerra os WebAgents de clientes. As preparações e o acompanhamento de sessões ficam em memória, por isso devem ser concluídos antes da atualização.

Desinstalar remove o aplicativo e seus atalhos, **não** remove `%LOCALAPPDATA%\ProtheusWebAgentManager`, nem instalações TOTVS ou cópias portáteis. A exclusão dos dados deve ser uma decisão manual após backup. Não há downgrade ou migração de banco automatizados nesta etapa.

## Roteiro Linux — ainda não suportado

1. Validar versões e distribuição oficial do WebAgent Linux, parâmetros de execução, requisitos de desktop e mecanismos de verificação de integridade/origem.
2. Implementar `LinuxPlatform` atrás de `IPlatform`, substituindo Authenticode/PE, pastas padrão, instalação EXE/MSI e descoberta de navegadores. A classe atual `WindowsPlatform` não é portável apenas trocando o RID.
3. Separar dados/configuração conforme XDG e definir permissões; testar migração dos cadastros sem presumir que caminhos Windows funcionem em Linux.
4. Homologar abertura de múltiplos ambientes, processos, portas e integrações em distribuições desktop definidas.
5. Gerar inicialmente `linux-x64.tar.gz` autocontido e launcher; avaliar `.deb`/RPM e autostart XDG por usuário depois. A publicação autocontida não elimina dependências nativas do sistema.
6. Somente então adicionar Linux à matriz de testes e releases. O job Ubuntu do workflow atual apenas publica arquivos no GitHub; não representa suporte do aplicativo a Linux.
