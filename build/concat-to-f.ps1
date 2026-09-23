# concat-to-f.ps1 - turn a Hebrew sentence glued together with "+" into one
# Lang.F template:   "you have " + n + " files"   ->   Lang.F("you have {0} files", n)
# A sentence cut into pieces cannot be translated: English puts the words in a
# different order. The template keeps the sentence whole.
#   concat-to-f.ps1 -File src\Qa.cs            proposals only
#   concat-to-f.ps1 -File src\Qa.cs -Apply     rewrite the file (repeat until 0)
# Every proposal must still be read by a person. The tool refuses (and lists)
# what it cannot prove safe: the first two operands both non-literal (could be
# numeric addition), unknown neighbours (== . * ...), verbatim strings, "new".
# ASCII only.
param([Parameter(Mandatory = $true)][string]$File, [switch]$Apply, [switch]$Quiet)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Text; using System.Collections.Generic;
public static class ConcatF {
  public class Tok { public char K; public int S, E; public string T; public bool Verb; }
  // K: s string, c char, n number, i identifier, p punctuation
  public class Prop { public int Line; public string Before, After, Why; public int S, E; }
  public static List<Prop> Props = new List<Prop>();
  static List<Tok> toks;
  static string src;
  static bool IsHeb(string s) { foreach (char c in s) if (c >= 0x0590 && c <= 0x05FF) return true; return false; }
  static readonly string[] Ops3 = { "<<=", ">>=" };
  static readonly string[] Ops2 = { "==", "!=", "<=", ">=", "&&", "||", "++", "--", "+=", "-=", "*=", "/=", "=>", "??", "::", "->", "%=", "&=", "|=", "^=" };
  static void Lex() {
    toks = new List<Tok>();
    string t = src; int i = 0, n = t.Length;
    bool lineStart = true;
    while (i < n) {
      char c = t[i];
      if (c == '\n') { lineStart = true; i++; continue; }
      if (char.IsWhiteSpace(c)) { i++; continue; }
      if (lineStart && c == '#') { while (i < n && t[i] != '\n') i++; continue; }
      lineStart = false;
      if (c == '/' && i + 1 < n && t[i + 1] == '/') { while (i < n && t[i] != '\n') i++; continue; }
      if (c == '/' && i + 1 < n && t[i + 1] == '*') { i += 2; while (i + 1 < n && !(t[i] == '*' && t[i + 1] == '/')) i++; i += 2; continue; }
      int s = i;
      if (c == '@' && i + 1 < n && t[i + 1] == '"') {
        i += 2;
        while (i < n) { if (t[i] == '"') { if (i + 1 < n && t[i + 1] == '"') { i += 2; continue; } break; } i++; }
        i++; toks.Add(new Tok { K = 's', S = s, E = i, T = t.Substring(s, i - s), Verb = true }); continue;
      }
      if (c == '"') {
        i++; while (i < n && t[i] != '"') { if (t[i] == '\\') i++; i++; } i++;
        toks.Add(new Tok { K = 's', S = s, E = i, T = t.Substring(s, i - s) }); continue;
      }
      if (c == '\'') {
        i++; while (i < n && t[i] != '\'') { if (t[i] == '\\') i++; i++; } i++;
        toks.Add(new Tok { K = 'c', S = s, E = i, T = t.Substring(s, i - s) }); continue;
      }
      if (char.IsDigit(c)) {
        while (i < n && (char.IsLetterOrDigit(t[i]) || t[i] == '.' || t[i] == '_')) {
          if (t[i] == '.' && !(i + 1 < n && char.IsDigit(t[i + 1]))) break;
          i++;
        }
        toks.Add(new Tok { K = 'n', S = s, E = i, T = t.Substring(s, i - s) }); continue;
      }
      if (char.IsLetter(c) || c == '_' || c == '@') {
        i++; while (i < n && (char.IsLetterOrDigit(t[i]) || t[i] == '_')) i++;
        toks.Add(new Tok { K = 'i', S = s, E = i, T = t.Substring(s, i - s) }); continue;
      }
      string op = null;
      foreach (string o in Ops3) if (string.CompareOrdinal(t, i, o, 0, 3) == 0) { op = o; break; }
      if (op == null) foreach (string o in Ops2) if (i + 1 < n && string.CompareOrdinal(t, i, o, 0, 2) == 0) { op = o; break; }
      if (op == null) op = c.ToString();
      i += op.Length;
      toks.Add(new Tok { K = 'p', S = s, E = i, T = op });
    }
  }
  static bool P(int i, string s) { return i >= 0 && i < toks.Count && toks[i].K == 'p' && toks[i].T == s; }
  static bool Id(int i) { return i >= 0 && i < toks.Count && toks[i].K == 'i'; }
  static int Match(int i) {
    string o = toks[i].T, c = o == "(" ? ")" : o == "[" ? "]" : "}";
    int d = 0;
    for (int k = i; k < toks.Count; k++) {
      if (toks[k].K != 'p') continue;
      if (toks[k].T == o) d++; else if (toks[k].T == c) { d--; if (d == 0) return k; }
    }
    return -1;
  }
  static int MatchBack(int i) {
    string c = toks[i].T, o = c == ")" ? "(" : c == "]" ? "[" : "{";
    int d = 0;
    for (int k = i; k >= 0; k--) {
      if (toks[k].K != 'p') continue;
      if (toks[k].T == c) d++; else if (toks[k].T == o) { d--; if (d == 0) return k; }
    }
    return -1;
  }
  static readonly HashSet<string> Kw = new HashSet<string> { "return", "new", "if", "else", "while", "for", "foreach", "switch", "case", "in", "is", "as", "throw", "using", "lock", "yield", "out", "ref", "await" };
  // last token of the operand that starts at p, or -1
  static int OperandEnd(int p) {
    if (p >= toks.Count) return -1;
    int e;
    Tok t = toks[p];
    if (t.K == 's' || t.K == 'c' || t.K == 'n') e = p;
    else if (t.K == 'i') { if (Kw.Contains(t.T)) return -1; e = p; }
    else if (P(p, "(")) {
      e = Match(p); if (e < 0) return -1;
      // (type)x - a cast: the operand goes on
      if (e + 1 < toks.Count && (toks[e + 1].K == 'i' || toks[e + 1].K == 's' || P(e + 1, "("))) {
        int e2 = OperandEnd(e + 1); if (e2 < 0) return -1; return e2;
      }
    }
    else return -1;
    while (true) {
      int q = e + 1;
      if (P(q, ".") && Id(q + 1)) { e = q + 1; continue; }
      if (P(q, "(") || P(q, "[")) { int m = Match(q); if (m < 0) return -1; e = m; continue; }
      break;
    }
    return e;
  }
  // first token of the operand that ends at e, or -1
  static int OperandStart(int e) {
    int s = e;
    while (true) {
      if (s < 0) return -1;
      Tok t = toks[s];
      if (P(s, ")") || P(s, "]")) {
        int o = MatchBack(s); if (o < 0) return -1;
        bool call = o - 1 >= 0 && ((Id(o - 1) && !Kw.Contains(toks[o - 1].T)) || P(o - 1, ")") || P(o - 1, "]"));
        if (call) { s = o - 1; continue; }
        s = o;
      }
      else if (t.K == 'i') { if (Kw.Contains(t.T)) return -1; }
      else if (t.K != 's' && t.K != 'c' && t.K != 'n') return -1;
      if (P(s - 1, ".")) { s = s - 2; continue; }
      break;
    }
    if (Id(s - 1) && toks[s - 1].T == "new") return -1;
    return s;
  }
  class Operand { public int S, E; }
  static string Text(int s, int e) { return src.Substring(toks[s].S, toks[e].E - toks[s].S); }
  static int LineOf(int pos) { int l = 1; for (int k = 0; k < pos; k++) if (src[k] == '\n') l++; return l; }
  static readonly HashSet<string> LeftOk = new HashSet<string> { "(", ",", "=", "?", ":", "{", ";", "=>", "+=", "??", "return" };
  static readonly HashSet<string> RightOk = new HashSet<string> { ")", ",", ";", ":", "}", "??" };
  // the literal body as it would appear inside "...", with { } doubled for string.Format
  static string Body(Tok t) { string b = t.T.Substring(1, t.T.Length - 2); return b.Replace("{", "{{").Replace("}", "}}"); }
  static bool IsLangT(int s, int e) {
    return e - s == 5 && Id(s) && toks[s].T == "Lang" && P(s + 1, ".") && Id(s + 2) && toks[s + 2].T == "T" && P(s + 3, "(") && toks[s + 4].K == 's' && !toks[s + 4].Verb && P(s + 5, ")");
  }
  static bool IsNewLine(int s, int e) { return e - s == 2 && toks[s].T == "Environment" && P(s + 1, ".") && toks[s + 2].T == "NewLine"; }
  public static string Run(string text, bool apply) {
    src = text; Props.Clear(); Lex();
    var taken = new List<int[]>();
    for (int i = 0; i < toks.Count; i++) {
      Tok t = toks[i];
      if (t.K != 's' || !IsHeb(t.T)) continue;
      bool left = P(i - 1, "+"), right = P(i + 1, "+");
      if (!left && !right) continue;
      // already inside Lang.T( "..." ) or Lang.F( "..." ): the call is the operand
      if (P(i - 1, "(") && Id(i - 2) && (toks[i - 2].T == "T" || toks[i - 2].T == "F")) continue;
      var ops = new List<Operand>();
      string why = null;
      int ee = OperandEnd(i);
      if (ee != i) why = "literal with a member call";
      ops.Add(new Operand { S = i, E = i });
      int a = i, b = i;
      while (why == null && P(a - 1, "+")) {
        int s = OperandStart(a - 2);
        if (s < 0) { why = "left operand not understood"; break; }
        ops.Insert(0, new Operand { S = s, E = a - 2 }); a = s;
      }
      while (why == null && P(b + 1, "+")) {
        int e = OperandEnd(b + 2);
        if (e < 0) { why = "right operand not understood"; break; }
        ops.Add(new Operand { S = b + 2, E = e }); b = e;
      }
      Prop pr = new Prop { Line = LineOf(toks[i].S) };
      if (why == null && P(a - 1, "(") && Id(a - 2) && (toks[a - 2].T == "Log" || toks[a - 2].T == "Dbg" || toks[a - 2].T == "WriteLine")) why = "log line - not interface text";
      if (why == null) {
        string lt = a - 1 >= 0 ? toks[a - 1].T : ";";
        string rt = b + 1 < toks.Count ? toks[b + 1].T : ";";
        if (!LeftOk.Contains(lt)) why = "left neighbour '" + lt + "'";
        else if (!RightOk.Contains(rt)) why = "right neighbour '" + rt + "'";
      }
      if (why == null && ops.Count >= 2) {
        bool s0 = toks[ops[0].S].K == 's' && ops[0].S == ops[0].E, s1 = toks[ops[1].S].K == 's' && ops[1].S == ops[1].E;
        bool l0 = IsLangT(ops[0].S, ops[0].E) || IsNewLine(ops[0].S, ops[0].E), l1 = IsLangT(ops[1].S, ops[1].E) || IsNewLine(ops[1].S, ops[1].E);
        if (!s0 && !s1 && !l0 && !l1) why = "first two operands are not text - could be addition";
      }
      StringBuilder tpl = new StringBuilder(); var args = new List<string>();
      if (why == null) {
        foreach (Operand o in ops) {
          if (o.S == o.E && toks[o.S].K == 's') {
            if (toks[o.S].Verb) { why = "verbatim string"; break; }
            tpl.Append(Body(toks[o.S]));
          }
          else if (IsLangT(o.S, o.E)) tpl.Append(Body(toks[o.S + 4]));
          else if (IsNewLine(o.S, o.E)) tpl.Append("\\r\\n");
          else { tpl.Append("{" + args.Count + "}"); args.Add(Text(o.S, o.E)); }
        }
      }
      pr.S = toks[a].S; pr.E = toks[b].E;
      pr.Before = src.Substring(pr.S, pr.E - pr.S);
      if (why == null) {
        foreach (int[] r in taken) if (pr.S < r[1] && r[0] < pr.E) { why = "overlaps another chain - run again"; break; }
      }
      if (why != null) { pr.Why = why; Props.Add(pr); continue; }
      string tplText = tpl.ToString();
      if (args.Count == 0) pr.After = "Lang.T(\"" + tplText.Replace("{{", "{").Replace("}}", "}") + "\")";
      else pr.After = "Lang.F(\"" + tplText + "\", " + string.Join(", ", args.ToArray()) + ")";
      taken.Add(new int[] { pr.S, pr.E });
      Props.Add(pr);
      i = b;
    }
    if (!apply) return text;
    var sb = new StringBuilder(text.Length + 4096); int copied = 0;
    foreach (Prop p in Props) {
      if (p.Why != null) continue;
      sb.Append(text, copied, p.S - copied).Append(p.After); copied = p.E;
    }
    sb.Append(text, copied, text.Length - copied);
    return sb.ToString();
  }
}
'@
$path = (Resolve-Path $File).Path
$text = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
$out = [ConcatF]::Run($text, [bool]$Apply)
$props = [ConcatF]::Props
if ($Apply) { [IO.File]::WriteAllText($path, $out, (New-Object Text.UTF8Encoding $false)) }
$ok = @($props | Where-Object { $_.Why -eq $null }); $no = @($props | Where-Object { $_.Why -ne $null })
if (-not $Quiet) {
    Write-Host ('{0}: {1} converted, {2} left' -f (Split-Path $path -Leaf), $ok.Count, $no.Count)
    foreach ($p in $props) {
        $b = ($p.Before -replace '\s+', ' ')
        if ($p.Why) { Write-Host ('  {0,5} LEFT  {1}   [{2}]' -f $p.Line, $b, $p.Why) }
        else { Write-Host ('  {0,5}  - {1}' -f $p.Line, $b); Write-Host ('         + {0}' -f ($p.After -replace '\s+', ' ')) }
    }
}
