$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $root 'artifacts\app\WebAgentManager.exe'
$port = 17643
if ($env:WAM_PORT) { $port = [int]$env:WAM_PORT }
$url = "http://127.0.0.1:$port"
$existing = $false
try {
    $response = Invoke-RestMethod "$url/api/bootstrap" -TimeoutSec 2
    $existing = $response.product -eq 'EstacaoProtheus'
} catch { $existing = $false }
if ($existing) {
    Start-Process $url
    return
}
if (-not (Test-Path -LiteralPath $executable)) {
    & (Join-Path $PSScriptRoot 'Publicar.ps1')
}
Start-Process -FilePath $executable -ArgumentList '--open' -WorkingDirectory (Split-Path $executable -Parent)
Write-Host "Gerenciador iniciado em $url. Se a pagina nao abrir, confira se a porta ja esta ocupada."
