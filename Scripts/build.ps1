# Construit l'app et regenere les sprites. Aucun IDE requis : le SDK .NET suffit.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

dotnet build "$root\src\Hamster.App" -c Release
dotnet run --project "$root\src\SpriteGen" -c Release -- "$root"

Write-Host ""
Write-Host "exe : $root\src\Hamster.App\bin\Release\net9.0-windows\Hamster.exe"
