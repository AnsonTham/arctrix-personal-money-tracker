# Creates (or refreshes) a desktop shortcut to the published Windows app.
#
# Run it once. The shortcut keeps working after every later `dotnet publish`, because it points at
# a path that publish overwrites in place - re-run this only if the publish location changes or the
# icon needs rebuilding.
#
#   dotnet publish -f net10.0-windows10.0.19041.0 -c Release
#   .\tools\create-desktop-shortcut.ps1
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ShortcutName = 'Arctrix Money Tracker'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$exe = Join-Path $ProjectRoot 'bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\Arctrix.PersonalMoneyTracker.exe'
if (-not (Test-Path $exe)) {
    throw "No published app at $exe - run: dotnet publish -f net10.0-windows10.0.19041.0 -c Release"
}

# The icon lives outside the publish folder, so a republish can never delete it.
$iconDir = Join-Path $env:LOCALAPPDATA 'Arctrix Solutions'
$iconPath = Join-Path $iconDir 'arctrix.ico'
New-Item -ItemType Directory -Force $iconDir | Out-Null

<#
.SYNOPSIS
Builds a multi-size .ico from the wallet mark, on the app's own dark tile.
The exe's embedded icon is only 32x32, which looks coarse at desktop size; this renders every size
Explorer asks for, from the 1024px source.
#>
function New-AppIcon([string]$markPath, [string]$destination) {
    $sizes = 16, 24, 32, 48, 64, 128, 256
    $background = [System.Drawing.Color]::FromArgb(255, 8, 10, 14)   # #080A0E, the app tile
    $mark = [System.Drawing.Image]::FromFile($markPath)
    $pngs = @()

    try {
        foreach ($size in $sizes) {
            $bmp = New-Object System.Drawing.Bitmap $size, $size
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.SmoothingMode = 'AntiAlias'
                $g.InterpolationMode = 'HighQualityBicubic'
                $g.PixelOffsetMode = 'HighQuality'

                # Rounded tile, the same shape Windows rounds app icons to.
                $radius = [math]::Max(2, [int]($size * 0.18))
                $path = New-Object System.Drawing.Drawing2D.GraphicsPath
                $d = $radius * 2
                $path.AddArc(0, 0, $d, $d, 180, 90)
                $path.AddArc($size - $d, 0, $d, $d, 270, 90)
                $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
                $path.AddArc(0, $size - $d, $d, $d, 90, 90)
                $path.CloseFigure()
                $brush = New-Object System.Drawing.SolidBrush $background
                $g.FillPath($brush, $path)
                $brush.Dispose(); $path.Dispose()

                # The mark at 80% of the tile, centred - matching MauiIcon's ForegroundScale.
                $inset = [int]($size * 0.10)
                $g.DrawImage($mark, $inset, $inset, $size - (2 * $inset), $size - (2 * $inset))
            }
            finally { $g.Dispose() }

            $stream = New-Object System.IO.MemoryStream
            $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
            $pngs += , @{ Size = $size; Bytes = $stream.ToArray() }
            $stream.Dispose()
        }
    }
    finally { $mark.Dispose() }

    # ICO container: a 6-byte header, a 16-byte directory entry per image, then the PNG data.
    $out = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $out
    try {
        $writer.Write([uint16]0)              # reserved
        $writer.Write([uint16]1)              # type: icon
        $writer.Write([uint16]$pngs.Count)

        $offset = 6 + (16 * $pngs.Count)
        foreach ($png in $pngs) {
            $dimension = if ($png.Size -ge 256) { 0 } else { $png.Size }   # 256 is stored as 0
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)            # palette size: none
            $writer.Write([byte]0)            # reserved
            $writer.Write([uint16]1)          # colour planes
            $writer.Write([uint16]32)         # bits per pixel
            $writer.Write([uint32]$png.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $png.Bytes.Length
        }
        foreach ($png in $pngs) { $writer.Write($png.Bytes) }

        $writer.Flush()
        [System.IO.File]::WriteAllBytes($destination, $out.ToArray())
    }
    finally { $writer.Dispose(); $out.Dispose() }
}

New-AppIcon (Join-Path $ProjectRoot 'Resources\Images\arctrix_wallet_mark.png') $iconPath

$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) "$ShortcutName.lnk"
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = Split-Path $exe -Parent      # so the app finds telegram.local.json beside it
$link.IconLocation = "$iconPath,0"
$link.Description = 'Arctrix Personal Money Tracker'
$link.Save()

"Shortcut : $shortcut"
"Target   : $exe"
"Icon     : $iconPath"
