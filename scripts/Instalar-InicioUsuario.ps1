param([switch]$Remover)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $root 'artifacts\app\WebAgentManager.exe'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Estacao Protheus.lnk'
if ($Remover) {
    if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
    Write-Host 'Atalho de inicio automatico removido deste usuario.'
    return
}
if (-not (Test-Path -LiteralPath $executable)) { throw 'Publique primeiro: .\scripts\Publicar.ps1' }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = Split-Path $executable -Parent
$shortcut.Description = 'Gerenciador local de WebAgents do Protheus por usuario'
$shortcut.Save()
Write-Host "Inicio automatico configurado somente para $env:USERNAME. Nenhum servico Windows foi criado."
