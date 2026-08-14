[CmdletBinding()]
param(
    [string]$HarnessVersion = '0.1.0-rc.6',
    [string]$NodeVersion = '24.18.1',
    [string]$PnpmVersion = '11.7.0',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts\runtime' }
$outputFullPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputFullPath.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Runtime output must stay inside the LocalWhale workspace: $outputFullPath"
}

$downloadCache = Join-Path $projectRoot '.tools\downloads'
New-Item -ItemType Directory -Force -Path $downloadCache | Out-Null
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null

function Get-CachedFile([string]$Uri, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { return }
    Write-Host "Downloading $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing
}

function Remove-RuntimeDebugArtifacts([string]$RuntimeDirectory) {
    if (-not (Test-Path -LiteralPath $RuntimeDirectory)) { return }
    $debugFiles = Get-ChildItem -LiteralPath $RuntimeDirectory -Recurse -File | Where-Object {
        $_.Extension -in @('.pdb', '.map')
    }
    if ($debugFiles.Count -gt 0) {
        $bytes = ($debugFiles | Measure-Object Length -Sum).Sum
        $debugFiles | Remove-Item -Force
        Write-Host "Removed $($debugFiles.Count) runtime debug artifacts ($([math]::Round($bytes / 1MB, 1)) MiB)."
    }
}

$nodeArchive = Join-Path $downloadCache "node-v$NodeVersion-win-x64.zip"
Get-CachedFile "https://nodejs.org/dist/v$NodeVersion/node-v$NodeVersion-win-x64.zip" $nodeArchive
$nodeDestination = Join-Path $outputFullPath 'node'
if (-not (Test-Path -LiteralPath (Join-Path $nodeDestination 'node.exe'))) {
    $nodeExtract = Join-Path $outputFullPath ".node-$NodeVersion.tmp"
    if (Test-Path -LiteralPath $nodeExtract) { Remove-Item -LiteralPath $nodeExtract -Recurse -Force }
    Expand-Archive -LiteralPath $nodeArchive -DestinationPath $nodeExtract
    $expandedNode = Join-Path $nodeExtract "node-v$NodeVersion-win-x64"
    if (Test-Path -LiteralPath $nodeDestination) { Remove-Item -LiteralPath $nodeDestination -Recurse -Force }
    Move-Item -LiteralPath $expandedNode -Destination $nodeDestination
    Remove-Item -LiteralPath $nodeExtract -Recurse -Force
}
$nodeExecutable = Join-Path $nodeDestination 'node.exe'

$pnpmDirectory = Join-Path $outputFullPath 'pnpm'
$pnpmScript = Join-Path $pnpmDirectory 'bin\pnpm.cjs'
$pnpmArchive = Join-Path $downloadCache "pnpm-$PnpmVersion.tgz"
Get-CachedFile "https://registry.npmjs.org/pnpm/-/pnpm-$PnpmVersion.tgz" $pnpmArchive
if (-not (Test-Path -LiteralPath $pnpmScript)) {
    $pnpmExtract = Join-Path $outputFullPath ".pnpm-$PnpmVersion.tmp"
    if (Test-Path -LiteralPath $pnpmExtract) { Remove-Item -LiteralPath $pnpmExtract -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $pnpmExtract | Out-Null
    & tar.exe -xzf $pnpmArchive -C $pnpmExtract
    if ($LASTEXITCODE -ne 0) { throw "Could not extract pnpm package: $pnpmArchive" }
    $expandedPnpm = Join-Path $pnpmExtract 'package'
    if (-not (Test-Path -LiteralPath (Join-Path $expandedPnpm 'bin\pnpm.cjs')) -or -not (Test-Path -LiteralPath (Join-Path $expandedPnpm 'dist\pnpm.mjs'))) {
        throw "The pnpm distribution was incomplete: $pnpmArchive"
    }
    if (Test-Path -LiteralPath $pnpmDirectory) { Remove-Item -LiteralPath $pnpmDirectory -Recurse -Force }
    Move-Item -LiteralPath $expandedPnpm -Destination $pnpmDirectory
    Remove-Item -LiteralPath $pnpmExtract -Recurse -Force
}

$runtimeDirectory = Join-Path $outputFullPath "harness\$HarnessVersion"
if (Test-Path -LiteralPath (Join-Path $runtimeDirectory 'runtime-manifest.json')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\desktop-bridge.yml') -Destination (Join-Path $runtimeDirectory 'desktop-bridge.yml') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\src\index.js') -Destination (Join-Path $runtimeDirectory 'packages\bridge\src\index.js') -Force
    $installedBridge = Join-Path $runtimeDirectory 'node_modules\@localwhale\dsh-desktop-bridge\src\index.js'
    if (Test-Path -LiteralPath $installedBridge) {
        Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\src\index.js') -Destination $installedBridge -Force
    }
    Remove-RuntimeDebugArtifacts $runtimeDirectory
    Write-Host "Harness runtime $HarnessVersion is already prepared."
    exit 0
}
if (Test-Path -LiteralPath $runtimeDirectory) { Remove-Item -LiteralPath $runtimeDirectory -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $runtimeDirectory 'packages\bridge\src') | Out-Null

Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\package.json') -Destination (Join-Path $runtimeDirectory 'packages\bridge\package.json')
Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\src\index.js') -Destination (Join-Path $runtimeDirectory 'packages\bridge\src\index.js')
Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\desktop-bridge.yml') -Destination (Join-Path $runtimeDirectory 'desktop-bridge.yml')

