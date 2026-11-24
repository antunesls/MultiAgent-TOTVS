param([switch]$Autocontido)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\WebAgentManager\WebAgentManager.csproj'
$output = Join-Path $root 'artifacts\app'
$selfContained = 'false'
if ($Autocontido) { $selfContained = 'true' }
& dotnet publish $project -c Release -r win-x64 --self-contained $selfContained -o $output
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação. Confira a saída do dotnet.' }
Write-Host "Aplicativo publicado em: $output"
Write-Host 'Execute Iniciar.cmd na raiz do projeto. O inicio no login e opcional.'
