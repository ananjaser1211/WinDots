<#
.SYNOPSIS
  Captures the collapsed handle pill (rest and hover) at the top-centre of the primary monitor.
  One app launch, no test player (keeps audio-session churn minimal). Moving the cursor to hover is a
  cursor move only - never keyboard injection. Writes tests/artifacts/handle-rest.png and handle-hover.png.
#>
$ErrorActionPreference = 'Stop'
if (Get-Process LogonUI -ErrorAction SilentlyContinue) { throw 'Workstation is locked: captures would be black.' }
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public class WH {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
  // Reports the width x height of the topmost-edge (T<=2) visible window owned by the pid - the handle pill.
  public static string TopEdge(uint pid){ string s="none"; EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==pid && IsWindowVisible(h)){ R r; GetWindowRect(h,out r); if(r.T<=2 && (r.Rt-r.L)<400){ s=string.Format("{0}x{1}", r.Rt-r.L, r.B-r.T); } } return true;}, IntPtr.Zero); return s; }
}
"@
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$art = Join-Path $root 'tests\artifacts'; New-Item -ItemType Directory -Force $art | Out-Null
$pfn = (Get-AppxPackage -Name WinDots.Dev).PackageFamilyName
$screenW = [WH]::GetSystemMetrics(0)   # SM_CXSCREEN (primary)
$cx = [int]($screenW / 2)
# Capture band: top strip, centred, wide enough to show rest (160) and hover (200) plus margin.
$w = 240; $h = 16; $x = $cx - [int]($w/2); $y = 0
function Grab($name) {
  $b = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($x, $y, 0, 0, $b.Size); $g.Dispose()
  $p = Join-Path $art $name; $b.Save($p); $b.Dispose(); "captured $p"
}
function Send-Quit { $hnd=[WH]::FindWindowW('WinDots.ShellMessageWindow',[NullString]::Value); if ($hnd -ne [IntPtr]::Zero) { [void][WH]::PostMessageW($hnd, 0x8002, [IntPtr]5, [IntPtr]0) } }

Stop-Process -Name WinDots,WinDots.TestPlayer -Force -ErrorAction SilentlyContinue; Start-Sleep -m 500
Add-AppxPackage -Register (Join-Path $root 'src\WinDots.App\bin\x64\Debug\net10.0-windows10.0.26100.0\AppxManifest.xml') -ForceUpdateFromAnyVersion
# Park the cursor away from the top edge so the first capture is the true rest state.
[WH]::SetCursorPos($cx, 400) | Out-Null
Start-Process "shell:AppsFolder\$pfn!App"; Start-Sleep 6
$appPid = (Get-Process WinDots -ErrorAction SilentlyContinue | Select-Object -First 1).Id
Grab 'handle-rest.png'
"rest handle size:  $([WH]::TopEdge([uint32]$appPid))"
# Hover: move the cursor onto the pill and jiggle it so WinUI registers a PointerEntered (a single teleport
# sometimes does not produce a move delta), then let the grow+brighten settle.
foreach ($dx in 0,2,-1,1,0) { [WH]::SetCursorPos($cx + $dx, 2) | Out-Null; Start-Sleep -m 80 }
Start-Sleep -m 450
"hover handle size: $([WH]::TopEdge([uint32]$appPid))"
Grab 'handle-hover.png'
[WH]::SetCursorPos($cx, 400) | Out-Null
Send-Quit; Start-Sleep 2
Stop-Process -Name WinDots -Force -ErrorAction SilentlyContinue
