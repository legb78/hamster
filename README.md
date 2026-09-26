# Hamster — desktop pet Windows branché sur Claude Code

Une mascotte en pixel art qui vit en overlay en bas de l'écran et reflète en temps réel
ce que fait Claude Code. Quand Claude bosse, elle bosse. Quand plus rien ne tourne, elle glande.
Quand Claude lance des sous-agents, le hamster se duplique : un mini hamster par sous-agent.

Le personnage : hamster gris, énormes yeux noirs brillants, un nœud rose sur le côté du crâne.
Ces deux signatures visuelles sont tenues dans 100 % des frames — c'est la contrainte de
lisibilité à 128 px.

## Build et lancement, sans IDE

Le SDK .NET suffit (testé avec 9.0.306). Ni Visual Studio, ni MSVC Build Tools, ni paquet NuGet.

```powershell
.\Scripts\build.ps1   # compile l'app et régénère les sprites + la palette
.\Scripts\run.ps1     # lance, logs sur stderr dans la console
```

Tests, sans fenêtre (applications console, code de sortie 1 au moindre échec) :

```powershell
dotnet run --project tests\Hamster.Activity.Tests -c Release   # parseur, watchers, modèle, relecture des vrais transcripts
dotnet run --project tests\Hamster.App.Tests -c Release        # directeur, minis, rendu, réglages, clé Run (valeur HamsterTest)
```

Le hamster est visible dès le lancement. Une seule instance par session : `run.ps1` arrête
l'instance installée.

Menu : clic droit sur le hamster, ou clic (gauche ou droit) sur son icône de notification.
Pour quitter : menu → Quitter.

## Ce qu'il fait

| Claude Code | Le hamster |
|---|---|
| travaille | une scène de travail choisie d'après l'outil en cours — `Bash`/`PowerShell` : terminal ; `Read`/`Grep`/`Glob`/`WebFetch`/`WebSearch` : gros livre et loupe ; `Edit`/`Write`/`NotebookEdit` : laptop ; `Agent`/`Workflow` : chef d'orchestre. Puis une autre toutes les 8 à 18 s, tirée au hasard selon des poids (laptop 25, livre 20, terminal 20, labo 15, chef d'orchestre 15, réflexion 5). Il ne se balade pas. |
| attend ta réponse (question, plan à valider, autorisation) | il répond au téléphone, avec **le nom du projet** au-dessus de la tête : c'est la conversation qui a besoin de toi |
| a fini une tâche | saut et feux d'artifice (3 s) |
| erreur d'outil ou d'API | tête catastrophée (2 s), puis il se remet au travail |
| rien | balade, puis après le délai de chill (10 s par défaut) : console, grignotage, sieste, étirements, skate, entrecoupés de poses et de petites balades |

**Les minis.** Chaque sous-agent actif, et chaque autre session Claude Code active, a son mini
hamster, à 50 %, dont le nœud prend une couleur stable (dérivée de l'id du sous-agent ou du
dossier de la session). Il apparaît dans un nuage de fumée, disparaît dans des étincelles et
tourne autour des pieds du principal (un tour en 40 s), derrière lui sur l'arc arrière, devant
sur l'arc avant. Un sous-agent garde la même scène de travail du début à la fin. Au-delà de
six minis, un badge `+N` compte les autres.

Pour que l'arc avant ne passe jamais devant le visage, le principal monte de 42 px (sprite)
tant qu'il y a des minis : les minis de devant restent sous son museau. Vérifié au pixel sur
toutes les frames par `Hamster.App.Tests`, avec un filet de sécurité pour ce qui dépasse du
corps d'un mini (bulle du téléphone, feux d'artifice) : rien ne se peint sur le visage.

**Interactif.** Au survol, une étiquette apparaît au-dessus du hamster survolé : projet et
état pour une session, description pour un sous-agent. Clic sur un mini : il réagit. Clic et
glisser sur le principal : comme avant (réaction, déplacement).

**Sons**, désactivés par défaut (menu → **Son**) : téléphone (`Windows Ringin.wav`) à l'arrivée
d'une attente, `tada.wav` en fin de tâche, `Windows Navigation Start.wav` à l'apparition d'un
sous-agent, au plus un toutes les 10 s. Jamais en boucle. Coupés quand Windows refuse les
notifications (`SHQueryUserNotificationState` ≠ `QUNS_ACCEPTS_NOTIFICATIONS` : application
plein écran, mode présentation, session verrouillée…).

