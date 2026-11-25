param([Parameter(Mandatory = $true)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
# Este teste altera atalhos e o registro HKCU. Nao executar no computador do usuario.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Execute este teste somente em runner Windows descartavel do GitHub Actions.' }
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$work = Join-Path $env:RUNNER_TEMP ('multagent-installer-' + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $work 'instalacao com espacos'
$uninstaller = Join-Path $install 'unins000.exe'
$data = Join-Path $env:LOCALAPPDATA 'ProtheusWebAgentManager'
$sentinel = Join-Path $data ('release-smoke-' + [Guid]::NewGuid().ToString('N') + '.txt')
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{C0755DAC-16FB-489E-B69A-BA59FB175794}_is1'
$startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'MultAgent TOTVS.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'MultAgent TOTVS.lnk'
if (Test-Path -LiteralPath $registry) { throw 'Ja existe instalacao registrada. Use um runner limpo.' }
New-Item -ItemType Directory -Path $work, $data -Force | Out-Null
Set-Content -LiteralPath $sentinel -Value 'dados preservados'
function Run-Setup([string]$path, [string[]]$arguments) {
    $process = Start-Process -FilePath $path -ArgumentList $arguments -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Instalador/desinstalador retornou $($process.ExitCode)." }
}
try {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$install`"", '/TASKS="desktopicon,startup"', "/LOG=`"$(Join-Path $work 'install.log')`"")
    Run-Setup $InstallerPath $arguments
    if (-not (Test-Path -LiteralPath $registry)) { throw 'Instalacao nao foi registrada no HKCU.' }
    if (-not (Test-Path -LiteralPath $startup) -or -not (Test-Path -LiteralPath $desktop)) { throw 'Atalhos opcionais nao foram criados.' }
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($startup).TargetPath -ne (Join-Path $install 'WebAgentManager.exe')) { throw 'Inicio automatico aponta para outro executavel.' }
    & (Join-Path $root 'tests\publish-smoke.ps1') -ApplicationDirectory $install
    # Reinstalar a mesma versao exercita substituicao de arquivos e AppId estavel.
    Run-Setup $InstallerPath $arguments
    if ((Get-Content -LiteralPath $sentinel -Raw).Trim() -ne 'dados preservados') { throw 'Reinstalacao alterou os dados.' }
    & (Join-Path $root 'tests\publish-smoke.ps1') -ApplicationDirectory $install
    Run-Setup $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $work 'uninstall.log')`"")
    # O desinstalador pode delegar a remocao a um processo temporario.
    for ($attempt = 0; $attempt -lt 40 -and (Test-Path -LiteralPath $registry); $attempt++) { Start-Sleep -Milliseconds 250 }
    if (Test-Path -LiteralPath $registry) { throw 'Registro da instalacao nao foi removido.' }
    if (Test-Path -LiteralPath $startup) { throw 'Atalho de inicio automatico nao foi removido.' }
    if (Test-Path -LiteralPath $desktop) { throw 'Atalho da area de trabalho nao foi removido.' }
    if ((Get-Content -LiteralPath $sentinel -Raw).Trim() -ne 'dados preservados') { throw 'Desinstalacao alterou os dados.' }
    Write-Output 'PASS: instalacao HKCU, atalhos, reinstalacao e desinstalacao preservando dados.'
} finally {
    if ((Test-Path -LiteralPath $registry) -and (Test-Path -LiteralPath $uninstaller)) { Run-Setup $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') }
    if (Test-Path -LiteralPath $sentinel) { Remove-Item -LiteralPath $sentinel }
    Write-Output "Logs do instalador: $work"
}
