param([switch]$Remover)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $root 'artifacts\app\WebAgentManager.exe'
$startup = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startup 'MultAgent TOTVS.lnk'
$legacyShortcut = Join-Path $startup 'Estacao Protheus.lnk'
$shell = New-Object -ComObject WScript.Shell
if ($Remover) {
    foreach ($path in @($shortcutPath, $legacyShortcut)) {
        if ((Test-Path -LiteralPath $path) -and $shell.CreateShortcut($path).TargetPath -eq $executable) { Remove-Item -LiteralPath $path }
    }
    Write-Host 'Atalho de inicio automatico removido deste usuario.'
    return
}
if (-not (Test-Path -LiteralPath $executable)) { throw 'Publique primeiro: .\scripts\Publicar.ps1' }
if (Test-Path -LiteralPath $shortcutPath) {
    if ($shell.CreateShortcut($shortcutPath).TargetPath -ne $executable) { throw 'Ja existe um atalho com esse nome apontando para outro aplicativo. Nenhum atalho foi alterado.' }
}
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = Split-Path $executable -Parent
$shortcut.Description = 'MultAgent TOTVS - Gerenciador local de WebAgents do Protheus por usuario'
$shortcut.Save()
if ((Test-Path -LiteralPath $legacyShortcut) -and $shell.CreateShortcut($legacyShortcut).TargetPath -eq $executable) { Remove-Item -LiteralPath $legacyShortcut }
Write-Host "Inicio automatico configurado somente para $env:USERNAME. Nenhum servico Windows foi criado."
