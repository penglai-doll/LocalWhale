[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$validator = Join-Path $projectRoot 'tools\Test-PublishLayout.ps1'

if (-not (Test-Path -LiteralPath $validator)) {
    Write-Error "Release layout validator is missing: $validator"
    exit 1
}

function New-ValidPublishFixture {
    param([Parameter(Mandatory)][string]$Path)

    $files = @(
        'LocalWhale.exe',
        'LocalWhale.pri',
        'MainWindow.xbf',
        'LICENSE',
        'Assets\LocalWhale.ico',
        'Assets\Themes\WhaleGirl\WhaleGirlPortrait.png',
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

    foreach ($relativePath in $files) {
        $fullPath = Join-Path $Path $relativePath
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $fullPath) | Out-Null
        [IO.File]::WriteAllText($fullPath, 'fixture')
    }
}

function Assert-ThrowsLike {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][string]$Pattern
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike $Pattern) {
            throw "Expected error like '$Pattern', got '$($_.Exception.Message)'."
        }
        return
    }

    throw "Expected action to throw an error like '$Pattern'."
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("LocalWhale.PublishLayout.Tests." + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $valid = Join-Path $testRoot 'valid'
    New-ValidPublishFixture -Path $valid
    & $validator -PublishDirectory $valid -MaximumBytes 1MB | Out-Null

    $missing = Join-Path $testRoot 'missing'
    New-ValidPublishFixture -Path $missing
    Remove-Item -LiteralPath (Join-Path $missing 'runtime\node\node.exe') -Force
    Assert-ThrowsLike -Pattern '*missing required file*runtime\node\node.exe*' -Action {
        & $validator -PublishDirectory $missing -MaximumBytes 1MB | Out-Null
    }

    $missingIcon = Join-Path $testRoot 'missing-icon'
    New-ValidPublishFixture -Path $missingIcon
    Remove-Item -LiteralPath (Join-Path $missingIcon 'Assets\LocalWhale.ico') -Force
    Assert-ThrowsLike -Pattern '*missing required file*Assets\LocalWhale.ico*' -Action {
        & $validator -PublishDirectory $missingIcon -MaximumBytes 1MB | Out-Null
    }

    $missingPortrait = Join-Path $testRoot 'missing-portrait'
    New-ValidPublishFixture -Path $missingPortrait
    Remove-Item -LiteralPath (Join-Path $missingPortrait 'Assets\Themes\WhaleGirl\WhaleGirlPortrait.png') -Force
    Assert-ThrowsLike -Pattern '*missing required file*Assets\Themes\WhaleGirl\WhaleGirlPortrait.png*' -Action {
        & $validator -PublishDirectory $missingPortrait -MaximumBytes 1MB | Out-Null
    }

    $oversized = Join-Path $testRoot 'oversized'
    New-ValidPublishFixture -Path $oversized
    $largeFile = [IO.File]::OpenWrite((Join-Path $oversized 'large.bin'))
    try { $largeFile.SetLength(2MB) } finally { $largeFile.Dispose() }
    Assert-ThrowsLike -Pattern '*exceeds*1,048,576*' -Action {
        & $validator -PublishDirectory $oversized -MaximumBytes 1MB | Out-Null
    }

    $reparse = Join-Path $testRoot 'reparse'
    New-ValidPublishFixture -Path $reparse
    New-Item -ItemType Junction -Path (Join-Path $reparse 'linked-runtime') -Target (Join-Path $reparse 'runtime') | Out-Null
    Assert-ThrowsLike -Pattern '*reparse point*linked-runtime*' -Action {
        & $validator -PublishDirectory $reparse -MaximumBytes 1MB | Out-Null
    }

    Write-Host 'Publish layout tests passed: valid, missing resource, size limit, and reparse point.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
