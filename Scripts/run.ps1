# Lance le hamster. Les logs partent sur stderr, dans cette console.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = "$root\src\Hamster.App\bin\Release\net9.0-windows\Hamster.exe"

if (-not (Test-Path $exe)) { & "$PSScriptRoot\build.ps1" }

Get-Process Hamster -ErrorAction SilentlyContinue | Stop-Process -Force
& $exe
