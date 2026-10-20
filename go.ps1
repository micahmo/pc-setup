# PC Setup bootstrap. Run from any PowerShell window:
#   irm https://raw.githubusercontent.com/micahmo/pc-setup/main/go.ps1 | iex
# Downloads the latest release, replaces any older copy, and starts it. Safe to run any number of times.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repo = 'micahmo/pc-setup'
$root = Join-Path $env:LOCALAPPDATA 'PCSetup'
$app = Join-Path $root 'app'
$zip = Join-Path $env:TEMP 'PCSetup.zip'

Write-Host 'Downloading PC Setup...'
Invoke-WebRequest "https://github.com/$repo/releases/latest/download/PCSetup.zip" -OutFile $zip -UseBasicParsing

Get-Process PCSetup -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
Expand-Archive $zip $app -Force
Remove-Item $zip -Force
Get-ChildItem $app -Recurse -File | Unblock-File

# On a brand-new install, winget can exist but not be registered for this user yet.
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Host 'Registering winget...'
    try { Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe } catch { }
}
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Warning 'winget is not available yet. Update "App Installer" from the Microsoft Store, then select Rescan in PC Setup.'
}

Write-Host 'Starting PC Setup (it will ask for admin rights)...'
Start-Process (Join-Path $app 'PCSetup.exe')
