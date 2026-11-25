param([string]$ApplicationDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $ApplicationDirectory) { $ApplicationDirectory = Join-Path $root 'artifacts\app' }
$executable = Join-Path $ApplicationDirectory 'WebAgentManager.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Publique o aplicativo antes deste teste.' }
$originalPort = $env:WAM_PORT
$originalData = $env:WAM_DATA_DIR
$env:WAM_PORT = '17645'
$env:WAM_DATA_DIR = Join-Path ([IO.Path]::GetTempPath()) ('wam-publish-' + [Guid]::NewGuid().ToString('N'))
$process = $null
try {
    $process = Start-Process -FilePath $executable -WorkingDirectory $env:TEMP -PassThru
    $bootstrap = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($process.HasExited) { throw "O executavel terminou antes de iniciar o servidor: $($process.ExitCode)" }
        try { $bootstrap = Invoke-RestMethod 'http://127.0.0.1:17645/api/bootstrap' -TimeoutSec 1; break }
        catch { Start-Sleep -Milliseconds 150 }
    }
    if (-not $bootstrap -or $bootstrap.product -ne 'MultAgentTOTVS' -or $bootstrap.name -ne 'MultAgent TOTVS') { throw 'Servidor publicado nao respondeu corretamente.' }
    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'src\WebAgentManager\WebAgentManager.csproj') -Raw
    if ($bootstrap.version -ne [string]$project.Project.PropertyGroup.Version) { throw 'Versao da API diverge da versao do projeto.' }
    $headers = @{ 'X-WAM-Token' = $bootstrap.token }
    $state = Invoke-RestMethod 'http://127.0.0.1:17645/api/state' -Headers $headers
    if ($state.environments.Count -ne 0) { throw 'Dados de teste nao estao isolados.' }
    if ($state.diagnostics.dataDirectory -ne $env:WAM_DATA_DIR) { throw 'Pasta de dados inesperada.' }
    $page = Invoke-WebRequest 'http://127.0.0.1:17645/' -UseBasicParsing
    if ($page.Content -notmatch 'Seus ambientes') { throw 'Arquivos estaticos nao foram publicados.' }
    if ($page.Content -notmatch '<title>MultAgent TOTVS') { throw 'Nome do projeto nao foi atualizado no painel.' }
    $script = Invoke-WebRequest 'http://127.0.0.1:17645/app.js' -UseBasicParsing
    if ($script.StatusCode -ne 200) { throw 'JavaScript nao publicado.' }
    Write-Output 'PASS: executavel publicado iniciou fora da pasta do projeto.'
    Write-Output 'PASS: painel, JavaScript, token e API responderam com dados isolados.'
    Write-Output "Evidencia temporaria: $env:WAM_DATA_DIR"
} finally {
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $env:WAM_PORT = $originalPort
    $env:WAM_DATA_DIR = $originalData
}
