# Defait install.ps1 : retire le lancement a l'ouverture de session, arrete le
# hamster et supprime le dossier installe. Les reglages de %APPDATA%\Hamster
# sont conserves.
$ErrorActionPreference = "Stop"
if (-not $env:LOCALAPPDATA) { throw "LOCALAPPDATA introuvable" }

$dest = Join-Path $env:LOCALAPPDATA "Programs\Hamster"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Remove-ItemProperty -Path $runKey -Name "Hamster" -ErrorAction SilentlyContinue

$running = Get-Process Hamster -ErrorAction SilentlyContinue
if ($running) { $running | Stop-Process -Force; $running | Wait-Process -Timeout 10 }

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }

Write-Host "desinstalle. Reglages conserves : $env:APPDATA\Hamster\settings.json"
