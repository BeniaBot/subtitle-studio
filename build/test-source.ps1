# כללים סטטיים על קוד המקור וסקריפטי הבדיקה. בלי לבנות, בלי להריץ, שנייה.
#
# **למה:** שלושה באגים שנמצאו ב-15.9.2026 היו גלויים בטקסט של הקוד, ואף בדיקה
# לא הסתכלה על הטקסט:
#  1. **תווי בקרה בסקריפט.** ‏test-upgrade-real.ps1 נכתב דרך heredoc, והנתיב
#     'build\payload\ffmpeg.pack' הפך ל-'build\payload<FF>fmpeg.pack' (\f), ו-
#     'build\app.ico' ל-'build<BEL>pp.ico' (\a). השורות האלה רצות רק כשתיקיית
#     הבנייה הישנה חסרה, ולכן השבירה ישבה בשקט עד השדרוג הבא.
#  2. **Theme.Ltr סביב מילה עברית.** ‏״תורגמו ״ + Ltr(done + " מתוך " + count)
#     הוצג ״תורגמו מתוך 40 5 כתוביות״ - בכל תרגום, מאז שהחלון נכתב.
#  3. **גודל קובץ בתוך משפט עברי בלי Ltr.** ‏״1.2 MB מתוך 37 MB״ הוצג
#     ״MB 37 מתוך MB 1.2״ בחלון ההורדה של העדכון.
# 5-7 (0.8.1): שכבת השפה - תבניות Lang.F, טקסט כתוב בכל קריאה, וטבלת התרגום.
# צפוי: 7 בדיקות.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$pass = 0; $fail = 0
function Lines($list) { if ($list.Count -eq 0) { return '' }; return "`n     " + ($list -join "`n     ") }
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }
$heb = '[א-ת]'

$cs = @(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs)
$ps = @(Get-ChildItem (Join-Path $root 'build') -Filter *.ps1) + @(Get-ChildItem (Join-Path $root 'build') -Filter *.cmd) + @(Get-Item (Join-Path $root 'build.cmd'))

# ---- 1. תווי בקרה ----
# מותר: טאב, CR, LF. כל השאר (FF, BEL, BS, VT, 0x01...) הוא שריד של בריחת בקסלש.
$bad = @()
foreach ($f in $cs + $ps) {
    $lines = [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $m = [regex]::Match($lines[$i], '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]')
        if ($m.Success) { $bad += ("{0}:{1}  0x{2:X2}  {3}" -f $f.Name, ($i + 1), [int][char]$m.Value, ($lines[$i].Trim() -replace '[\x00-\x1F]', '¤')) }
    }
}
Check 'אין תווי בקרה בקוד ובסקריפטים (שרידי \f \a \b)' ($bad.Count -eq 0) (Lines $bad)

# ---- 2. BOM בסקריפט עם עברית ----
$bad = @()
foreach ($f in (Get-ChildItem (Join-Path $root 'build') -Filter *.ps1)) {
    $b = [IO.File]::ReadAllBytes($f.FullName)
    $hasBom = $b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF
    if (-not $hasBom -and ([Text.Encoding]::UTF8.GetString($b) -match $heb)) { $bad += $f.Name }
}
Check 'כל סקריפט PowerShell עם עברית שמור עם BOM' ($bad.Count -eq 0) ($bad -join ', ')

