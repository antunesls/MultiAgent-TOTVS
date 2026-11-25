param(
    [string]$Tag,
    [string]$OutputDirectory,
    [string]$IsccPath,
    [switch]$SomenteZip
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\WebAgentManager\WebAgentManager.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
$versionPattern = '(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)'
if ($version -notmatch "^$versionPattern`$") { throw 'Versao do projeto invalida. Use X.Y.Z no csproj.' }
if (-not $Tag) { $Tag = "v$version" }
if ($Tag -cnotmatch "^v$versionPattern`$") { throw 'Tag invalida. Use vX.Y.Z, por exemplo v0.1.2.' }
if ($Tag -cne "v$version") { throw "A tag $Tag nao corresponde a versao do projeto ($version)." }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts\releases\$version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "O diretorio de saida ja existe: $OutputDirectory. Escolha um novo destino; nenhum arquivo foi sobrescrito." }
if (-not [Environment]::Is64BitOperatingSystem -or $env:OS -ne 'Windows_NT') { throw 'Gere os pacotes em Windows x64.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Instale o SDK .NET 10 para gerar os pacotes.' }
if (-not $SomenteZip) {
    if (-not $IsccPath) {
        $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($compiler) { $IsccPath = $compiler.Source }
        foreach ($candidate in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
            if (-not $IsccPath -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { $IsccPath = $candidate }
        }
    }
    if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) { throw 'Inno Setup 6 nao encontrado. Instale-o, informe -IsccPath ou use -SomenteZip para gerar apenas o portatil.' }
}

$work = Join-Path $root ('artifacts\release-work\' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'app'
$assets = Join-Path $work 'assets'
New-Item -ItemType Directory -Path $payload, $assets -Force | Out-Null
try {
    & dotnet publish $project -c Release -r win-x64 --self-contained true -p:ContinuousIntegrationBuild=true -o $payload
    if ($LASTEXITCODE -ne 0) { throw 'Falha no dotnet publish. Nenhum pacote foi liberado.' }
    if (-not (Test-Path -LiteralPath (Join-Path $payload 'WebAgentManager.exe'))) { throw 'Executavel publicado nao encontrado.' }
    if (-not (Test-Path -LiteralPath (Join-Path $payload 'wwwroot\index.html'))) { throw 'Painel nao encontrado na publicacao.' }
    Copy-Item -Path (Join-Path $root 'packaging\windows\portable\*') -Destination $payload
    $forbidden = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object { $_.Name -match '\.db($|-)|^web-agent\.(exe|msi)$|^servers\.json$' })
    if ($forbidden.Count) { throw 'Dados locais ou WebAgents encontrados no payload. Empacotamento recusado.' }
    $baseName = "MultAgent-TOTVS-$version-win-x64"
    Compress-Archive -Path (Join-Path $payload '*') -DestinationPath (Join-Path $assets "$baseName-portable.zip") -CompressionLevel Optimal
    if (-not $SomenteZip) {
        & $IsccPath "/DAppVersion=$version" "/DPayloadDir=$payload" "/DReleaseDir=$assets" (Join-Path $root 'packaging\windows\MultAgentTOTVS.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador. Nenhum pacote foi liberado.' }
        if (-not (Test-Path -LiteralPath (Join-Path $assets "$baseName-setup.exe"))) { throw 'Instalador nao encontrado apos compilacao.' }
    }
    $checksums = @(Get-ChildItem -LiteralPath $assets -File | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    })
    [IO.File]::WriteAllLines((Join-Path $assets 'SHA256SUMS.txt'), [string[]]$checksums, [Text.UTF8Encoding]::new($false))
    # Publicar a pasta apenas depois de concluir todos os pacotes.
    New-Item -ItemType Directory -Path (Split-Path $OutputDirectory -Parent) -Force | Out-Null
    Move-Item -LiteralPath $assets -Destination $OutputDirectory
    Write-Host "Release $Tag gerada em $OutputDirectory"
    if ($SomenteZip) { Write-Warning 'Somente o ZIP foi gerado. O instalador nao foi compilado nem validado.' }
    Write-Host 'Nenhum arquivo foi enviado ao GitHub. Execute os testes de smoke antes de distribuir.'
} finally {
    # Somente a pasta temporaria criada por esta execucao e descartada.
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
