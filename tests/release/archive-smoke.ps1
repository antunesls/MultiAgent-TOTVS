param([Parameter(Mandatory = $true)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ReleaseDirectory = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$manifest = Join-Path $ReleaseDirectory 'SHA256SUMS.txt'
$listed = @()
foreach ($line in Get-Content -LiteralPath $manifest) {
    if ($line -notmatch '^([a-f0-9]{64})  (MultAgent-TOTVS-[0-9]+\.[0-9]+\.[0-9]+-win-x64-(portable\.zip|setup\.exe))$') { throw 'Linha invalida no SHA256SUMS.txt.' }
    $expected = $Matches[1]
    $name = $Matches[2]
    if ($listed -contains $name) { throw 'Arquivo duplicado no manifesto.' }
    $listed += $name
    if ((Get-FileHash -LiteralPath (Join-Path $ReleaseDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "SHA-256 divergente: $name" }
}
$packages = @(Get-ChildItem -LiteralPath $ReleaseDirectory -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' })
if ($listed.Count -ne $packages.Count -or $listed.Count -lt 1) { throw 'Manifesto incompleto ou pacote vazio.' }
$zip = @(Get-ChildItem -LiteralPath $ReleaseDirectory -Filter '*-portable.zip' -File)
if ($zip.Count -ne 1) { throw 'Era esperado exatamente um ZIP portatil.' }
$work = Join-Path $root ('artifacts\archive-smoke\' + [Guid]::NewGuid().ToString('N') + '\pasta com espacos')
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    Expand-Archive -LiteralPath $zip[0].FullName -DestinationPath $work
    foreach ($name in @('WebAgentManager.exe', 'coreclr.dll', 'Microsoft.AspNetCore.Hosting.dll', 'wwwroot\index.html', 'Iniciar.cmd', 'Iniciar.ps1', 'Parar-Gerenciador.ps1', 'LEIA-ME.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $work $name) -PathType Leaf)) { throw "Arquivo obrigatorio ausente no ZIP: $name" }
    }
    if (@(Get-ChildItem -LiteralPath $work -Recurse -File | Where-Object { $_.Name -match '\.db($|-)|^web-agent\.(exe|msi)$' }).Count) { throw 'ZIP contem dados ou agentes indevidos.' }
    & (Join-Path $root 'tests\publish-smoke.ps1') -ApplicationDirectory $work
    Write-Output 'PASS: ZIP autocontido, manifesto SHA-256 e execucao em pasta com espacos.'
} finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
