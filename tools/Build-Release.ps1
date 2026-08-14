[CmdletBinding()]
param([switch]$SkipRuntime)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = Join-Path $projectRoot 'artifacts'
$publish = Join-Path $artifacts 'publish'
$runtime = Join-Path $artifacts 'runtime'
$redist = Join-Path $artifacts 'redist'

if (-not $publish.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid publish path.' }
if (-not $SkipRuntime) {
    & (Join-Path $PSScriptRoot 'Build-InitialRuntime.ps1') -OutputDirectory $runtime
    if ($LASTEXITCODE -ne 0) { throw 'Initial runtime build failed.' }
}

& dotnet test (Join-Path $projectRoot 'LocalWhale.slnx') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
& dotnet publish (Join-Path $projectRoot 'src\LocalWhale.App\LocalWhale.App.csproj') --configuration Release --runtime win-x64 --self-contained true --output $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$publishRuntime = Join-Path $publish 'runtime'
New-Item -ItemType Directory -Force -Path (Join-Path $publishRuntime 'harness') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishRuntime 'node') | Out-Null
Copy-Item -LiteralPath (Join-Path $runtime 'node\node.exe') -Destination (Join-Path $publishRuntime 'node\node.exe')
Copy-Item -LiteralPath (Join-Path $runtime 'node\LICENSE') -Destination (Join-Path $publishRuntime 'node\LICENSE')
Copy-Item -LiteralPath (Join-Path $runtime 'pnpm') -Destination (Join-Path $publishRuntime 'pnpm') -Recurse
Copy-Item -LiteralPath (Join-Path $runtime 'harness\0.1.0-rc.6') -Destination (Join-Path $publishRuntime 'harness\0.1.0-rc.6') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination (Join-Path $publish 'licenses') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $publish 'LICENSE')

$layout = & (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -PublishDirectory $publish -MaximumBytes 500MB
Write-Host ("Validated publish layout: {0} files, {1:N2} MiB." -f $layout.FileCount, ($layout.TotalBytes / 1MB))

New-Item -ItemType Directory -Force -Path $redist | Out-Null
$webViewInstaller = Join-Path $redist 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
if (-not (Test-Path -LiteralPath $webViewInstaller) -or (Get-Item -LiteralPath $webViewInstaller).Length -lt 100MB) {
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/?linkid=2124701' -OutFile $webViewInstaller -UseBasicParsing
}
if ((Get-Item -LiteralPath $webViewInstaller).Length -lt 100MB) { throw 'Downloaded WebView2 file is not the x64 Evergreen Standalone offline installer.' }

$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) { $iscc = Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }
if (-not (Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 compiler was not found.' }
& $iscc (Join-Path $projectRoot 'installer\LocalWhale.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

Write-Host "Release ready: $(Join-Path $artifacts 'installer\LocalWhale-Setup-x64.exe')"
