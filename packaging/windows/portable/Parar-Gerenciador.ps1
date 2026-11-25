$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'WebAgentManager.exe'
Get-CimInstance Win32_Process -Filter "Name='WebAgentManager.exe'" | Where-Object { $_.ExecutablePath -eq $executable } | ForEach-Object {
    Stop-Process -Id $_.ProcessId
    Write-Host "Gerenciador encerrado: PID $($_.ProcessId). Os WebAgents nao foram encerrados."
}
