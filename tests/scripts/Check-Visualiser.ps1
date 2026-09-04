<#
.SYNOPSIS
  One-launch on-device stability check for the E5 visualiser. Enables the visualiser in the packaged settings,
  launches the app, opens the drawer, plays a short 440 Hz tone through the default output (so WASAPI loopback has
  signal), captures the drawer rectangle, checks the log for capture start / unhandled exceptions, then quits.
  Single app launch, single tone - no test player, no loops.
#>
$ErrorActionPreference = 'Stop'
if (Get-Process LogonUI -ErrorAction SilentlyContinue) { throw 'Workstation locked: capture would be black.' }
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;
public class CV{
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string c,string t);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h,uint m,IntPtr w,IntPtr l);
}
"@
$root = "C:\Users\anan_\Desktop\AI\WinDots"
$art = Join-Path $root 'tests\artifacts'
$pfn = (Get-AppxPackage -Name WinDots.Dev).PackageFamilyName
$state = Join-Path $env:LOCALAPPDATA "Packages\$pfn\LocalState"
$log = Join-Path $state 'logs\shell.log'

# Generate a 2.5 s 440 Hz mono 16-bit WAV so the loopback capture has real signal.
$wav = Join-Path $env:TEMP 'windots-tone.wav'
$sr = 44100; $secs = 2.5; $n = [int]($sr*$secs); $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([char[]]'RIFF'); $bw.Write([int](36 + $n*2)); $bw.Write([char[]]'WAVE'); $bw.Write([char[]]'fmt '); $bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]$sr); $bw.Write([int]($sr*2)); $bw.Write([int16]2); $bw.Write([int16]16); $bw.Write([char[]]'data'); $bw.Write([int]($n*2))
for ($i=0;$i -lt $n;$i++){ $bw.Write([int16]([math]::Sin(2*[math]::PI*440*$i/$sr)*8000)) }
$bw.Flush(); [System.IO.File]::WriteAllBytes($wav,$ms.ToArray()); $bw.Dispose()

Stop-Process -Name WinDots,WinDots.TestPlayer -Force -ErrorAction SilentlyContinue; Start-Sleep -m 500
New-Item -ItemType Directory -Force $state | Out-Null
# Enable the visualiser (ring) before launch.
'{ "schemaVersion": 1, "visualiser": { "enabled": true, "style": "Ring", "placement": "BehindArt" } }' | Set-Content (Join-Path $state 'settings.json') -Encoding utf8
if (Test-Path $log) { Remove-Item $log -Force }
Add-AppxPackage -Register (Join-Path $root 'src\WinDots.App\bin\x64\Debug\net10.0-windows10.0.26100.0\AppxManifest.xml') -ForceUpdateFromAnyVersion
$cx = [int]([CV]::GetSystemMetrics(0)/2)
Start-Process "shell:AppsFolder\$pfn!App"; Start-Sleep 6
function Send-Cmd($c){ $h=[CV]::FindWindowW('WinDots.ShellMessageWindow',[NullString]::Value); if($h -ne [IntPtr]::Zero){[void][CV]::PostMessageW($h,0x8002,[IntPtr]$c,[IntPtr]0)} }
Send-Cmd 2  # toggle at cursor -> but cursor may be anywhere; use monitor 0 open instead
$h=[CV]::FindWindowW('WinDots.ShellMessageWindow',[NullString]::Value); if($h -ne [IntPtr]::Zero){[void][CV]::PostMessageW($h,0x8002,[IntPtr]2,[IntPtr]0)}
Start-Sleep -m 1500
$player = New-Object System.Media.SoundPlayer $wav
$player.Play()
Start-Sleep -m 1800
$w=760;$hh=360;$x=$cx-[int]($w/2); $b=New-Object System.Drawing.Bitmap $w,$hh; $g=[System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($x,0,0,0,$b.Size); $g.Dispose(); $b.Save((Join-Path $art 'visualiser.png')); $b.Dispose()
$player.Stop()
Send-Cmd 6  # dump state
Start-Sleep -m 400
"=== log ==="; Get-Content $log -Tail 20 | Select-String -Pattern "UNHANDLED|UNOBSERVED|visualiser|captur|backdrop|host ready|Exception"
Send-Cmd 5  # quit
Start-Sleep 2; Stop-Process -Name WinDots -Force -ErrorAction SilentlyContinue
"done"
