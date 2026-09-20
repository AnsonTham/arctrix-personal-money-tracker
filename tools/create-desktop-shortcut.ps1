# Creates (or refreshes) a desktop shortcut to the published Windows app.
#
# Run it once. The shortcut keeps working after every later `dotnet publish`, because it points at
# a path that publish overwrites in place - re-run this only if the publish location changes.
#
# The icon comes from the executable itself (see ApplicationIcon in the project file), so a rebuilt
# icon needs no change here.
#
#   dotnet publish -f net10.0-windows10.0.19041.0 -c Release
#   .\tools\create-desktop-shortcut.ps1
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ShortcutName = 'Arctrix Money Tracker'
)
$ErrorActionPreference = 'Stop'

$exe = Join-Path $ProjectRoot 'bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\Arctrix.PersonalMoneyTracker.exe'
if (-not (Test-Path $exe)) {
    throw "No published app at $exe - run: dotnet publish -f net10.0-windows10.0.19041.0 -c Release"
}

$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) "$ShortcutName.lnk"
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = Split-Path $exe -Parent      # so the app finds telegram.local.json beside it
$link.IconLocation = "$exe,0"      # the icon the build embedded, so it never drifts
$link.Description = 'Arctrix Personal Money Tracker'
$link.Save()

"Shortcut : $shortcut"
"Target   : $exe"
"Icon     : embedded in the executable"

# Explorer caches shortcut icons; nudge it so a rebuilt icon shows immediately.
Start-Process ie4uinit.exe -ArgumentList "-show" -NoNewWindow -Wait
