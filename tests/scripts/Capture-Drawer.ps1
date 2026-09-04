<#
.SYNOPSIS
  Captures the drawer's own rectangle on the primary monitor (no full-screen grabs) in the playing and empty states.
  Uses the diagnostics hook only; no input injection. Writes tests/artifacts/drawer-media.png and drawer-empty.png.
#>
param([int]$X = 600, [int]$Y = 0, [int]$W = 720, [int]$H = 300)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class WC {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
"@
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$art = Join-Path $root 'tests\artifacts'; New-Item -ItemType Directory -Force $art | Out-Null
$pfn = (Get-AppxPackage -Name WinDots.Dev).PackageFamilyName
$log = Join-Path $env:LOCALAPPDATA "Packages\$pfn\LocalState\logs\shell.log"
function Send-Cmd([int]$c,[int]$a=0){ $h=[WC]::FindWindowW('WinDots.ShellMessageWindow',[NullString]::Value); if ($h -eq [IntPtr]::Zero) { throw 'WinDots not running' }; [void][WC]::PostMessageW($h,0x8002,[IntPtr]$c,[IntPtr]$a) }
function Grab($name){ $b = New-Object System.Drawing.Bitmap $W,$H; $g=[System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($X,$Y,0,0,$b.Size); $g.Dispose(); $p = Join-Path $art $name; $b.Save($p); $b.Dispose(); "captured $p" }

if (Get-Process LogonUI -ErrorAction SilentlyContinue) { throw 'Workstation is locked: screen captures would be black. Unlock and rerun.' }
Stop-Process -Name WinDots,WinDots.TestPlayer -Force -ErrorAction SilentlyContinue; Start-Sleep -m 500
Add-AppxPackage -Register (Join-Path $root 'src\WinDots.App\bin\x64\Debug\net10.0-windows10.0.26100.0\AppxManifest.xml') -ForceUpdateFromAnyVersion

# Fake player with artwork and a known track.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Join-Path $root 'tests\WinDots.TestPlayer\bin\x64\Debug\net10.0-windows10.0.26100.0\WinDots.TestPlayer.exe')
$psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$tp = [System.Diagnostics.Process]::Start($psi)   # stdin stays open until we close it, keeping the session alive
Start-Sleep 3
$tp.StandardInput.WriteLine('seek 18'); $tp.StandardInput.Flush()
Start-Process "shell:AppsFolder\$pfn!App"; Start-Sleep 6
Send-Cmd 2 0; Start-Sleep -m 2500
Send-Cmd 6; Start-Sleep -m 300
Grab 'drawer-media.png'
Get-Content $log -Tail 4 | ForEach-Object { "   | $_" }
# Volume hooks: 11 logs the match, 12 sets 25 %, 13 toggles mute (twice to restore).
Send-Cmd 11; Start-Sleep -m 400; Send-Cmd 12; Start-Sleep -m 600; Send-Cmd 13; Start-Sleep -m 600; Send-Cmd 11; Start-Sleep -m 400; Send-Cmd 13; Start-Sleep -m 600
Grab 'drawer-media-volume.png'
Get-Content $log | Select-String -Pattern 'audio:|volume:' | Select-Object -Last 6 | ForEach-Object { "   | $_" }
Send-Cmd 3; Start-Sleep -m 1200

# Empty state: stop the fake player, reopen.
$tp.StandardInput.WriteLine('quit'); $tp.StandardInput.Flush(); if (-not $tp.WaitForExit(3000)) { $tp.Kill() }; Start-Sleep 2
Send-Cmd 2 0; Start-Sleep -m 2500
Send-Cmd 6; Start-Sleep -m 300
Grab 'drawer-empty.png'
Get-Content $log -Tail 3 | ForEach-Object { "   | $_" }
Send-Cmd 3; Start-Sleep -m 1000
Send-Cmd 5; Start-Sleep 3
Stop-Process -Name WinDots -Force -ErrorAction SilentlyContinue
