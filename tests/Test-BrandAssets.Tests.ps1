[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$validator = Join-Path $projectRoot 'tools\Test-BrandAssets.ps1'

if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
    throw "Brand asset validator is missing: $validator"
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

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("LocalWhale.BrandAssets.Tests." + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $emptyFixture = Join-Path $testRoot 'empty'
    New-Item -ItemType Directory -Path $emptyFixture | Out-Null
    Assert-ThrowsLike -Pattern '*missing brand asset*Assets\LocalWhale.ico*' -Action {
        & $validator -ProjectRoot $emptyFixture | Out-Null
    }

    $result = & $validator -ProjectRoot $projectRoot
    if (($result.IconSizes -join ',') -ne '16,20,24,32,48,64,128,256') {
        throw "Unexpected ICO sizes: $($result.IconSizes -join ',')"
    }
    if (($result.PngSizes -join ',') -ne '16,24,32') {
        throw "Unexpected PNG sizes: $($result.PngSizes -join ',')"
    }
    if (-not $result.SvgValid) {
        throw 'SVG validation did not report success.'
    }

    $corruptFixture = Join-Path $testRoot 'corrupt'
    $corruptAssets = Join-Path $corruptFixture 'src\LocalWhale.App\Assets'
    New-Item -ItemType Directory -Path (Split-Path -Parent $corruptAssets) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\LocalWhale.App\Assets') -Destination $corruptAssets -Recurse
    [IO.File]::WriteAllBytes((Join-Path $corruptAssets 'LocalWhale.ico'), [byte[]](1, 2, 3, 4))
    Assert-ThrowsLike -Pattern '*invalid ICO header*' -Action {
        & $validator -ProjectRoot $corruptFixture | Out-Null
    }

    Write-Host 'Brand asset tests passed: missing, valid sizes, SVG contract, and corrupt ICO.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
