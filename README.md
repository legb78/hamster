# Hamster — desktop pet Windows branché sur Claude Code

Une mascotte en pixel art qui vit en overlay en bas de l'écran et reflète en temps réel
ce que fait Claude Code. Quand Claude bosse, elle bosse. Quand plus rien ne tourne, elle glande.

Le personnage : hamster gris, énormes yeux noirs brillants, deux nœuds roses. Ces deux
signatures visuelles sont tenues dans 100 % des frames — c'est la contrainte de lisibilité à 128 px.

## Build et lancement, sans IDE

Le SDK .NET suffit (testé avec 9.0.306). Ni Visual Studio, ni MSVC Build Tools.

```powershell
.\Scripts\build.ps1   # compile l'app et régénère les sprites + la palette
.\Scripts\run.ps1     # lance, logs sur stderr dans la console
```

**Par défaut, le hamster reste caché tant que Claude Desktop ne tourne pas** (voir plus bas) :
sans Claude Desktop, seule son icône apparaît dans la zone de notification — sous la flèche
`^` si Windows l'y range. Menu → décocher **Seulement avec Claude Desktop** pour le voir.
Une seule instance par session : `run.ps1` arrête l'instance installée, qui revient à la
prochaine ouverture de session.

Menu : clic droit sur le hamster, ou clic (gauche ou droit) sur son icône de notification.
Pour quitter : menu → Quitter.

Les réglages persistent dans `%APPDATA%\Hamster\settings.json` (échelle, opacité, position, pause,
mode Claude Desktop, marqueurs de chemin).

## Avec Claude Desktop

```powershell
.\Scripts\install.ps1     # publie dans %LOCALAPPDATA%\Programs\Hamster et lance à chaque session
.\Scripts\uninstall.ps1   # défait tout, garde les réglages
```

Windows n'offre pas de déclencheur « quand l'application X démarre » sans activer l'audit
des processus, un réglage de sécurité système. Le hamster fait donc l'inverse : il démarre
avec la session (clé `HKCU\…\Run`, aucun droit administrateur), reste **caché** tant que
Claude Desktop ne tourne pas, **apparaît** quand il démarre et se cache quand il se ferme.
Délai : jusqu'à 2 s, le temps d'une énumération de processus.

Le nom de processus ne suffit pas à reconnaître Claude Desktop : Claude Code (CLI, extension
VS Code) s'appelle lui aussi `claude.exe`. Le hamster lit donc le chemin de l'exécutable et
cherche `\WindowsApps\Claude_`, le dossier du paquet MSIX. Conséquence : **Claude Code seul,
dans un terminal ou VS Code, ne fait pas apparaître le hamster.**

Si Claude Desktop est installé ailleurs, ajouter un fragment de son chemin dans
`%APPDATA%\Hamster\settings.json` (créer le fichier s'il n'existe pas ; les clés absentes
gardent leur valeur par défaut). En JSON, **les antislashs se doublent** :

```json
{ "ClaudeDesktopPathMarkers": ["\\WindowsApps\\Claude_", "\\chemin\\vers\\Claude\\"] }
```

Le hamster relit ce réglage dans les 2 s, sans redémarrage, et ne l'écrase plus en sauvegardant.
Commentaires et virgule finale sont tolérés ; un fichier vraiment illisible est copié en
`settings.json.bad` et signalé par une notification Windows.

Menu → **Seulement avec Claude Desktop** : décoché, le hamster reste visible en permanence.
Pendant l'attente, l'icône de la zone de notification reste là pour quitter ou changer de mode.

Les scripts arrêtent le hamster proprement (il retire son icône de notification) avant de
recourir au kill, et seulement dans la session Windows courante. `install.ps1` lève aussi une
désactivation faite dans le Gestionnaire des tâches ; `uninstall.ps1` retire les deux entrées.

## Structure

```
src/Hamster.Art/     palette indexée, personnage paramétrable, clips — aucune dépendance Windows
src/SpriteGen/       rend les sheets PNG, la palette .gpl et les planches de relecture
src/Hamster.App/
  App/               entrée, cycle de vie, réglages, icône de notification, menu, diagnostics,
                     détection de Claude Desktop
  Window/            fenêtre layered, surface DIB, contrôleur, bureaux virtuels, P/Invoke
  Render/            bibliothèque de sprites (miroir, recolorisation), animateur
  State/             balade du personnage
Assets/              palette.gpl, sprites/hamster/, preview/
Scripts/             build.ps1, run.ps1, shot.ps1, install.ps1, uninstall.ps1, common.ps1
```

## Adaptation macOS → Windows : ce qui a changé et pourquoi

La spec d'origine visait macOS. Ce qui ne transposait pas, et l'arbitrage retenu :

