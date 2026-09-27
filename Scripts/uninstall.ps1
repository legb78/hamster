# Defait install.ps1 : retire le lancement a l'ouverture de session, arrete le
# hamster, supprime le dossier installe et ~/.hamster (hook.sh, events.jsonl et sa
# rotation). Les reglages de %APPDATA%\Hamster sont conserves. Les reglages de Claude
# Code ne sont jamais lus ni modifies : le bloc Notification, s'il a ete ajoute a la
# main, se retire a la main.
$ErrorActionPreference = "Stop"
if (-not $env:LOCALAPPDATA) { throw "LOCALAPPDATA introuvable" }
. "$PSScriptRoot\common.ps1"

$dest = Join-Path $env:LOCALAPPDATA "Programs\Hamster"
# le dossier que l'app remplit : Environment.SpecialFolder.UserProfile, comme HookInstaller
$data = Join-Path ([Environment]::GetFolderPath("UserProfile")) ".hamster"

Remove-ItemProperty -Path $RunKey -Name "Hamster" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $StartupApprovedKey -Name "Hamster" -ErrorAction SilentlyContinue

# avant ~/.hamster : lance, le hamster y redeposerait hook.sh
Stop-Hamster

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
$left = @(Remove-HamsterData $data)

Write-Host "desinstalle. Reglages conserves : $env:APPDATA\Hamster\settings.json"
if ($left.Count -gt 0) {
    Write-Host "$data garde, il contient d'autres fichiers : $(($left | ForEach-Object { $_.Name }) -join ', ')"
}
Write-Host ""
Write-Host "A FAIRE A LA MAIN si tu avais branche le hook : retire le bloc Notification du hamster"
Write-Host "(variante Git Bash : la commande sh `"`$HOME/.hamster/hook.sh`" ; variante PowerShell : la"
Write-Host "commande qui ecrit dans .hamster\events.jsonl) de $([Environment]::GetFolderPath("UserProfile"))\.claude\settings.json,"
Write-Host "ou du fichier de reglages de Claude Code ou tu l'avais mis. Ce script n'y touche pas."
Write-Host "Tant qu'il reste : la variante Git Bash affiche une erreur de hook a chaque notification,"
Write-Host "la variante PowerShell recree .hamster et y ecrit en silence, sans limite de taille."
