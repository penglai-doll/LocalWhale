[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appRoot = Join-Path $projectRoot 'src\LocalWhale.App'
$requiredResources = @(
    'ShellTitleBarBackgroundBrush',
    'ShellOverlayBackgroundBrush',
    'ShellCardBackgroundBrush',
    'ShellCardBorderBrush',
    'ShellAccentBrush',
    'ShellAccentForegroundBrush',
    'ShellMutedForegroundBrush',
    'WhaleGirlDecorationOpacity'
)

foreach ($themeName in @('OriginalTheme', 'WhaleGirlTheme')) {
    $themePath = Join-Path $appRoot "Themes\$themeName.xaml"
    if (-not (Test-Path -LiteralPath $themePath -PathType Leaf)) {
        throw "Shell theme dictionary is missing: $themePath"
    }

    [xml]$theme = Get-Content -Raw -Encoding UTF8 -LiteralPath $themePath
    foreach ($variant in @('Light', 'Dark', 'HighContrast')) {
        $variantNode = $theme.SelectSingleNode("//*[local-name()='ResourceDictionary' and @*[local-name()='Key']='$variant']")
        if ($null -eq $variantNode) {
            throw "$themeName is missing the $variant theme dictionary."
        }

        $keys = @(
            $variantNode.SelectNodes(".//*[@*[local-name()='Key']]") |
                ForEach-Object { $_.Attributes | Where-Object LocalName -eq 'Key' | Select-Object -ExpandProperty Value }
        )
        foreach ($required in $requiredResources) {
            if ($required -notin $keys) {
                throw "$themeName/$variant is missing resource key $required."
            }
        }
    }
}

[xml]$application = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $appRoot 'App.xaml')
$originalMerge = $application.SelectSingleNode("//*[local-name()='ResourceDictionary' and @Source='Themes/OriginalTheme.xaml']")
if ($null -eq $originalMerge) {
    throw 'App.xaml does not load OriginalTheme.xaml by default.'
}

[xml]$window = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $appRoot 'MainWindow.xaml')
if ($null -eq $window.SelectSingleNode("//*[local-name()='TitleBar' and @*[local-name()='Name']='AppTitleBar']")) {
    throw 'MainWindow.xaml does not use the WinUI TitleBar control.'
}
$whaleEmoji = [char]::ConvertFromUtf32(0x1F433)
if ((Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $appRoot 'MainWindow.xaml')).Contains($whaleEmoji)) {
    throw 'MainWindow.xaml still contains the generic whale emoji.'
}

$servicePath = Join-Path $appRoot 'ShellThemeService.cs'
if (-not (Test-Path -LiteralPath $servicePath -PathType Leaf)) {
    throw "Shell theme service is missing: $servicePath"
}

Write-Host 'Shell theme contract passed: variants, keys, default merge, TitleBar, and service.'
