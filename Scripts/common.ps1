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

# Une entree desactivee dans le Gestionnaire des taches laisse une valeur ici, et
# Explorer saute alors la cle Run a l'ouverture de session.
$StartupApprovedKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
$RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
