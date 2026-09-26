# Installe le hamster dans %LOCALAPPDATA%\Programs\Hamster et le lance.
# Le lancement a l'ouverture de session s'active depuis le menu du hamster ("Lancer au
# demarrage") : ce script ne cree plus la valeur Run. Si elle existe deja, il la fait
# pointer sur l'exe installe, pour qu'une ancienne installation reste valide.
# Tout est au niveau utilisateur : aucun droit administrateur requis.
$ErrorActionPreference = "Stop"
if (-not $env:LOCALAPPDATA) { throw "LOCALAPPDATA introuvable" }
. "$PSScriptRoot\common.ps1"

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA "Programs\Hamster"
$exe = Join-Path $dest "Hamster.exe"

# l'exe installe est verrouille tant qu'il tourne : on l'arrete avant de copier
Stop-Hamster

# un dossier a part plutot que bin\ : on peut recompiler sans arreter le hamster
dotnet publish "$root\src\Hamster.App" -c Release -o $dest --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish en echec" }

$existing = Get-ItemProperty -Path $RunKey -Name "Hamster" -ErrorAction SilentlyContinue
if ($existing) {
    Set-ItemProperty -Path $RunKey -Name "Hamster" -Value "`"$exe`""
    $startup = "HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Hamster, mis a jour"
} else {
    $startup = "inactif : menu du hamster > Lancer au demarrage"
}
Start-Process $exe

Write-Host ""
Write-Host "installe  : $exe"
Write-Host "demarrage : $startup"
Write-Host "retrait   : .\Scripts\uninstall.ps1"
