[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [ValidateRange(1, [long]::MaxValue)][long]$MaximumBytes = 500MB
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish directory does not exist: $PublishDirectory"
}

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$requiredFiles = @(
    'LocalWhale.exe',
    'LocalWhale.pri',
    'MainWindow.xbf',
    'LICENSE',
    'Assets\LocalWhale.ico',
    'bridge\package.json',
    'bridge\src\index.js',
    'bridge\desktop-bridge.yml',
    'runtime\node\node.exe',
    'runtime\pnpm\dist\pnpm.mjs',
    'runtime\harness\0.1.0-rc.6\node_modules\@deepseek-ai\dsh\lib\bin.js',
    'runtime\harness\0.1.0-rc.6\runtime-manifest.json',
    'runtime\harness\0.1.0-rc.6\pnpm-lock.yaml',
    'licenses\DeepSeek-Harness-LICENSE.txt',
    'licenses\THIRD-PARTY-NOTICES.md'
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $publishRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Publish layout is missing required file: $relativePath"
    }
}

$rootItem = Get-Item -LiteralPath $publishRoot -Force
$items = @($rootItem) + @(Get-ChildItem -LiteralPath $publishRoot -Recurse -Force)
$reparsePoint = $items | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } | Select-Object -First 1
if ($null -ne $reparsePoint) {
    $rootPrefix = $publishRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $relative = if ($reparsePoint.FullName.Equals($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        '.'
    }
    else {
        $reparsePoint.FullName.Substring($rootPrefix.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    }
    throw "Publish layout contains a reparse point: $relative"
}

$files = @($items | Where-Object { -not $_.PSIsContainer })
$totalBytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
if ($totalBytes -gt $MaximumBytes) {
    throw ("Publish layout size {0:N0} bytes exceeds maximum {1:N0} bytes." -f $totalBytes, $MaximumBytes)
}

[PSCustomObject]@{
    PublishDirectory = $publishRoot
    FileCount = $files.Count
    TotalBytes = $totalBytes
    MaximumBytes = $MaximumBytes
}
