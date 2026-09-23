# hang-report.ps1 - a test that "hangs" is often a .NET crash dialog waiting for a click.
# Lists every powershell test process, and if one shows the "Unhandled exception"
# window, opens its Details and prints the stack trace. -Kill closes the stuck ones.
# ASCII only.
param([switch]$Kill)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class HangWin {
  public delegate bool Cb(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(Cb c, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, Cb c, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  static string T(IntPtr c) { int n = (int)SendMessage(c, 0x000E, IntPtr.Zero, IntPtr.Zero); var sb = new StringBuilder(n + 1); SendMessage(c, 0x000D, (IntPtr)(n + 1), sb); return sb.ToString(); }
  static List<IntPtr> Children(uint pid) {
    var r = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) EnumChildWindows(h, (c, x) => { r.Add(c); return true; }, IntPtr.Zero); return true; }, IntPtr.Zero);
    return r; }
  public static bool HasCrash(uint pid) { foreach (var c in Children(pid)) if (T(c).StartsWith("Unhandled exception")) return true; return false; }
  public static void OpenDetails(uint pid) { foreach (var c in Children(pid)) if (T(c) == "&Details") SendMessage(c, 0x00F5, IntPtr.Zero, IntPtr.Zero); }
  public static string Longest(uint pid) { string best = ""; foreach (var c in Children(pid)) { string t = T(c); if (t.Length > best.Length) best = t; } return best; }
}
'@
$procs = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -match '\.ps1' })
if ($procs.Count -eq 0) { Write-Host 'no test processes'; exit 0 }
foreach ($p in $procs)
{
    $id = [uint32]$p.ProcessId
    $script = ([regex]::Match($p.CommandLine, '[^\\ "]+\.ps1')).Value
    $age = [int]((Get-Date) - $p.CreationDate).TotalMinutes
    if ([HangWin]::HasCrash($id))
    {
        [HangWin]::OpenDetails($id)
        Start-Sleep -Milliseconds 700
        $txt = [HangWin]::Longest($id)
        $cut = $txt.IndexOf('************** Loaded Assemblies')
        if ($cut -gt 0) { $txt = $txt.Substring(0, $cut) }
        Write-Host ("CRASHED  pid {0}  {1}  ({2} min)" -f $id, $script, $age) -ForegroundColor Red
        Write-Host $txt
        if ($Kill) { Stop-Process -Id $id -Force; Write-Host '  killed' }
    }
    else
    {
        Write-Host ("running  pid {0}  {1}  ({2} min)" -f $id, $script, $age)
    }
}
