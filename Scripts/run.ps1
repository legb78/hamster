# Lance le hamster. Les logs partent sur stderr, dans cette console.
# Une seule instance par session : l'instance installee (install.ps1) est arretee,
# elle reviendra a la prochaine ouverture de session.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\common.ps1"
$root = Split-Path -Parent $PSScriptRoot
$exe = "$root\src\Hamster.App\bin\Release\net9.0-windows\Hamster.exe"

if (-not (Test-Path $exe)) { & "$PSScriptRoot\build.ps1" }

Stop-Hamster
& $exe