# ---- 3. Theme.Ltr לא עוטף עברית ----
# מחפש את הארגומנט של כל Theme.Ltr( עם ספירת סוגריים, גם כשהוא נמשך על כמה שורות.
$bad = @()
foreach ($f in $cs) {
    $t = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
    $i = 0
    while (($i = $t.IndexOf('Theme.Ltr(', $i)) -ge 0) {
        $start = $i + 'Theme.Ltr'.Length; $depth = 0; $end = -1; $inStr = $false
        for ($k = $start; $k -lt $t.Length -and $k -lt $start + 2000; $k++) {
            $ch = $t[$k]
            if ($ch -eq '"' -and $t[$k - 1] -ne '\') { $inStr = -not $inStr }
            if ($inStr) { continue }
            if ($ch -eq '(') { $depth++ } elseif ($ch -eq ')') { $depth--; if ($depth -eq 0) { $end = $k; break } }
        }
        if ($end -gt 0) {
            $arg = $t.Substring($start, $end - $start + 1)
            # הערות בתוך הארגומנט לא נחשבות
            $argNoComments = [regex]::Replace($arg, '//[^\n]*', '')
            if ($argNoComments -match $heb) {
                $line = ($t.Substring(0, $i) -split "`n").Count
                $bad += ("{0}:{1}  Theme.Ltr{2}" -f $f.Name, $line, ($arg -replace '\s+', ' '))
            }
        }
        $i = $start
    }
}
Check 'Theme.Ltr לא עוטף מילה עברית (״5 מתוך 40״ מתהפך)' ($bad.Count -eq 0) (Lines $bad)

# ---- 4. גודל קובץ בטקסט עברי ----
# FormatSize מחזיר ״37 MB״. בשורה שיש בה עברית הוא חייב להיות בתוך Theme.Ltr.
$bad = @()
foreach ($f in $cs) {
    $lines = [IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($l -notmatch 'FormatSize\(' -or $l -match '^\s*//' -or $l -match 'static string FormatSize') { continue }
        $code = [regex]::Replace($l, '//.*$', '')
        if ($code -notmatch $heb) { continue }
        $unwrapped = [regex]::Replace($code, 'Theme\.Ltr\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)', '')
        if ($unwrapped -match 'FormatSize\(') { $bad += ("{0}:{1}  {2}" -f $f.Name, ($i + 1), $l.Trim()) }
    }
}
Check 'גודל קובץ בתוך משפט עברי עטוף ב-Theme.Ltr (״MB 37״ מתהפך)' ($bad.Count -eq 0) (Lines $bad)

# ---- 5-7. שכבת השפה ----
# ‏Lang.F שנכשל ב-string.Format לא מפיל את התוכנה: הוא מחזיר את התבנית כמו שהיא,
# עם ״{1}״ באמצע המשפט. כלומר באג שעובר בשקט - לכן הוא נבדק כאן, על הטקסט.
Add-Type @'
using System; using System.Collections.Generic; using System.Text.RegularExpressions;
public static class LangScan {
  public class Call { public int Line; public char Kind; public bool Literal; public string Template; public int Args; }
  // every Lang.T( / Lang.F( with its first argument and the number of arguments after it
  public static List<Call> Find(string t) {
    var r = new List<Call>();
    int i = 0;
    while ((i = t.IndexOf("Lang.", i, StringComparison.Ordinal)) >= 0) {
      int p = i + 5;
      if (p + 1 >= t.Length || (t[p] != 'T' && t[p] != 'F') || t[p + 1] != '(' || (i > 0 && (char.IsLetterOrDigit(t[i - 1]) || t[i - 1] == '.'))) { i = p; continue; }
      var c = new Call { Kind = t[p], Line = 1 };
      for (int k = 0; k < i; k++) if (t[k] == '\n') c.Line++;
      int q = p + 2;
      while (q < t.Length && char.IsWhiteSpace(t[q])) q++;
      c.Literal = q < t.Length && t[q] == '"';
      int depth = 1, args = 0; bool first = true; var sb = new System.Text.StringBuilder();
      for (int k = q; k < t.Length && depth > 0; k++) {
        char ch = t[k];
        if (ch == '"' && !(k > 0 && t[k - 1] == '@')) {
          int s = k + 1; k++;
          while (k < t.Length && t[k] != '"') { if (t[k] == '\\') k++; k++; }
          if (first && depth == 1 && c.Template == null) c.Template = t.Substring(s, k - s);
          continue;
        }
        if (ch == '\'') { k++; while (k < t.Length && t[k] != '\'') { if (t[k] == '\\') k++; k++; } continue; }
        if (ch == '(' || ch == '[' || ch == '{') depth++;
        else if (ch == ')' || ch == ']' || ch == '}') depth--;
        else if (ch == ',' && depth == 1) { args++; first = false; }
      }
      c.Args = args;
      r.Add(c);
      i = p;
    }
    return r;
  }
  // the {n} indexes a format string uses, or null if string.Format would throw on it
  public static SortedSet<int> Holes(string f) {
    var s = new SortedSet<int>();
    for (int i = 0; i < f.Length; i++) {
      if (f[i] == '{') {
        if (i + 1 < f.Length && f[i + 1] == '{') { i++; continue; }
        int e = f.IndexOf('}', i);
        if (e < 0) return null;
        Match m = Regex.Match(f.Substring(i + 1, e - i - 1), @"^(\d+)(,-?\d+)?(:[^{}]*)?$");
        if (!m.Success) return null;
        s.Add(int.Parse(m.Groups[1].Value)); i = e;
      }
      else if (f[i] == '}') { if (i + 1 < f.Length && f[i + 1] == '}') { i++; continue; } return null; }
    }
    return s;
  }
}
'@
$badF = @(); $badLit = @()
foreach ($f in $cs) {
    if ($f.Name -eq 'Lang.cs' -or $f.Name -eq 'LangEn.cs') { continue }
    $t = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
    foreach ($c in [LangScan]::Find($t)) {
        if (-not $c.Literal) { $badLit += ('{0}:{1}  Lang.{2}(...)' -f $f.Name, $c.Line, $c.Kind); continue }
        if ($c.Kind -ne [char]'F') { continue }
        $h = [LangScan]::Holes($c.Template)
        if ($null -eq $h) { $badF += ('{0}:{1}  תבנית שבורה: {2}' -f $f.Name, $c.Line, $c.Template); continue }
        $need = if ($h.Count -gt 0) { $h.Max + 1 } else { 0 }
        if ($need -ne $c.Args -or $h.Count -ne $need) { $badF += ('{0}:{1}  {2} ערכים, {3} מקומות: {4}' -f $f.Name, $c.Line, $c.Args, $need, $c.Template) }
    }
}
Check 'כל Lang.F מקבל ערך לכל {n} בתבנית, ואף אחד מיותר' ($badF.Count -eq 0) (Lines $badF)
Check 'Lang.T ו-Lang.F מקבלים טקסט כתוב, שנכנס לטבלת התרגום' ($badLit.Count -eq 0) (Lines $badLit)

$tsv = Join-Path $root 'build\lang\en.tsv'
$bad = @()
if (Test-Path $tsv) {
    foreach ($line in [IO.File]::ReadAllLines($tsv, [Text.Encoding]::UTF8)) {
        if ($line.StartsWith('#') -or $line.Trim().Length -eq 0) { continue }
        $p = $line -split "`t", 2
        if ($p.Length -lt 2 -or $p[1].Trim().Length -eq 0) { continue }
        $a = [LangScan]::Holes($p[0]); $b = [LangScan]::Holes($p[1])
        $sa = if ($a) { ($a | ForEach-Object { $_ }) -join ',' } else { 'x' }
        $sb = if ($b) { ($b | ForEach-Object { $_ }) -join ',' } else { 'x' }
        if ($null -eq $a -or $null -eq $b -or $sa -ne $sb) { $bad += ('{0}  ->  {1}' -f $p[0], $p[1]) }
    }
}
Check 'בטבלת התרגום, לאנגלית אותם {n} כמו לעברית' ($bad.Count -eq 0) (Lines $bad)

Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
