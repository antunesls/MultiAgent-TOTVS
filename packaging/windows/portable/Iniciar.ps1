$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'WebAgentManager.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Executavel ausente. Extraia todo o ZIP antes de iniciar.' }
$port = 17643
if ($env:WAM_PORT) { $port = [int]$env:WAM_PORT }
$url = "http://127.0.0.1:$port"
$existing = $false
try {
    $response = Invoke-RestMethod "$url/api/bootstrap" -TimeoutSec 2
    $existing = $response.product -in @('MultAgentTOTVS', 'EstacaoProtheus')
} catch { $existing = $false }
if ($existing) { Start-Process $url; return }
Start-Process -FilePath $executable -ArgumentList '--open' -WorkingDirectory $PSScriptRoot
Write-Host "Painel: $url. Fechar a pagina nao encerra o gerenciador."