**Overlay de debug** (menu) : clip, état, outil, nombre de minis, état des deux sources.
Chaque changement d'état est journalisé sur stderr.

## Réglages

Dans `%APPDATA%\Hamster\settings.json` ; les clés absentes gardent leur valeur par défaut.

| Clé | Défaut | Menu |
|---|---|---|
| `Scale` | 2 | Taille |
| `OpacityPercent` | 100 | Opacite |
| `ChillDelaySeconds` | 10 | Delai avant le mode chill (5, 10, 30, 60 s) |
| `SoundEnabled` | false | Son |
| `OnlyWithClaudeDesktop` | false | Seulement avec Claude Desktop |
| `Paused`, `DebugOverlay`, `AnchorX`, `AnchorY` | | Pause, Overlay de debug, position |
| `ClaudeDesktopPathMarkers` | `["\\WindowsApps\\Claude_"]` | à la main, voir plus bas |

`OnlyWithClaudeDesktop` passe à `false` par défaut : le hamster suit Claude Code, qui tourne
aussi dans un terminal ou VS Code. Un `settings.json` existant qui porte `true` le garde.

Variables d'environnement, pour les tests surtout :

| Variable | Remplace |
|---|---|
| `HAMSTER_SETTINGS_DIR` | le dossier `%APPDATA%\Hamster` des réglages |
| `HAMSTER_PROJECTS_ROOT` | `%USERPROFILE%\.claude\projects`, les transcripts lus |
| `HAMSTER_EVENTS_PATH` | `%USERPROFILE%\.hamster\events.jsonl`, écrit par le hook |
| `HAMSTER_RUN_VALUE` | le nom `Hamster` de la valeur de démarrage (les tests utilisent `HamsterTest`) |

## Lancement au démarrage et installation

```powershell
.\Scripts\install.ps1     # publie dans %LOCALAPPDATA%\Programs\Hamster et le lance
.\Scripts\uninstall.ps1   # défait tout, garde les réglages
```

