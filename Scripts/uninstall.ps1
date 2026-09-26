# Defait install.ps1 : retire le lancement a l'ouverture de session, arrete le
# hamster et supprime le dossier installe. Les reglages de %APPDATA%\Hamster
# sont conserves.
$ErrorActionPreference = "Stop"
if (-not $env:LOCALAPPDATA) { throw "LOCALAPPDATA introuvable" }
. "$PSScriptRoot\common.ps1"

$dest = Join-Path $env:LOCALAPPDATA "Programs\Hamster"

Remove-ItemProperty -Path $RunKey -Name "Hamster" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $StartupApprovedKey -Name "Hamster" -ErrorAction SilentlyContinue

Stop-Hamster

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }

Write-Host "desinstalle. Reglages conserves : $env:APPDATA\Hamster\settings.json"
