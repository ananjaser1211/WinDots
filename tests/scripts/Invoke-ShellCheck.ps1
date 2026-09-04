<#
.SYNOPSIS
  On-device check of the WinDots shell without injecting any global input.
  Drives the running app through its diagnostics hook (WM_APP+2 posted to the WinDots.ShellMessageWindow window)
  and reads %LOCALAPPDATA%\WinDots\logs\shell.log. Never sends keyboard or mouse input to the desktop.

.PARAMETER Launch
  Register the Debug build and start the app first (stops it at the end).
#>
param([switch]$Launch)

$ErrorActionPreference = 'Stop'
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class WD {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
  public static List<string> List(uint pid){ var o=new List<string>(); EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==pid && IsWindowVisible(h)){ R r; GetWindowRect(h,out r); o.Add(string.Format("0x{0:X}|{1},{2} {3}x{4}", h.ToInt64(), r.L,r.T,r.Rt-r.L,r.B-r.T)); } return true;}, IntPtr.Zero); return o; }
}
"@

# Packaged app: the log lives in the package's LocalState folder.
$pfn = (Get-AppxPackage -Name WinDots.Dev).PackageFamilyName
$log = Join-Path $env:LOCALAPPDATA "Packages\$pfn\LocalState\logs\shell.log"
$WM_APP_COMMAND = 0x8002

function Send-Cmd([int]$cmd, [int]$arg = 0) {
  # [NullString]::Value marshals as a real null; $null would become "" and only match untitled windows.
  $h = [WD]::FindWindowW('WinDots.ShellMessageWindow', [NullString]::Value)
  if ($h -eq [IntPtr]::Zero) { throw 'ShellMessageWindow not found; is WinDots running?' }
  [void][WD]::PostMessageW($h, $WM_APP_COMMAND, [IntPtr]$cmd, [IntPtr]$arg)
}
function Wins { $p = @(Get-Process WinDots -ErrorAction SilentlyContinue); if ($p.Count -gt 0) { [WD]::List([uint32]$p[0].Id) } }
function Drawer { Wins | Where-Object { $_ -match ' \d+x([2-9]\d\d)$' } }
# The pill handle window is now sized to the visual (6 logical px tall at rest, 8 on hover), so its physical height is
# a single/low-double digit across DPI (6 at 100 %, ~9 at 150 %); match 4-15 px. The drawer is >=200 px (3 digits).
function Handles { Wins | Where-Object { $_ -match ' \d+x([4-9]|1[0-5])$' } }
function LogTail([int]$n = 12) { if (Test-Path $log) { Get-Content $log -Tail $n | ForEach-Object { "   | $_" } } }
function Check($name, $cond) { if ($cond) { "PASS $name" } else { "FAIL $name" } }

if ($Launch) {
  Stop-Process -Name WinDots -Force -ErrorAction SilentlyContinue; Start-Sleep -m 500
  if (Test-Path $log) { Remove-Item $log -Force }
  Add-AppxPackage -Register (Join-Path $PSScriptRoot '..\..\src\WinDots.App\bin\x64\Debug\net10.0-windows10.0.26100.0\AppxManifest.xml') -ForceUpdateFromAnyVersion
  Start-Process "shell:AppsFolder\$((Get-AppxPackage -Name WinDots.Dev).PackageFamilyName)!App"
  Start-Sleep 6
}

$procs = @(Get-Process WinDots -ErrorAction SilentlyContinue)
Check 'exactly one WinDots process' ($procs.Count -eq 1)
# The app logs the monitor count it enumerated ("host ready: monitors=N"); WMI can report displays that are off.
$monitorCount = 1
$ready = Get-Content $log -ErrorAction SilentlyContinue | Select-String -Pattern 'host ready: monitors=(\d+)' | Select-Object -Last 1
if ($ready) { $monitorCount = [int]$ready.Matches[0].Groups[1].Value }
if (Get-Process LogonUI -ErrorAction SilentlyContinue) { 'NOTE workstation is locked: foreground and pixel checks cannot pass' }
"handles: $((Handles) -join '; ')"
Check "one handle per monitor (expected $monitorCount)" (@(Handles).Count -eq $monitorCount)
Check 'no drawer visible at start' (-not (Drawer))

'--- toggle on monitor 0'; Send-Cmd 2 0; Start-Sleep -m 1200
$d = Drawer; "drawer: $d"; Send-Cmd 6; Start-Sleep -m 300
Check 'drawer visible after toggle' ($null -ne $d)
Check 'drawer has full height' ($d -match ' \d+x(3\d\d|[2-9]\d\d)$')
Check 'controller Open' ((Get-Content $log -Tail 3) -match 'controller=Open')
Check 'drawer is foreground' ((Get-Content $log -Tail 3) -match 'foregroundIsDrawer=True')

'--- dismiss (Escape path)'; Send-Cmd 3; Start-Sleep -m 1200
Check 'drawer hidden after dismiss' (-not (Drawer))

'--- cross-monitor: open on 0, then toggle on last monitor'
Send-Cmd 2 0; Start-Sleep -m 1200; $first = Drawer
$last = [Math]::Max(0, $monitorCount - 1); Send-Cmd 2 $last; Start-Sleep -m 2000; $second = Drawer
"first: $first  second: $second"
if ($monitorCount -gt 1) { Check 'drawer moved to the other monitor' (($second) -and ($second -ne $first)) } else { 'SKIP cross-monitor (single monitor)' }

'--- interrupt a settle: toggle twice quickly'
Send-Cmd 3; Start-Sleep -m 1200; Send-Cmd 2 0; Start-Sleep -m 100; Send-Cmd 2 0; Start-Sleep -m 1500
Check 'drawer closed after rapid double toggle' (-not (Drawer))
Send-Cmd 2 0; Start-Sleep -m 1200; Check 'drawer reopens after interruption' ($null -ne (Drawer)); Send-Cmd 3; Start-Sleep -m 1000

'--- log tail'; LogTail 25

if ($Launch) {
  Send-Cmd 5; Start-Sleep 2
  Check 'process exited via Quit' (-not (Get-Process WinDots -ErrorAction SilentlyContinue))
  Stop-Process -Name WinDots -ErrorAction SilentlyContinue
}
