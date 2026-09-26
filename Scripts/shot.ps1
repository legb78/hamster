# Capture la zone de l'ecran occupee par le hamster, plus une petite marge.
# Volontairement limite a la fenetre du personnage : on ne capture pas le bureau entier.
param([string]$Out = "$PSScriptRoot\..\Assets\preview\window.png", [int]$Margin = 24)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sig = @"
using System;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
if (-not ("W" -as [type])) { Add-Type -TypeDefinition $sig }

# sans ca PowerShell est virtualise par le DPI et la capture est decalee
[void][W]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))

# [NullString]::Value, pas $null : PowerShell convertirait $null en chaine vide,
# et FindWindow chercherait alors une classe nommee "" au lieu de n'importe laquelle
$hwnd = [W]::FindWindow([NullString]::Value, "HamsterPet")
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "fenetre HamsterPet introuvable (l'app tourne-t-elle ?)"; exit 1 }

$r = New-Object W+RECT
[void][W]::GetWindowRect($hwnd, [ref]$r)

$x = $r.Left - $Margin
$y = $r.Top - $Margin
$w = ($r.Right - $r.Left) + 2 * $Margin
$h = ($r.Bottom - $r.Top) + 2 * $Margin

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$g.Dispose()

$full = [System.IO.Path]::GetFullPath($Out)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($full)) | Out-Null
$bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "$full  ($w x $h a $x,$y)"
