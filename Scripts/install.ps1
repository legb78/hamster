# Installe le hamster et le lance a chaque ouverture de session Windows.
# Il reste cache tant que Claude Desktop ne tourne pas, apparait quand Claude
# Desktop demarre et se cache quand il se ferme (reglage OnlyWithClaudeDesktop).
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

Set-ItemProperty -Path $RunKey -Name "Hamster" -Value "`"$exe`""
# installer, c'est vouloir le demarrage : on leve une desactivation anterieure
Remove-ItemProperty -Path $StartupApprovedKey -Name "Hamster" -ErrorAction SilentlyContinue
Start-Process $exe

Write-Host ""
Write-Host "installe  : $exe"
Write-Host "demarrage : HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Hamster"
Write-Host "retrait   : .\Scripts\uninstall.ps1"
