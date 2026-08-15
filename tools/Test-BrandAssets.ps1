[CmdletBinding()]
param([Parameter(Mandatory)][string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ProjectRoot)
$assetRoot = Join-Path $root 'src\LocalWhale.App\Assets'
$relativeFiles = @(
    'LocalWhale.ico',
    'Brand\LocalWhaleMark.svg',
    'Brand\LocalWhaleMark-16.png',
    'Brand\LocalWhaleMark-24.png',
    'Brand\LocalWhaleMark-32.png'
)

foreach ($relativePath in $relativeFiles) {
    $fullPath = Join-Path $assetRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "missing brand asset: src\LocalWhale.App\Assets\$relativePath"
    }
}

$iconPath = Join-Path $assetRoot 'LocalWhale.ico'
$stream = [IO.File]::OpenRead($iconPath)
try {
    if ($stream.Length -lt 6) { throw "invalid ICO header: file is shorter than 6 bytes" }
    $reader = [IO.BinaryReader]::new($stream)
    $reserved = $reader.ReadUInt16()
    $type = $reader.ReadUInt16()
    $count = $reader.ReadUInt16()
    if ($reserved -ne 0 -or $type -ne 1 -or $count -lt 1 -or $stream.Length -lt (6 + 16 * $count)) {
        throw "invalid ICO header: reserved=$reserved type=$type count=$count"
    }

    $iconSizes = [Collections.Generic.List[int]]::new()
    for ($index = 0; $index -lt $count; $index++) {
        $widthByte = $reader.ReadByte()
        $heightByte = $reader.ReadByte()
        $null = $reader.ReadByte()
        $null = $reader.ReadByte()
        $null = $reader.ReadUInt16()
        $null = $reader.ReadUInt16()
        $length = $reader.ReadUInt32()
        $offset = $reader.ReadUInt32()
        $width = if ($widthByte -eq 0) { 256 } else { [int]$widthByte }
        $height = if ($heightByte -eq 0) { 256 } else { [int]$heightByte }
        if ($width -ne $height) { throw "invalid ICO entry: ${width}x${height} is not square" }
        if ($length -eq 0 -or ([long]$offset + [long]$length) -gt $stream.Length) {
            throw "invalid ICO entry: size $width points outside the file"
        }
        $iconSizes.Add($width)
    }
}
finally {
    $stream.Dispose()
}

$actualIconSizes = @($iconSizes | Sort-Object -Unique)
$expectedIconSizes = @(16, 20, 24, 32, 48, 64, 128, 256)
if (($actualIconSizes -join ',') -ne ($expectedIconSizes -join ',')) {
    throw "invalid ICO sizes: expected $($expectedIconSizes -join ','), got $($actualIconSizes -join ',')"
}

Add-Type -AssemblyName System.Drawing.Common
$pngSizes = [Collections.Generic.List[int]]::new()
foreach ($size in @(16, 24, 32)) {
    $path = Join-Path $assetRoot "Brand\LocalWhaleMark-$size.png"
    $image = [Drawing.Image]::FromFile($path)
    try {
        if ($image.Width -ne $size -or $image.Height -ne $size) {
            throw "invalid PNG dimensions: LocalWhaleMark-$size.png is $($image.Width)x$($image.Height)"
        }
    }
    finally {
        $image.Dispose()
    }
    $pngSizes.Add($size)
}

$svgPath = Join-Path $assetRoot 'Brand\LocalWhaleMark.svg'
$svgText = Get-Content -Raw -LiteralPath $svgPath
[xml]$svg = $svgText
if ($svg.DocumentElement.LocalName -ne 'svg' -or $svg.DocumentElement.GetAttribute('viewBox') -ne '0 0 256 256') {
    throw 'invalid SVG: expected an svg root with viewBox="0 0 256 256"'
}
if ($svg.SelectNodes('//*[local-name()="text"]').Count -ne 0) {
    throw 'invalid SVG: text elements are not allowed in the application icon'
}

$allowedColors = @('#24366F', '#BDEEFF', '#5F8FE6')
$usedColors = @([regex]::Matches($svgText, '#[0-9A-Fa-f]{6}') | ForEach-Object { $_.Value.ToUpperInvariant() } | Sort-Object -Unique)
$unexpectedColors = @($usedColors | Where-Object { $_ -notin $allowedColors })
if ($unexpectedColors.Count -gt 0) {
    throw "invalid SVG colors: $($unexpectedColors -join ',')"
}
if ('#24366F' -notin $usedColors -or '#BDEEFF' -notin $usedColors) {
    throw 'invalid SVG colors: required background and mark colors are missing'
}

[pscustomobject]@{
    IconSizes = $actualIconSizes
    PngSizes = @($pngSizes)
    SvgValid = $true
}