Menu → **Lancer au demarrage** : la case écrit ou retire la valeur
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Hamster` (le chemin de l'exe en cours, entre
guillemets), aucun droit administrateur. Seul ce clic la modifie, jamais le lancement. Cocher
la case retire aussi la marque qu'une désactivation dans le Gestionnaire des tâches laisse sous
`...\Explorer\StartupApproved\Run`, comme le faisait `install.ps1`. `install.ps1` ne crée plus
la valeur Run : si elle existe déjà, il la fait pointer sur l'exe installé.

## Avec Claude Desktop

Windows n'offre pas de déclencheur « quand l'application X démarre » sans activer l'audit
des processus, un réglage de sécurité système. Avec **Seulement avec Claude Desktop**, le
hamster fait donc l'inverse : il reste **caché** tant que Claude Desktop ne tourne pas,
**apparaît** quand il démarre et se cache quand il se ferme. Délai : jusqu'à 2 s, le temps
d'une énumération de processus. Pendant l'attente, l'icône de la zone de notification reste
là pour quitter ou changer de mode.

Le nom de processus ne suffit pas à reconnaître Claude Desktop : Claude Code (CLI, extension
VS Code) s'appelle lui aussi `claude.exe`. Le hamster lit donc le chemin de l'exécutable et
cherche `\WindowsApps\Claude_`, le dossier du paquet MSIX.

Si Claude Desktop est installé ailleurs, ajouter un fragment de son chemin dans
`%APPDATA%\Hamster\settings.json` (créer le fichier s'il n'existe pas). En JSON, **les
antislashs se doublent** :

```json
{ "ClaudeDesktopPathMarkers": ["\\WindowsApps\\Claude_", "\\chemin\\vers\\Claude\\"] }
```

Le hamster relit ce réglage dans les 2 s, sans redémarrage, et ne l'écrase plus en sauvegardant.
Commentaires et virgule finale sont tolérés ; un fichier vraiment illisible est copié en
`settings.json.bad` et signalé par une notification Windows.

Les scripts arrêtent le hamster proprement (il retire son icône de notification) avant de
recourir au kill, et seulement dans la session Windows courante.

## D'où vient l'état

- **Source principale : les transcripts** que Claude Code écrit déjà dans
  `~/.claude/projects/<slug>/<session>.jsonl`, et ceux des sous-agents dans
  `<session>/subagents/agent-<id>.jsonl` (plus `subagents/workflows/<wf>/`), avec un
  `agent-<id>.meta.json` qui donne leur description. Lus en lecture seule, sans jamais bloquer
  Claude Code (`FileShare.ReadWrite | FileShare.Delete`), par suivi de fin de fichier. Coût sur
  les sessions : zéro. Latence mesurée de l'écriture d'une ligne au changement d'état dans le
  journal : 60 à 64 ms en général, 170 ms au pire.
- **Source complémentaire : un hook `Notification`.** Les demandes d'autorisation n'apparaissent
  pas dans les transcripts ; seul ce hook les signale. Le hamster dépose le script
  `~/.hamster/hook.sh` au lancement, mais **ne branche rien** : il ne touche jamais aux réglages
  de Claude Code. Pour l'activer, fusionner à la main ce bloc (`Hooks/settings-snippet.json`)
  dans les réglages de Claude Code, par exemple `~/.claude/settings.json`
  ([doc des hooks](https://code.claude.com/docs/en/hooks)) :

  ```json
  {
    "hooks": {
      "Notification": [
        {
          "matcher": "permission_prompt|agent_needs_input|elicitation_dialog|elicitation_url_dialog",
          "hooks": [
            { "type": "command", "command": "sh \"$HOME/.hamster/hook.sh\"", "async": true, "timeout": 5 }
          ]
        }
      ]
    }
  }
  ```

  Le hook recopie la notification dans `~/.hamster/events.jsonl` et rend la main aussitôt
  (`async`, code 0 quoi qu'il arrive). Il passe par `sh` : sous Windows, il faut Git Bash. Un
  lancement de `sh` coûte de 160 à 310 ms (mesuré), en arrière-plan. Sans le hook, tout marche
  sauf les demandes d'autorisation : le téléphone ne sonne alors que pour les questions et les
  plans à valider, qui, eux, sont dans les transcripts.

Le format des transcripts est interne et non documenté : le parsing est tolérant (une ligne non
reconnue ne produit rien) et le test de relecture de `Hamster.Activity.Tests` sert de détecteur
si une mise à jour de Claude Code le change.

Garde-fous : une session qui travaille sans rien écrire pendant 3 min est considérée inactive
(un outil unique plus long fait donc passer le hamster au repos jusqu'à son résultat), une
attente est abandonnée après 1 h, un sous-agent muet disparaît après 30 min.

## Coût mesuré

Sur ce poste, en cycles (`QueryProcessCycleTime`), 30 s par mesure, échelle 2x :

| | CPU (% d'un cœur) | Mémoire privée |
|---|---|---|
| version précédente (celle de `main`, d'après la date de l'exe installé), au repos | 1,47 % | 17,8 Mo |
| cette version, au repos, watchers compris | 1,20 % | 27,5 Mo |
| cette version, 6 minis en orbite et badge +2 | 1,45 % | 28,2 Mo |

Les frames restent en indices de palette (2,3 Mio pour 148 frames, contre 18,5 Mio en ARGB avec
miroir) et sont converties au blit ; les minis recolorent leur nœud par la palette. Cadence du
timer : le clip le plus rapide affiché, borné à 6–12 Hz.

## Structure

```
src/Hamster.Art/       palette indexée, personnage paramétrable, clips — aucune dépendance Windows
src/SpriteGen/         rend les sheets PNG, la palette .gpl et les planches de relecture
src/Hamster.Activity/  transcripts et hook -> événements -> état des sessions et des sous-agents
src/Hamster.App/
  App/                 entrée, cycle de vie, réglages, menu, ActivityHub, sons, clé Run,
                       détection de Claude Desktop, diagnostics
  Window/              fenêtre layered, surface DIB, contrôleur, bureaux virtuels, P/Invoke
  Render/              frames indexées, animateur, composition, orbite, police 3x5
  State/               balade, directeur (état -> clip), foule des minis
