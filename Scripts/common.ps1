# Fonctions partagees par les scripts. A charger avec : . "$PSScriptRoot\common.ps1"

# Arrete le hamster de la session courante, proprement d'abord : l'app retire alors
# son icone de notification. Un kill la laisserait en fantome jusqu'au survol.
function Stop-Hamster {
    # Local\ ne designe que la session courante : on ne touche pas au hamster d'un
    # autre compte connecte, qu'un kill ne pourrait de toute facon pas arreter
    $session = (Get-Process -Id $PID).SessionId
    $procs = @(Get-Process Hamster -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $session })
    if ($procs.Count -eq 0) { return }

    try {
        $quit = [System.Threading.EventWaitHandle]::OpenExisting("Local\Hamster.DesktopPet.Quit")
        # on ferme notre handle tout de suite : garde ouvert, il maintiendrait
        # l'evenement en vie pour l'instance que le script relance ensuite
        try { [void]$quit.Set() } finally { $quit.Dispose() }
        $procs | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    }
    catch {
        # version sans arret propre, ou deja en train de quitter : on passe au kill
    }

    $left = @($procs | Where-Object { -not $_.HasExited })
    if ($left.Count -gt 0) {
        Write-Warning "arret propre sans reponse, kill : l'icone peut rester jusqu'au survol"
        $left | Stop-Process -Force
        $left | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }
}

# Le Gestionnaire des taches y garde une valeur binaire par entree Run, non documentee :
# premier octet 0x02 active, 0x03 desactivee (Explorer saute alors la valeur Run a
# l'ouverture de session), d'apres les valeurs lues sur le poste de developpement.
$StartupApprovedKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
$RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

# Ce que le hamster et son hook ecrivent dans ~/.hamster : le script depose au lancement
# (et son fichier temporaire d'ecriture), les notifications recopiees et leur rotation.
$HamsterDataFiles = @("hook.sh", "hook.sh.tmp", "events.jsonl", "events.jsonl.1")

# Supprime ces fichiers, puis le dossier s'il est vide. Un fichier ajoute a la main
# dans le dossier est laisse, et le dossier avec lui. Rend ce qui reste.
function Remove-HamsterData([string]$Dir) {
    if (-not (Test-Path -LiteralPath $Dir)) { return @() }
    foreach ($name in $HamsterDataFiles) {
        $path = Join-Path $Dir $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    $left = @(Get-ChildItem -LiteralPath $Dir -Force)
    if ($left.Count -eq 0) { Remove-Item -LiteralPath $Dir -Force }
    return $left
}
