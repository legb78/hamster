<#
.SYNOPSIS
    Construit l'archive a publier : un Hamster.exe autonome, sans rien a installer.

.DESCRIPTION
    Un seul fichier autonome : ni .NET ni aucune dependance a installer sur la machine
    cible. Le prix, c'est le poids du runtime .NET embarque ; l'archive zip le compresse.
    L'archive contient aussi LICENSE et NOTICE (la licence Apache 2.0 impose de les
    joindre a toute redistribution), un LISEZMOI et les deux variantes du hook.

.PARAMETER Output
    Dossier de destination. Par defaut publish/ a la racine du depot (ignore par git).

.EXAMPLE
    .\Scripts\publish.ps1
#>
[CmdletBinding()]
param(
    [string]$Output
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $root "publish" }
$stage = Join-Path $Output "Hamster"
$zip = Join-Path $Output "Hamster-win-x64.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

& dotnet publish (Join-Path $root "src\Hamster.App\Hamster.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $stage `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish en echec (code $LASTEXITCODE)" }

# seul l'exe compte : un fichier de plus a cote serait une dependance oubliee
$extra = @(Get-ChildItem $stage -File | Where-Object Name -ne "Hamster.exe")
if ($extra.Count -gt 0) { throw "fichiers inattendus a cote de l'exe : $(($extra.Name) -join ', ')" }

Copy-Item (Join-Path $root "LICENSE") $stage
Copy-Item (Join-Path $root "NOTICE") $stage

# l'exe embarque le runtime .NET et Windows Forms (MIT) : leur licence et leurs avis tiers
# accompagnent l'archive. On prend ceux des paquets de runtime que la publication a
# reellement utilises, lus dans project.assets.json (downloadDependencies, version exacte
# notee "[9.0.10, 9.0.10]"), pas ceux de l'installation du SDK.
$assets = Get-Content (Join-Path $root "src\Hamster.App\obj\project.assets.json") -Raw | ConvertFrom-Json
$downloads = @($assets.project.frameworks.PSObject.Properties | ForEach-Object { $_.Value.downloadDependencies })
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
$legal = @{
    "Microsoft.NETCore.App.Runtime.win-x64"        = @{ "LICENSE.TXT" = "dotnet-runtime-LICENSE.txt"; "THIRD-PARTY-NOTICES.TXT" = "dotnet-runtime-THIRD-PARTY-NOTICES.txt" }
    "Microsoft.WindowsDesktop.App.Runtime.win-x64" = @{ "LICENSE" = "windowsdesktop-runtime-LICENSE.txt" }
}
foreach ($id in $legal.Keys) {
    $dep = $downloads | Where-Object { $_.name -eq $id } | Select-Object -First 1
    if (-not $dep -or $dep.version -notmatch '^\[([0-9][^,\]]*)') { throw "$id absent de project.assets.json" }
    $lib = "$id/$($Matches[1])"
    $dir = Join-Path $packages ($lib.ToLowerInvariant())
    foreach ($file in $legal[$id].Keys) {
        $src = Join-Path $dir $file
        if (-not (Test-Path $src)) { throw "licence introuvable : $src" }
        Copy-Item $src (Join-Path $stage $legal[$id][$file])
    }
    Write-Host "runtime embarque : $lib"
}
Copy-Item (Join-Path $root "Hooks\settings-snippet.json") (Join-Path $stage "hook-git-bash.json")
Copy-Item (Join-Path $root "Hooks\settings-snippet.powershell.json") (Join-Path $stage "hook-powershell.json")
Copy-Item (Join-Path $root "Scripts\LISEZMOI.txt") $stage

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

$exeMb = [math]::Round((Get-Item (Join-Path $stage "Hamster.exe")).Length / 1MB)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB)
Write-Host ""
Write-Host "exe     : $(Join-Path $stage 'Hamster.exe') ($exeMb Mo)"
Write-Host "archive : $zip ($zipMb Mo)"
