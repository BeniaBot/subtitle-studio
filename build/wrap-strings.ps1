# wrap-strings.ps1 - wrap the Hebrew string literals of one source file in Lang.T(...)
# where that is safe, and report every literal it left alone and why.
#   wrap-strings.ps1 -File src\Hero.cs            report only
#   wrap-strings.ps1 -File src\Hero.cs -Apply     rewrite the file
# Left alone (reported): already wrapped, log lines, const, case labels,
# attributes, comparisons and lookups (==, Equals, Contains, StartsWith...),
# static initializers, and concatenations with "+" - those need Lang.F with
# a {0} placeholder by hand, because the English word order is different.
# ASCII only.
param([Parameter(Mandatory = $true)][string]$File, [switch]$Apply, [switch]$Quiet)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Text; using System.Collections.Generic; using System.Text.RegularExpressions;
public static class Wrapper {
  public class Hit { public int Line; public string Kind; public string Text; }
  static bool IsHeb(string s) { foreach (char c in s) if (c >= 0x0590 && c <= 0x05FF) return true; return false; }
  static string LineOf(string t, int pos) {
    int a = t.LastIndexOf('\n', Math.Max(0, pos - 1)) + 1; int b = t.IndexOf('\n', pos); if (b < 0) b = t.Length;
    return t.Substring(a, b - a);
  }
  static string Before(string t, int pos, int n) { int a = Math.Max(0, pos - n); return t.Substring(a, pos - a); }
  public static List<Hit> Hits = new List<Hit>();
  public static string Run(string t, bool apply) {
    Hits.Clear();
    var sb = new StringBuilder(t.Length + 4096);
    int i = 0, n = t.Length, line = 1, copied = 0;
    while (i < n) {
      char c = t[i];
      if (c == '\n') { line++; i++; continue; }
      if (c == '/' && i + 1 < n && t[i + 1] == '/') { while (i < n && t[i] != '\n') i++; continue; }
      if (c == '/' && i + 1 < n && t[i + 1] == '*') { i += 2; while (i + 1 < n && !(t[i] == '*' && t[i + 1] == '/')) { if (t[i] == '\n') line++; i++; } i += 2; continue; }
      if (c == '\'') { i++; while (i < n && t[i] != '\'') { if (t[i] == '\\') i++; i++; } i++; continue; }
      bool verb = c == '@' && i + 1 < n && t[i + 1] == '"';
      if (c == '"' || verb) {
        int start = i, startLine = line;
        i += verb ? 2 : 1;
        while (i < n) {
          if (verb) { if (t[i] == '"') { if (i + 1 < n && t[i + 1] == '"') { i += 2; continue; } break; } }
          else { if (t[i] == '\\') { i += 2; continue; } if (t[i] == '"') break; }
          if (t[i] == '\n') line++;
          i++;
        }
        int end = i + 1; i = end;
        string lit = t.Substring(start, end - start);
        if (!IsHeb(lit)) continue;
        string kind = Classify(t, start, end);
        Hits.Add(new Hit { Line = startLine, Kind = kind, Text = lit.Length > 90 ? lit.Substring(0, 90) + "..." : lit });
        if (apply && kind == "wrap") {
          sb.Append(t, copied, start - copied);
          sb.Append("Lang.T(").Append(lit).Append(")");
          copied = end;
        }
        continue;
      }
      i++;
    }
    sb.Append(t, copied, n - copied);
    return sb.ToString();
  }
  static char PrevChar(string t, int pos) { int k = pos - 1; while (k >= 0 && char.IsWhiteSpace(t[k])) k--; return k >= 0 ? t[k] : '\0'; }
  static char NextChar(string t, int pos) { int k = pos; while (k < t.Length && char.IsWhiteSpace(t[k])) k++; return k < t.Length ? t[k] : '\0'; }
  static string Classify(string t, int start, int end) {
    string before = Before(t, start, 60);
    string bt = before.TrimEnd();
    string ln = LineOf(t, start).Trim();
    if (bt.EndsWith("Lang.T(") || bt.EndsWith("Lang.F(")) return "done";
    if (Regex.IsMatch(ln, @"\bLog\s*\(") ) return "log";
    if (Regex.IsMatch(ln, @"\bconst\s+string\b")) return "const";
    if (Regex.IsMatch(ln, @"^(case|default)\b")) return "case";
    if (ln.StartsWith("[")) return "attribute";
    char pc = PrevChar(t, start), nc = NextChar(t, end);
    if (bt.EndsWith("==") || bt.EndsWith("!=")) return "compare";
    int k = end; while (k < t.Length && char.IsWhiteSpace(t[k])) k++;
    if (k + 1 < t.Length && ((t[k] == '=' && t[k + 1] == '=') || (t[k] == '!' && t[k + 1] == '='))) return "compare";
    if (Regex.IsMatch(bt, @"\.(Equals|Contains|StartsWith|EndsWith|IndexOf|LastIndexOf|Replace|Split|Trim|TrimStart|TrimEnd)\s*\($")) return "compare";
    if (Regex.IsMatch(bt, @"Regex\.\w+\s*\([^)]*$")) return "compare";
    if (Regex.IsMatch(ln, @"\bstatic\b") && ln.Contains("=")) return "static";
    if (pc == '+' || nc == '+') {
      // a whole sentence joined to another literal or to a line break can be
      // translated on its own; a fragment next to a value needs Lang.F
      string body = t.Substring(start, end - start).TrimStart('@').Trim('"');
      if (body.Length > 0 && (char.IsWhiteSpace(body[0]) || char.IsWhiteSpace(body[body.Length - 1]))) return "concat";
      if (pc == '+' && !SentenceOperandBefore(t, start)) return "concat";
      if (nc == '+' && !SentenceOperandAfter(t, end)) return "concat";
      return "wrap";
    }
    return "wrap";
  }
  static bool IsSentenceStart(string rest) {
    rest = rest.TrimStart();
    return rest.StartsWith("\"") || rest.StartsWith("@\"") || rest.StartsWith("Environment.NewLine");
  }
  static bool SentenceOperandAfter(string t, int end) {
    int k = end; while (k < t.Length && char.IsWhiteSpace(t[k])) k++;
    if (k >= t.Length || t[k] != '+') return true;
    return IsSentenceStart(t.Substring(k + 1, Math.Min(40, t.Length - k - 1)));
  }
  static bool SentenceOperandBefore(string t, int start) {
    int k = start - 1; while (k >= 0 && char.IsWhiteSpace(t[k])) k--;
    if (k < 0 || t[k] != '+') return true;
    k--; while (k >= 0 && char.IsWhiteSpace(t[k])) k--;
    if (k < 0) return false;
    if (t[k] == '"') return true;
    string tail = t.Substring(Math.Max(0, k - 18), Math.Min(19, k + 1));
    return tail.EndsWith("Environment.NewLine");
  }
}
'@
$path = (Resolve-Path $File).Path
$text = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
$out = [Wrapper]::Run($text, [bool]$Apply)
$hits = [Wrapper]::Hits
if ($Apply) { [IO.File]::WriteAllText($path, $out, (New-Object Text.UTF8Encoding $false)) }
$groups = $hits | Group-Object Kind | Sort-Object Count -Descending
if (-not $Quiet) {
    Write-Host ('{0}: {1} Hebrew literals' -f (Split-Path $path -Leaf), $hits.Count)
    foreach ($g in $groups) { Write-Host ('  {0,-10} {1}' -f $g.Name, $g.Count) }
    foreach ($h in $hits) { if ($h.Kind -ne 'wrap' -and $h.Kind -ne 'done') { Write-Host ('  {0,5} {1,-9} {2}' -f $h.Line, $h.Kind, $h.Text) } }
}
