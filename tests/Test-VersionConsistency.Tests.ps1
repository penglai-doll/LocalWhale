[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProjectPath = Join-Path $projectRoot 'src\LocalWhale.App\LocalWhale.App.csproj'
$windowXamlPath = Join-Path $projectRoot 'src\LocalWhale.App\MainWindow.xaml'
$windowCodePath = Join-Path $projectRoot 'src\LocalWhale.App\MainWindow.xaml.cs'
$installerPath = Join-Path $projectRoot 'installer\LocalWhale.iss'
$readmePath = Join-Path $projectRoot 'README.md'

function Assert-TextMatch {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Pattern,
        [Parameter(Mandatory)][string]$Description
    )

    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath $Path
    if ($text -notmatch $Pattern) {
        throw "$Description is inconsistent in $Path"
    }
}

[xml]$project = Get-Content -Raw -Encoding UTF8 -LiteralPath $appProjectPath
$expectedProjectValues = @{
    ApplicationDisplayVersion = '0.1.2'
    ApplicationVersion = '3'
    Version = '0.1.2'
    AssemblyVersion = '0.1.2.0'
    FileVersion = '0.1.2.0'
}

foreach ($property in $expectedProjectValues.Keys) {
    $actual = $project.Project.PropertyGroup.$property | Select-Object -First 1
    if ($actual -ne $expectedProjectValues[$property]) {
        throw "$property expected $($expectedProjectValues[$property]), got '$actual' in $appProjectPath"
    }
}

Assert-TextMatch -Path $installerPath -Pattern '(?m)^#define MyAppVersion "0\.1\.2"\r?$' -Description 'Installer product version'
Assert-TextMatch -Path $windowCodePath -Pattern '\?\? "0\.1\.2";' -Description 'Shell version fallback'
Assert-TextMatch -Path $windowXamlPath -Pattern 'Subtitle="LocalWhale 0\.1\.2 \u00B7 Harness \u2014"' -Description 'Initial title-bar version'
Assert-TextMatch -Path $readmePath -Pattern '\|\s*LocalWhale\s*\|\s*0\.1\.2\s*\|' -Description 'README LocalWhale version table'

foreach ($path in @($readmePath, (Join-Path $projectRoot 'src\LocalWhale.Core\Persistence\LocalWhalePaths.cs'))) {
    Assert-TextMatch -Path $path -Pattern '0\.1\.0-rc\.6' -Description 'Pinned Harness version'
}

Write-Host 'Version consistency passed: LocalWhale 0.1.2 and Harness 0.1.0-rc.6.'