| Spec macOS | Windows | Pourquoi |
|---|---|---|
| `NSPanel` non activable | `Form` + `WS_EX_LAYERED`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE` | Le Form ne sert que de porteur de HWND ; aucun pixel ne passe par WinForms, tout va par `UpdateLayeredWindow`. |
| `NSApp.setActivationPolicy(.accessory)` + `LSUIElement` | `WS_EX_TOOLWINDOW` + `ShowInTaskbar = false` | Ni barre des tâches, ni Alt+Tab, ni vol de focus. |
| Hit-test sur l'alpha à écrire à la main | **gratuit** | Pour une fenêtre layered en alpha par pixel, Windows fait son hit-test sur l'alpha. Vérifié par `WindowFromPoint` : les pixels opaques reçoivent la souris, les pixels à alpha 0 la laissent passer à la fenêtre du dessous. |
| `canJoinAllSpaces` | poll 1 s + `IVirtualDesktopManager` | Aucun équivalent public. Épingler sur tous les bureaux passe par `IVirtualDesktopManagerInternal`, non documentée, dont l'IID change à chaque build de Windows. Le repli n'utilise que l'API publique. Rançon : jusqu'à 1 s de retard au changement de bureau. |
| Niveau `.statusBar` | `HWND_TOPMOST`, réaffirmé toutes les 2 s | Couvre la barre des tâches et le plein écran *borderless*. **Ne couvre pas** le plein écran exclusif DirectX — limite structurelle de Windows, sans contournement propre. |
| `NSWorkspace.willSleep` / `screenIsLocked` | `SystemEvents.PowerModeChanged`, `SessionSwitch`, `WM_POWERBROADCAST` + `GUID_SESSION_DISPLAY_STATUS` | Couvre veille, verrouillage et extinction d'écran. Le timer est arrêté, pas ralenti. |
| Échelles Retina entières | `PerMonitorV2` + échelle sprite en pixels **physiques** | Windows propose 125 / 150 / 175 %. Suivre le DPI imposerait du ×1,5 flou. L'échelle reste un entier choisi au menu ; conséquence assumée : sur un écran à 150 % le hamster paraît plus petit que le reste de l'UI. |
| Bundle `.app` | `dotnet publish` → dossier + `.exe` | Pas de signature, pas de notarisation. |

## Sources d'état (phase 2)

Conductor n'existe pas sur Windows. Et un hook Claude Code coûte ici **131 ms** (sh → awk),
44 ms pour awk seul, 27 ms pour le seul `cmd /c exit` : le plancher de `CreateProcess` +
Defender rend le budget « < 10 ms » de la spec inatteignable, quel que soit le langage.

D'où l'architecture retenue, mesurée et non supposée :

- **source primaire** — watcher des transcripts que Claude Code écrit déjà dans
  `~/.claude/projects/<slug>/<session-id>.jsonl`. Chaque ligne porte `sessionId`, `timestamp`,
  `cwd`, `gitBranch`, `entrypoint` (`claude-vscode` / `claude-desktop`) et
  `message.content[].type` (`tool_use` / `thinking` / `text`). Coût sur les sessions
  de l'utilisateur : **zéro**.

  Un champ `isSidechain` existe sur chaque ligne mais n'a **jamais été observé à `true`**
  (30 043 lignes analysées, dont 3 appels `Task`). Les sous-agents sont donc détectés
  autrement : un `tool_use` nommé `Task` sans `tool_result` correspondant = un sous-agent
  en cours, et l'id du `tool_use` sert d'identifiant stable pour la couleur de son nœud.
- **deux hooks seulement**, sur des événements rares : `Notification` (attente d'une réponse)
  et `Stop` (fin de tâche). Jamais sur `PreToolUse`/`PostToolUse`, qui ajouteraient ~150 ms
  à chaque appel d'outil.

Le format des transcripts est interne et non documenté : le parsing sera tolérant et
dégradera proprement (rien de reconnu → IDLE) si une mise à jour de Claude Code le change.

## État

- [x] **Phase 1 — squelette.** Fenêtre layered always-on-top, aucune icône de barre des tâches,
      menu (clic droit + zone de notification), drag, position persistée, click-through sur
      l'alpha, balade en bas de l'écran, pause veille/verrouillage/écran éteint, instance unique,
      cadence adaptative 6–12 Hz, overlay de debug, personnage procédural.
- [x] **Lancement avec Claude Desktop** (avancé de la phase 4) — démarrage à l'ouverture de
      session, apparition et disparition avec Claude Desktop, scripts d'installation.
- [ ] Phase 2 — machine à états, watcher de transcripts, hooks, garde-fous.
      **Pas encore fait : le hamster ne réagit pas encore à ce que fait Claude Code.**
- [ ] Phase 3 — pipeline pixel art complet, scènes de travail et de repos.
- [ ] Phase 4 — minis, FX, sons, réglages.