$packageJson = @{
    name = 'localwhale-harness-runtime'
    version = '1.0.0'
    private = $true
    packageManager = "pnpm@$PnpmVersion"
    dependencies = [ordered]@{
        '@deepseek-ai/dsh' = $HarnessVersion
        '@localwhale/dsh-desktop-bridge' = 'file:./packages/bridge'
    }
} | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText((Join-Path $runtimeDirectory 'package.json'), $packageJson, [System.Text.UTF8Encoding]::new($false))
$workspaceYaml = @'
minimumReleaseAge: 0
strictDepBuilds: true
nodeLinker: hoisted
packageImportMethod: copy
allowBuilds:
  '@deepseek-ai/dsh-subprocess-local@0.1.0-rc.6': true
  '@google/genai@1.52.0': true
  'koffi@3.1.4': true
  'koffi@3.1.5': true
  'node-pty@1.1.0': true
  'protobufjs@7.6.5': true
'@
[System.IO.File]::WriteAllText((Join-Path $runtimeDirectory 'pnpm-workspace.yaml'), $workspaceYaml, [System.Text.UTF8Encoding]::new($false))

$oldPath = $env:PATH
try {
    $env:PATH = "$nodeDestination;$oldPath"
    Push-Location $runtimeDirectory
    & $nodeExecutable $pnpmScript install --lockfile-only --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw "pnpm lockfile generation failed with exit code $LASTEXITCODE" }
    & $nodeExecutable $pnpmScript fetch --frozen-lockfile --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw "pnpm fetch failed with exit code $LASTEXITCODE" }
    & $nodeExecutable $pnpmScript install --offline --frozen-lockfile --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw "pnpm offline install failed with exit code $LASTEXITCODE" }
    Pop-Location

    & dotnet run --project (Join-Path $projectRoot 'tools\LocalWhale.RuntimeTool\LocalWhale.RuntimeTool.csproj') --configuration Release -- validate-lifecycle (Join-Path $runtimeDirectory 'node_modules')
    if ($LASTEXITCODE -ne 0) { throw 'Harness lifecycle script validation failed.' }

    Push-Location $runtimeDirectory
    & $nodeExecutable $pnpmScript rebuild --pending
    if ($LASTEXITCODE -ne 0) { throw "pnpm rebuild failed with exit code $LASTEXITCODE" }
    Pop-Location

    Remove-RuntimeDebugArtifacts $runtimeDirectory

    & dotnet run --project (Join-Path $projectRoot 'tools\LocalWhale.RuntimeTool\LocalWhale.RuntimeTool.csproj') --configuration Release -- write-manifest $runtimeDirectory $HarnessVersion
    if ($LASTEXITCODE -ne 0) { throw 'Runtime manifest generation failed.' }
}
finally {
    $env:PATH = $oldPath
    while ((Get-Location).Path.StartsWith($runtimeDirectory, [System.StringComparison]::OrdinalIgnoreCase)) { Pop-Location }
}

Write-Host "Prepared LocalWhale runtime at $runtimeDirectory"