tests/                 Hamster.Activity.Tests, Hamster.App.Tests
Hooks/                 hook.sh, settings-snippet.json (jamais appliqué automatiquement)
Assets/                palette.gpl, sprites/hamster/, preview/
Scripts/               build.ps1, run.ps1, shot.ps1, install.ps1, uninstall.ps1, common.ps1
```

## Adaptation macOS → Windows : ce qui a changé et pourquoi

La spec d'origine visait macOS. Ce qui ne transposait pas, et l'arbitrage retenu :

| Spec macOS | Windows | Pourquoi |
|---|---|---|
| `NSPanel` non activable | `Form` + `WS_EX_LAYERED`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE` | Le Form ne sert que de porteur de HWND ; aucun pixel ne passe par WinForms, tout va par `UpdateLayeredWindow`. |
| `NSApp.setActivationPolicy(.accessory)` + `LSUIElement` | `WS_EX_TOOLWINDOW` + `ShowInTaskbar = false` | Ni barre des tâches, ni Alt+Tab, ni vol de focus. |
| Hit-test sur l'alpha à écrire à la main | **gratuit** | Pour une fenêtre layered en alpha par pixel, Windows fait son hit-test sur l'alpha. Vérifié par `WindowFromPoint` : les pixels opaques reçoivent la souris, les pixels à alpha 0 la laissent passer à la fenêtre du dessous. Les minis partagent la fenêtre du principal : un seul hit-test. |
| `canJoinAllSpaces` | poll 1 s + `IVirtualDesktopManager` | Aucun équivalent public. Épingler sur tous les bureaux passe par `IVirtualDesktopManagerInternal`, non documentée, dont l'IID change à chaque build de Windows. Le repli n'utilise que l'API publique. Rançon : jusqu'à 1 s de retard au changement de bureau. |
| Niveau `.statusBar` | `HWND_TOPMOST`, réaffirmé toutes les 2 s | Couvre la barre des tâches et le plein écran *borderless*. **Ne couvre pas** le plein écran exclusif DirectX — limite structurelle de Windows, sans contournement propre. |
| `NSWorkspace.willSleep` / `screenIsLocked` | `SystemEvents.PowerModeChanged`, `SessionSwitch`, `WM_POWERBROADCAST` + `GUID_SESSION_DISPLAY_STATUS` | Couvre veille, verrouillage et extinction d'écran. Le timer est arrêté, pas ralenti. |
| Échelles Retina entières | `PerMonitorV2` + échelle sprite en pixels **physiques** | Windows propose 125 / 150 / 175 %. Suivre le DPI imposerait du ×1,5 flou. L'échelle reste un entier choisi au menu ; conséquence assumée : sur un écran à 150 % le hamster paraît plus petit que le reste de l'UI. Les étiquettes, elles, suivent le DPI. |
| Bundle `.app` | `dotnet publish` → dossier + `.exe` | Pas de signature, pas de notarisation. |
| Hook à chaque outil, budget < 10 ms | transcripts + un seul hook, `Notification` | Un hook coûte ici 131 ms (sh → awk), 27 ms pour le seul `cmd /c exit` : le plancher de `CreateProcess` + Defender rend le budget inatteignable. Les transcripts donnent tout le reste gratuitement. |

## État

- [x] **Phase 1 — squelette.** Fenêtre layered always-on-top, aucune icône de barre des tâches,
      menu (clic droit + zone de notification), drag, position persistée, click-through sur
      l'alpha, balade en bas de l'écran, pause veille/verrouillage/écran éteint, instance unique,
      cadence adaptative 6–12 Hz, overlay de debug, personnage procédural.
- [x] **Lancement avec Claude Desktop** — apparition et disparition avec Claude Desktop
      (optionnel), scripts d'installation.
- [x] **Phase 2 — état.** Watcher de transcripts, hook `Notification` (script déposé, à brancher
      à la main), machine à états par session et par sous-agent, garde-fous.
- [x] **Phase 3 — scènes.** Travail (laptop, livre, terminal, labo, chef d'orchestre, réflexion),
      téléphone, fête, erreur, activités de repos.
- [x] **Phase 4 — minis, FX, sons, réglages.** Minis en orbite, fumée et étincelles, badge +N,
      étiquettes au survol, clic sur les minis, sons, lancement au démarrage depuis le menu,
      délai de chill.
- [ ] Non vérifié : rendu sur un second écran à un autre DPI, et comportement face à une
      mise à jour du format des transcripts de Claude Code.
