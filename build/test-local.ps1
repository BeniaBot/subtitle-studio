# התמלול על המחשב (LocalStt.cs, 0.8.45) - בלי המנוע האמיתי ובלי רשת.
#
# **מנוע מדומה:** EXE קטן שמהודר כאן (csc) בשם של המנוע האמיתי, ומדפיס את אותו פלט - שורת JSON בערוץ
# השגיאות. הוא רושם איך הופעל (ארגומנטים, תיקיית עבודה, מה יש בקובץ הקול), ויודע גם להיתקע, ליפול
# ולהחזיר זבל. כך נבדק כל מה שהקוד שלנו עושה סביב המנוע: שמות יחסיים מתיקייה עברית, שקט בתחילה ובסוף,
# תיקון האיחור בזמנים, חיבור קטעים בלי לאבד מילה ובלי להכפיל, עצירה, ונפילות.
# **שרת הורדות מדומה:** TcpListener שמגיש קבצים עם Range, ויודע לנתק באמצע, להחזיר דף חסימה, 404.
#
# **מה לא נבדק כאן:** המנוע והמודל האמיתיים. נמדדו ביד (15.11.2026: WER 10.1%, נתיב עברי, 15 דקות
# בארבעה קטעים בלי מילה חסרה או כפולה בתפרים) - docs\RESEARCH-local-models.md.
# צפוי: 111 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$SF = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IF = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i=0;$i -lt $args.Count;$i++){ $v=$args[$i]; if ($null -ne $v) { $v = $v.psobject.BaseObject }; $a[$i]=$v }; return ,$a }
$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }

$work = [IO.Path]::GetFullPath((Join-Path $env:TEMP ('ss-local-' + [guid]::NewGuid().ToString('N').Substring(0, 6))))
New-Item -ItemType Directory $work | Out-Null
$lsT = T 'LocalStt'; $lmT = T 'LocalModel'; $sttT = T 'Stt'; $wordT = T 'OpenAiStt+SttWord'
[void](T 'Runtime').GetMethod('Prepare', $SF).Invoke($null, @())
$ff = [string](T 'Ff').GetProperty('Exe', $SF).GetValue($null, $null)
$delay = [double]$lsT.GetField('DelaySec', $SF).GetValue($null)
$headPad = [double]$lsT.GetField('HeadPadSec', $SF).GetValue($null)
$tailPad = [double]$lsT.GetField('TailPadSec', $SF).GetValue($null)
$tail = [double]$lsT.GetField('TailSec', $SF).GetValue($null)

# ---------------- חלקי מילים ← מילים ----------------
Write-Host "`n--- words from tokens"
$wft3 = $lsT.GetMethods($SF) | Where-Object { $_.Name -eq 'WordsFromTokens' -and $_.GetParameters().Count -eq 3 }
function Words([string[]]$tok, [double[]]$at, [double]$shift) {
    return ,$wft3.Invoke($null, (Pack $tok $at $shift))
}
function Texts($ws) { return (@($ws | ForEach-Object { $_.Text }) -join '|') }
$w = Words @(' ', 'ות', 'כ', 'ף', ' אני', ' לא') @(1.5, 1.6, 1.9, 2.0, 2.6, 2.8) 1.28
Check 'רווח פותח מילה, והחלקים שאחריו מצטרפים אליה' ((Texts $w) -eq 'ותכף|אני|לא') (Texts $w)
Check 'תחילת המילה = החלק הראשון שלה, פחות האיחור' ([Math]::Abs($w[0].Start - 0.22) -lt 0.001 -and [Math]::Abs($w[1].Start - 1.32) -lt 0.001) ("" + $w[0].Start + ' ' + $w[1].Start)
$w = Words @(' שלום', '.', ' עולם') @(2.0, 3.5, 4.0) 0
Check 'נקודה נדבקת למילה שלפניה' ((Texts $w) -eq 'שלום.|עולם') (Texts $w)
Check 'נקודה שדווחה אחרי ההפסקה לא מותחת את המילה לתוכה' ([Math]::Abs($w[0].End - (2.0 + $tail)) -lt 0.001) ("" + $w[0].End)
$w = Words @(' א', ' ב') @(1.0, 1.1) 0
Check 'סוף מילה לא עובר את תחילת הבאה' ([Math]::Abs($w[0].End - 1.1) -lt 0.001 -and $w[1].End -gt $w[1].Start) ("" + $w[0].End)
$w = Words @(' א', ' ב') @(0.3, 2.0) 1.28
Check 'זמן שיוצא שלילי אחרי התיקון - אפס' ($w[0].Start -eq 0 -and $w[0].End -gt 0) ("" + $w[0].Start)
$w = Words @(' ולה', 'ט', '"', 'ב', 'ים') @(1, 1.1, 1.2, 1.3, 1.4) 0
Check 'גרשיים באמצע מילה נשארים בה (להט"בים)' ($w.Count -eq 1 -and $w[0].Text -eq 'ולהט"בים') (Texts $w)
$w = Words @(([string][char]0x2581 + 'THE'), ' ', ' ', 'x', '') @(1, 2, 3, 4, 5) 0
Check 'גם הסימן של SentencePiece פותח מילה, ורווחים ריקים לא יוצרים מילה ריקה' ((Texts $w) -eq 'THE|x') (Texts $w)

# מילה שהמודל לא בטוח בה
$wp = $lsT.GetMethods($SF) | Where-Object { $_.Name -eq 'WordsFromTokens' -and $_.GetParameters().Count -eq 4 }
$w = $wp.Invoke($null, (Pack ([string[]]@(' שלום', ' עו', 'לם', '.', ' טוב')) ([double[]]@(1, 2, 2.2, 2.4, 3)) ([double]0) ([double[]]@(-0.2, -0.3, -2.1, -3.0, -1.4))))
Check 'מילה שחלק ממנה חלש מסומנת ״לא בטוחה״ - סביב האותיות, והנקודה נשארת בחוץ' ((Texts $w) -eq ('שלום|' + [char]0x27E6 + 'עולם' + [char]0x27E7 + '.|טוב')) (Texts $w)
Check 'נקודה חלשה לבדה לא מסמנת את המילה, וגם לא מה שמעל הסף' (-not ($w[0].Text -match '\u27E6') -and -not ($w[2].Text -match '\u27E6')) ''

# ---------------- קריאת הפלט של המנוע ----------------
Write-Host "`n--- engine output"
# פלט אמיתי של sherpa-onnx 2.0.1 (15.11.2026), קטע 000 מ״כאן״ - בלי השקט שלנו בהתחלה
$real = @'
D:\a\sherpa-onnx\sherpa-onnx\sherpa-onnx\csrc\parse-options.cc:Read:374 'sherpa-onnx.exe' '--tokens=tokens.txt' c.wav

OnlineRecognizerConfig(feat_config=FeatureExtractorConfig(sampling_rate=16000, feature_dim=80), decoding_method="greedy_search")

c.wav
Number of threads: 4, Elapsed seconds: 0.26, Audio duration (s): 3.1, Real time factor (RTF) = 0.26/3.1 = 0.085
ותכף אני לא אוכל להשיג את כל הציוד
{ "text": "ותכף אני לא אוכל להשיג את כל הציוד", "tokens": [" ", "ות", "כ", "ף", " אני", " לא", " או", "כל", " לה", "שי", "ג", " את", " כל", " ה", "צי", "וד"], "timestamps": [0.88, 0.96, 1.28, 1.40, 2.00, 2.20, 2.44, 2.60, 2.76, 2.84, 3.04, 3.12, 3.24, 3.40, 3.48, 3.64], "ys_probs": [-0.811018, -0.558165], "lm_probs": [], "context_scores": [], "segment": 0, "words": [], "num_trailing_blanks": 4, "start_time": 0.00, "is_final": false, "is_eof": false}
'@
function ParseOut([string]$s) {
    $argv = New-Object object[] 2; $argv[0] = $s; $argv[1] = $null
    $r = $lsT.GetMethod('Parse', $SF).Invoke($null, $argv)
    return @{ words = $r; err = $argv[1] }
}
$p = ParseOut $real
Check 'פלט אמיתי של המנוע: שבע מילים, בסדר' ($p.err -eq $null -and (Texts $p.words) -eq 'ותכף|אני|לא|אוכל|להשיג|את|כל|הציוד') ((Texts $p.words) + ' ' + $p.err)
$shift = $headPad + $delay
Check 'הזמנים: פחות השקט שהוספנו ופחות האיחור' ([Math]::Abs($p.words[1].Start - (2.00 - $shift)) -lt 0.001 -and [Math]::Abs($p.words[7].Start - (3.40 - $shift)) -lt 0.001) ("" + $p.words[1].Start + ' ' + $p.words[7].Start)
$p = ParseOut "Start to create recognizer`nno json here"
Check 'פלט בלי תשובה - שגיאה במילים, לא חריגה' ($p.words -eq $null -and [string]$p.err -match 'לא החזיר תשובה') ([string]$p.err)
$p = ParseOut '{ "text": "x", "tokens": [" a", " b"], "timestamps": [1.0] }'
Check 'מספר זמנים שונה ממספר החלקים - שגיאה' ($p.words -eq $null -and [string]$p.err -match 'פורמט') ([string]$p.err)

# ---------------- חיבור קטעים ----------------
Write-Host "`n--- stitching"
$listT = [System.Collections.Generic.List`1].MakeGenericType($wordT)
function WList($items) {
    $l = [Activator]::CreateInstance($listT)
    foreach ($it in $items) { $x = [Activator]::CreateInstance($wordT, $true); $x.Start = [double]$it[0]; $x.End = [double]$it[0] + 0.3; $x.Text = [string]$it[1]; $l.Add($x) }
    return ,$l
}
function Stitch($a, $b, [double]$from, [double]$to) { [void]$lsT.GetMethod('Stitch', $SF).Invoke($null, (Pack $a $b $from $to)) }
# דיבור רצוף: מילה כל 0.4 שניות, 280-306. הקטע הקודם שמע עד 300, החדש מתחיל ב-294
$script = @(); for ($i = 0; $i -lt 65; $i++) { $script += ,@((280 + $i * 0.4), ('מילה' + $i)) }
$prev = WList @($script | Where-Object { $_[0] -lt 299.7 })
$nextItems = @($script | Where-Object { $_[0] -ge 294 } | ForEach-Object { ,@(($_[0] + 0.07), $_[1]) })
$next = WList $nextItems
Stitch $prev $next 294 300
Check 'חפיפה עם זמנים שזזו ב-0.07: כל מילה פעם אחת, בסדר' ((Texts $prev) -eq (@($script | ForEach-Object { $_[1] }) -join '|')) ("" + $prev.Count + ' / ' + $script.Count)
$prev = WList @($script | Where-Object { $_[0] -lt 299.7 })
$garbled = @($nextItems | ForEach-Object { if ($_[0] -lt 295.2) { ,@($_[0], 'זבל') } else { ,$_ } })
$next = WList $garbled
Stitch $prev $next 294 300
Check 'תחילת הקטע החדש משובשת (התחיל באמצע מילה): לא נכנסת' ((Texts $prev) -eq (@($script | ForEach-Object { $_[1] }) -join '|')) ''
$rep = @(); for ($i = 0; $i -lt 40; $i++) { $rep += ,@((286 + $i * 0.4), 'לא') }
$prev = WList @($rep | Where-Object { $_[0] -lt 299.7 }); $next = WList @($rep | Where-Object { $_[0] -ge 294 } | ForEach-Object { ,@(($_[0] - 0.05), $_[1]) })
Stitch $prev $next 294 300
Check 'אותה מילה שוב ושוב בחפיפה: המספר נשמר' ($prev.Count -eq 40) ("" + $prev.Count)
$prev = WList @(@(280, 'א'), @(284, 'ב'), @(290, 'ג')); $next = WList @(@(297.2, 'ד'), @(298, 'ה'), @(301, 'ו'))
Stitch $prev $next 294 300
Check 'בלי מילה משותפת (שקט בקטע הקודם): שום דבר לא נכפל ולא אובד' ((Texts $prev) -eq 'א|ב|ג|ד|ה|ו') (Texts $prev)
$prev = WList @(@(280, 'א'), @(295.0, 'ב'), @(296.0, 'ג'), @(298.6, 'ד')); $next = WList @(@(295.1, 'שונה'), @(296.3, 'אחרת'), @(298.9, 'לגמרי'), @(301, 'ו'))
Stitch $prev $next 294 300
$t = Texts $prev
Check 'הקטעים לא מסכימים בחפיפה: חיתוך בהפסקה הגדולה, בלי חפיפה בזמן' ($t.StartsWith('א|ב|ג') -and $t.EndsWith('ו') -and (@($prev | Where-Object { $_.Text -eq 'ד' -or $_.Text -eq 'לגמרי' }).Count -eq 1)) $t
$prev = WList @(); $next = WList @(@(1, 'א'), @(2, 'ב'))
Stitch $prev $next 0 6
Check 'אין קודם - הכול מהחדש' ((Texts $prev) -eq 'א|ב') ''
$prev = WList @(,@(1, 'א')); $next = WList @()
Stitch $prev $next 0 6
Check 'קטע חדש ריק - הקודם נשאר' ((Texts $prev) -eq 'א') ''

# ---------------- המנוע המדומה ----------------
Write-Host "`n--- fake engine"
$fakeSrc = @'
using System; using System.IO; using System.Text; using System.Globalization; using System.Collections.Generic;
class P {
  static int Main(string[] a) {
    string wav = a[a.Length - 1];
    string mode = File.Exists("fake-mode.txt") ? File.ReadAllText("fake-mode.txt").Trim() : "ok";
    long len = File.Exists(wav) ? new FileInfo(wav).Length : -1;
    int rate = 0, ch = 0, bits = 0; bool tail = false, head = false; double sec = 0;
    if (len > 44) {
      byte[] b = File.ReadAllBytes(wav);
      ch = BitConverter.ToInt16(b, 22); rate = BitConverter.ToInt32(b, 24); bits = BitConverter.ToInt16(b, 34);
      int d = 12; while (d + 8 < b.Length && !(b[d] == 'd' && b[d+1] == 'a' && b[d+2] == 't' && b[d+3] == 'a')) d += 8 + BitConverter.ToInt32(b, d + 4);
      int start = d + 8, n = b.Length - start; sec = n / (rate * 2.0);
      tail = true; for (int i = b.Length - (int)(1.5 * rate) * 2; i < b.Length; i++) if (b[i] != 0) { tail = false; break; }
      head = true; for (int i = start; i < start + (int)(0.4 * rate) * 2; i++) if (b[i] != 0) { head = false; break; }
    }
    string hot = "";
    foreach (string x in a) if (x.StartsWith("--hotwords-file=")) { string hf = x.Substring(16); hot = File.Exists(hf) ? File.ReadAllText(hf, Encoding.UTF8).Replace("\n", ";") : "MISSING"; }
    File.AppendAllText("fake-calls.log", string.Join(" ", a) + "|cwd=" + Directory.GetCurrentDirectory() + "|rate=" + rate + "|ch=" + ch + "|bits=" + bits +
      "|sec=" + sec.ToString("0.00", CultureInfo.InvariantCulture) + "|tail=" + tail + "|head=" + head + "|hot=" + hot + "\n", new UTF8Encoding(false));
    Console.Out.WriteLine("Start to create recognizer");
    if (mode == "hang") { System.Threading.Thread.Sleep(60000); return 0; }
    if (mode == "fail") { Console.Error.WriteLine("boom"); return 3; }
    Stream es = Console.OpenStandardError();
    if (mode == "garbage") { byte[] g = Encoding.UTF8.GetBytes("no json here\n"); es.Write(g, 0, g.Length); return 0; }
    // words: "<seconds in the whole file>\t<word>". The chunk index is in the wav name (c<tag>_<i>.wav); fake-step.txt = seconds per chunk step.
    int idx = 0; string nm = Path.GetFileNameWithoutExtension(wav); int us = nm.LastIndexOf('_');
    if (us > 0) int.TryParse(nm.Substring(us + 1), out idx);
    double step = File.Exists("fake-step.txt") ? double.Parse(File.ReadAllText("fake-step.txt").Trim(), CultureInfo.InvariantCulture) : 294;
    double off = File.Exists("fake-offset.txt") ? double.Parse(File.ReadAllText("fake-offset.txt").Trim(), CultureInfo.InvariantCulture) : 0;
    double real = sec - 2.5;                      // without the silence we were given
    double jitter = 0.04 * ((idx % 3) - 1);      // each run hears the same word a frame earlier or later
    List<string> toks = new List<string>(); List<string> ts = new List<string>(); List<string> probs = new List<string>(); StringBuilder text = new StringBuilder();
    if (File.Exists("fake-words.txt"))
      foreach (string line in File.ReadAllLines("fake-words.txt", Encoding.UTF8)) {
        int tab = line.IndexOf('\t'); if (tab < 0) continue;
        double rel = double.Parse(line.Substring(0, tab), CultureInfo.InvariantCulture) - off - idx * step;
        if (rel < 0 || rel > real - 0.3) continue;                      // cut in the middle of a word: gone
        string word = (idx > 0 && rel < 1.0) ? "~~" : line.Substring(tab + 1);   // a cold start hears rubbish
        string pr = "-0.30"; if (word.StartsWith("!")) { word = word.Substring(1); pr = "-2.50"; }      // "!" = the model is unsure
        probs.Add(pr);
        toks.Add("\" " + word.Replace("\"", "\\\"") + "\""); ts.Add((rel + 0.5 + 0.78 + jitter).ToString("0.00", CultureInfo.InvariantCulture));
        text.Append(word).Append(' ');
      }
    string json = "{ \"text\": \"" + text.ToString().Trim().Replace("\"", "\\\"") + "\", \"tokens\": [" + string.Join(", ", toks.ToArray()) + "], \"timestamps\": [" +
      string.Join(", ", ts.ToArray()) + "], \"ys_probs\": [" + string.Join(", ", probs.ToArray()) + "], \"segment\": 0, \"is_final\": false}";
    byte[] o = Encoding.UTF8.GetBytes(wav + "\nNumber of threads: 1\n" + text + "\n" + json + "\n");
    es.Write(o, 0, o.Length);
    return 0;
  }
}
'@
# תיקייה עם עברית ורווח בשם - כמו C:\Users\בנימין. המנוע האמיתי נשבר כאן כשמעבירים לו נתיב מלא
$model = Join-Path $work 'מודל עברי\stt-he'
New-Item -ItemType Directory $model | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
[IO.File]::WriteAllText((Join-Path $work 'fake.cs'), $fakeSrc)
& $csc /nologo /target:exe /out:"$work\fake.exe" "$work\fake.cs" | Out-Null
Copy-Item "$work\fake.exe" (Join-Path $model 'sherpa-onnx.exe')
$realFiles = $lmT.GetField('Files', $SF).GetValue($null); $realSizes = $lmT.GetField('Sizes', $SF).GetValue($null)
$realGz = $lmT.GetField('GzSizes', $SF).GetValue($null); $realSha = $lmT.GetField('Sha256', $SF).GetValue($null)
$realUrl = [string]$lmT.GetField('BaseUrl', $SF).GetValue($null)
Check 'הטבלה: שמונה קבצים, לכל אחד גודל, גודל דחוס וטביעה' ($realFiles.Count -eq 8 -and $realSizes.Count -eq 8 -and $realGz.Count -eq 8 -and $realSha.Count -eq 8 -and ($realSha | Where-Object { $_.Length -ne 64 }).Count -eq 0) ''
$dlBytes = [long]$lmT.GetProperty('DownloadBytes', $SF).GetValue($null, $null)
Check 'ההורדה: כ-62 מגה, מהמאגר שלנו, ממהדורה נפרדת' ($dlBytes -gt 60MB -and $dlBytes -lt 70MB -and $realUrl -eq 'https://github.com/BeniaBot/subtitle-studio/releases/download/local-stt-he-1/') ("" + $dlBytes + ' ' + $realUrl)

function UseFake {
    $lmT.GetField('FolderOverride', $SF).SetValue($null, [string]$model)
    $lmT.GetField('Files', $SF).SetValue($null, [string[]]@('sherpa-onnx.exe'))
    $lmT.GetField('Sizes', $SF).SetValue($null, [long[]]@((Get-Item (Join-Path $model 'sherpa-onnx.exe')).Length))
}
function Mode([string]$m) { [IO.File]::WriteAllText((Join-Path $model 'fake-mode.txt'), $m); Remove-Item (Join-Path $model 'fake-calls.log') -ErrorAction SilentlyContinue }
function Calls { $p = Join-Path $model 'fake-calls.log'; if (Test-Path $p) { return @([IO.File]::ReadAllLines($p, [Text.Encoding]::UTF8)) } else { return @() } }
function WriteWords($items) { [IO.File]::WriteAllLines((Join-Path $model 'fake-words.txt'), [string[]]@($items | ForEach-Object { ('{0:0.00}' -f $_[0]) + "`t" + $_[1] }), (New-Object Text.UTF8Encoding $false)) }
$loc = $sttT.GetProperty('Local', $SF).GetValue($null, $null)
$runFileM = $lsT.GetMethods($IF) | Where-Object { $_.Name -eq 'RunFile' -and $_.GetParameters().Count -eq 7 }
$script:ctx = $null
function RunFile([string]$media, [long]$dur, $cancel, [long]$from, [long]$to) {
    $argv = New-Object object[] 7
    $argv[0] = $media; $argv[1] = $dur; $argv[2] = $script:ctx; $argv[3] = $null; $argv[4] = $cancel; $argv[5] = $from; $argv[6] = $to
    return $runFileM.Invoke($loc, $argv)
}
function CueText($res) { return (@($res.Cues | ForEach-Object { $_.Text }) -join ' ') }

# לפני שהורד
$lmT.GetField('FolderOverride', $SF).SetValue($null, [string](Join-Path $work 'nothing-here'))
$res = RunFile 'x.mp4' 40000 $null 0 0
Check 'לפני ההורדה: הודעה שאומרת מאיפה מורידים, ושום הרצה' ([string]$res.Error -match 'עוד לא הורד' -and -not [bool]$loc.HasKey) ([string]$res.Error)
UseFake
Check 'אחרי: ״מותקן״ לפי הקבצים והגודל שלהם' ([bool]$lmT.GetProperty('Installed', $SF).GetValue($null, $null) -and [bool]$loc.HasKey -and -not [bool]$loc.SendsAudioOut) ''
[IO.File]::WriteAllText((Join-Path $model 'sherpa-onnx.exe.x'), 'x'); $lmT.GetField('Sizes', $SF).SetValue($null, [long[]]@(5))
Check 'קובץ בגודל אחר (הורדה שנקטעה, קובץ שנפגע) - לא ״מותקן״' (-not [bool]$lmT.GetProperty('Installed', $SF).GetValue($null, $null)) ''
Remove-Item (Join-Path $model 'sherpa-onnx.exe.x'); UseFake

# קובץ קצר, סטריאו - קטע אחד
$short = Join-Path $work 'short.wav'
& $ff -y -hide_banner -v error -f lavfi -i "sine=frequency=330:duration=40" -ac 2 -ar 44100 $short
$scriptShort = @(@(3.00, 'שלום'), @(3.50, 'לכולם.'), @(10.00, 'זה'), @(10.40, 'משפט'), @(10.80, 'שני.'), @(39.20, 'סוף.'))
WriteWords $scriptShort
Mode 'ok'
$res = RunFile $short 40000 $null 0 0
$calls = @(Calls)
Check 'קובץ של 40 שניות: הרצה אחת' ($calls.Count -eq 1 -and [int]$res.Chunks -eq 1 -and [int]$res.Failed -eq 0) ("" + $calls.Count)
$argsPart = $calls[0].Split('|')[0]
Check 'הארגומנטים: רק שמות יחסיים באנגלית, חיפוש רחב, ובלי רשימת מילים כשאין רקע' ($argsPart -match '^--tokens=tokens\.txt --encoder=encoder\.int8\.onnx --decoder=decoder\.int8\.onnx --joiner=joiner\.int8\.onnx --num-threads=[1-4] --decoding-method=modified_beam_search work\\c[\d_]+\.wav$') $argsPart
Check 'המנוע רץ מתוך תיקיית המודל (גם כשהשם שלה בעברית)' ($calls[0] -match [regex]::Escape('|cwd=' + $model + '|')) ''
Check 'הקול שהמנוע קיבל: 16 אלף הרץ, ערוץ אחד, 16 ביט' ($calls[0] -match '\|rate=16000\|ch=1\|bits=16\|') $calls[0].Substring($calls[0].IndexOf('|rate'))
$sec = [double]([regex]::Match($calls[0], '\|sec=([\d.]+)').Groups[1].Value)
Check 'אורך הקול: הקטע + חצי שנייה לפני + שתיים אחרי' ([Math]::Abs($sec - (40 + $headPad + $tailPad)) -lt 0.15) ("" + $sec)
Check 'שקט בסוף ובהתחלה (גם בסטריאו) - בלי זה המילה האחרונה נחתכת' ($calls[0] -match '\|tail=True\|head=True') ''
Check 'הכתוביות: לפי משפטים' ((CueText $res) -eq 'שלום לכולם. זה משפט שני. סוף.' -and $res.Cues.Count -eq 3) ((CueText $res) + ' / ' + $res.Cues.Count)
# המנוע המדומה מזיז כל הרצה בפריים (0.04) - כמו האמיתי; לכן 50 אלפיות
Check 'הזמנים: של הסרט, אחרי תיקון השקט והאיחור' ([Math]::Abs($res.Cues[0].Start - 3000) -le 50 -and [Math]::Abs($res.Cues[1].Start - 10000) -le 50) ("" + $res.Cues[0].Start + ' ' + $res.Cues[1].Start)
Check 'שום כתובית לא עוברת את סוף הקובץ' (@($res.Cues | Where-Object { $_.End -gt 40000 }).Count -eq 0) ''
Check 'קובץ הקול הזמני נמחק' (@(Get-ChildItem (Join-Path $model 'work') -ErrorAction SilentlyContinue).Count -eq 0) ''

# רק קטע
Mode 'ok'
$res = RunFile $short 40000 $null 9000 20000
$calls = @(Calls)
$sec = [double]([regex]::Match($calls[0], '\|sec=([\d.]+)').Groups[1].Value)
[IO.File]::WriteAllText((Join-Path $model 'fake-offset.txt'), '9')
Mode 'ok'
$res = RunFile $short 40000 $null 9000 20000
Remove-Item (Join-Path $model 'fake-offset.txt')
Check 'רק הקטע המסומן: הקול נחתך בגבולות שלו, והזמנים של הסרט המלא' ([Math]::Abs($sec - (11 + $headPad + $tailPad)) -lt 0.15 -and (CueText $res) -eq 'זה משפט שני.' -and [Math]::Abs($res.Cues[0].Start - 10000) -le 50) ("" + $sec + ' ' + (CueText $res))

# מילה לא בטוחה, מקצה לקצה: הסימון יורד מהטקסט ונשמר בכתובית
Mode 'ok'; WriteWords @(@(3.00, 'שלום'), @(3.50, '!לכולם.'), @(10.00, 'זה'), @(10.40, 'משפט.'))
$res = RunFile $short 40000 $null 0 0
Check 'בכתובית: הטקסט נקי, והמילה רשומה כ״לא בטוחה״ (כמו בתמלול של גוגל)' ((CueText $res) -eq 'שלום לכולם. זה משפט.' -and [string]$res.Cues[0].Doubt -eq 'לכולם' -and [string]$res.Cues[1].Doubt -eq '') ((CueText $res) + ' / ' + $res.Cues[0].Doubt)
$p = ParseOut '{ "text": "x", "tokens": [" אחת", " שתיים"], "timestamps": [2.0, 3.0], "ys_probs": [-0.1] }'
Check 'ציוני ביטחון באורך אחר (גרסה אחרת של המנוע) - מתמללים בלי סימון, לא נופלים' ($p.err -eq $null -and (Texts $p.words) -eq 'אחת|שתיים') ((Texts $p.words) + ' ' + $p.err)

# ״רקע על התוכן״ ← מילים שהמנוע מעדיף
function Hot([string]$c) { return ,@($lsT.GetMethod('Hotwords', $SF).Invoke($null, @([string]$c))) }
$h = Hot ('שיעור בגמרא ' + [char]0xB7 + ' הרב כהן, קדאפי; סנטו דומינגו. Hello 2024 רמב' + [char]0x5F4 + 'ם')
Check 'מילים עבריות של שלוש אותיות ומעלה, וכל צירוף קצר גם כצירוף' (($h -contains 'שיעור') -and ($h -contains 'בגמרא') -and ($h -contains 'שיעור בגמרא') -and ($h -contains 'הרב כהן') -and ($h -contains 'קדאפי') -and ($h -contains 'סנטו דומינגו') -and ($h -contains 'דומינגו')) ($h -join ' | ')
Check 'אנגלית ומספרים לא נכנסים (המודל יודע עברית בלבד); ראשי תיבות עם גרשיים - כן' (-not ($h -match 'Hello|2024') -and ($h -contains 'רמב"ם')) ($h -join ' | ')
$h = Hot ('קַדָּאפִי, קדאפי! "ירושלים" (תל-אביב)')
Check 'בלי ניקוד, בלי כפילויות, ובלי סימני פיסוק שהיו נכנסים לתמלול' ((($h | Where-Object { $_ -eq 'קדאפי' }).Count -eq 1) -and ($h -contains 'ירושלים') -and ($h -contains 'אביב') -and -not (($h -join '') -match '[,!()\-]') -and -not (($h -join '') -match '[\u0591-\u05C7]')) ($h -join ' | ')
Check 'רקע ריק, או בלי אף מילה עברית - אין רשימה' ((Hot '').Count -eq 0 -and (Hot 'Lecture about AI, 2024').Count -eq 0 -and (Hot $null).Count -eq 0) ''
Mode 'ok'; WriteWords $scriptShort
$script:ctx = 'שיעור בגמרא, הרב כהן'
$res = RunFile $short 40000 $null 0 0
$script:ctx = $null
$calls = @(Calls)
Check 'עם רקע: המנוע מקבל את קובץ המילים (שם יחסי) ואת אוצר החלקים של המודל' ($calls[0].Split('|')[0] -match ' --decoding-method=modified_beam_search --modeling-unit=bpe --bpe-vocab=bpe\.vocab --hotwords-file=work\\hot[\d_]+\.txt --hotwords-score=2 work\\c[\d_]+\.wav$') ($calls[0].Split('|')[0])
Check 'בקובץ: מילה או צירוף בשורה, בעברית' ($calls[0] -match '\|hot=שיעור;בגמרא;שיעור בגמרא;הרב;כהן;הרב כהן;$') ($calls[0].Substring($calls[0].IndexOf('|hot=')))
Check 'וקובץ המילים נמחק בסוף, כמו קובץ הקול' (@(Get-ChildItem (Join-Path $model 'work') -ErrorAction SilentlyContinue).Count -eq 0 -and (CueText $res) -eq 'שלום לכולם. זה משפט שני. סוף.') ''

# קובץ ארוך: שלושה קטעים, ומילה כל חצי שנייה לכל האורך
$long = Join-Path $work 'long.wav'
& $ff -y -hide_banner -v error -f lavfi -i "sine=frequency=220:duration=800" -ac 1 -ar 8000 $long
$scriptLong = @(); for ($i = 0; $i -lt 1590; $i++) { $scriptLong += ,@((1 + $i * 0.5), ('מ' + $i)) }
WriteWords $scriptLong
Mode 'ok'
$sw = [Diagnostics.Stopwatch]::StartNew()
$res = RunFile $long 800000 $null 0 0
$calls = @(Calls)
Check 'קובץ של 13 דקות: שלושה קטעים של חמש דקות עם חפיפה' ($calls.Count -eq 3 -and [int]$res.Chunks -eq 3) ("" + $calls.Count)
$got = @(); foreach ($c in $res.Cues) { $got += @($c.Text -split ' ') }
$want = @($scriptLong | ForEach-Object { $_[1] })
$missing = @($want | Where-Object { $got -notcontains $_ }); $dups = @($got | Group-Object | Where-Object { $_.Count -gt 1 })
Check 'בתפרים: אף מילה לא חסרה, אף מילה לא כפולה, והסדר נשמר' (($got -join ' ') -eq ($want -join ' ')) ("יצאו " + $got.Count + " מתוך " + $want.Count + "; חסרות " + $missing.Count + ", כפולות " + $dups.Count + ' ' + (@($missing | Select-Object -First 4) -join ','))
Check 'הזבל של תחילת קטע לא נכנס' (-not (($got -join ' ') -match '~~')) ''
$bad = @($res.Cues | Where-Object { $_.End - $_.Start -gt 7100 -or $_.Text.Length -gt 84 })
$sorted = $true; for ($i = 1; $i -lt $res.Cues.Count; $i++) { if ($res.Cues[$i].Start -lt $res.Cues[$i - 1].Start) { $sorted = $false } }
Check 'בלי פיסוק בכלל: עדיין כתוביות של עד 7 שניות ו-84 תווים, לפי הסדר' ($bad.Count -eq 0 -and $sorted -and $res.Cues.Count -gt 100) ("" + $res.Cues.Count + ' cues, bad ' + $bad.Count)
$near = $res.Cues | Where-Object { $_.Text.StartsWith('מ600 ') -or $_.Text -match '(^| )מ600( |$)' } | Select-Object -First 1
Check 'זמנים בקטע השני: מילה מ-301 שניות יוצאת שם' ($near -ne $null -and $near.Start -le 301100 -and $near.End -ge 300900) ("" + $near.Start + '-' + $near.End)

# נפילות
Mode 'fail'
$res = RunFile $short 40000 $null 0 0
Check 'המנוע נופל: הודעה שאומרת מה אפשר לעשות, בלי כתוביות' ([string]$res.Error -match 'לא הצליח לרוץ במחשב הזה' -and $res.Cues.Count -eq 0 -and [int]$res.Failed -eq 1) ([string]$res.Error)
Mode 'fail'
$res = RunFile $long 800000 $null 0 0
Check 'שני קטעים נופלים ברצף - עוצרים, לא ממשיכים על כל הקובץ' (@(Calls).Count -eq 2 -and [long]$res.StoppedAtMs -eq 0 -and $res.Gaps.Count -eq 0) ("" + @(Calls).Count + ' ' + $res.StoppedAtMs)
Mode 'garbage'
$res = RunFile $short 40000 $null 0 0
Check 'המנוע מחזיר משהו אחר - שגיאה במילים' ([string]$res.Error -match 'לא החזיר תשובה') ([string]$res.Error)
Remove-Item (Join-Path $model 'sherpa-onnx.exe');
$res = RunFile $short 40000 $null 0 0
Check 'קובץ המנוע נעלם - ״עוד לא הורד״, לא חריגה' ([string]$res.Error -match 'עוד לא הורד') ([string]$res.Error)
Copy-Item "$work\fake.exe" (Join-Path $model 'sherpa-onnx.exe'); UseFake

# עצירה
Mode 'hang'
$t0 = [DateTime]::UtcNow
$cancel = [Func[bool]]{ return ([DateTime]::UtcNow - $t0).TotalSeconds -gt 1.5 }
$sw = [Diagnostics.Stopwatch]::StartNew()
$res = RunFile $short 40000 $cancel 0 0
$sw.Stop()
Start-Sleep -Milliseconds 300
$left = @(Get-CimInstance Win32_Process -Filter "Name='sherpa-onnx.exe'" | Where-Object { $_.ExecutablePath -like ($work + '*') })
Check 'עצירה באמצע: חוזר מיד, והמנוע נסגר' ([bool]$res.Canceled -and $sw.Elapsed.TotalSeconds -lt 6 -and $left.Count -eq 0 -and $res.Error -eq $null) ("" + [int]$sw.Elapsed.TotalSeconds + 's, left ' + $left.Count)
Mode 'hang'
$lsT.GetField('EngineTimeoutSec', $SF).SetValue($null, 2)
$sw = [Diagnostics.Stopwatch]::StartNew()
$res = RunFile $short 40000 $null 0 0
$sw.Stop()
$lsT.GetField('EngineTimeoutSec', $SF).SetValue($null, 0)
$left = @(Get-CimInstance Win32_Process -Filter "Name='sherpa-onnx.exe'" | Where-Object { $_.ExecutablePath -like ($work + '*') })
Check 'מנוע שנתקע: נסגר אחרי הזמן הקצוב, עם הודעה' ([string]$res.Error -match 'לא הגיב' -and $sw.Elapsed.TotalSeconds -lt 10 -and $left.Count -eq 0) ([string]$res.Error + ' ' + [int]$sw.Elapsed.TotalSeconds + 's')

# דרך המסלול הרגיל של התוכנה, והממשק הכללי
Mode 'ok'; WriteWords $scriptShort
$argv = New-Object object[] 8
$argv[0] = $loc; $argv[1] = [string]$short; $argv[2] = [long]40000; $argv[3] = [string]'רקע'; $argv[4] = $null; $argv[5] = $null; $argv[6] = [long]0; $argv[7] = [long]40000
$runM = (T 'Transcribe').GetMethods($SF) | Where-Object { $_.Name -eq 'Run' -and $_.GetParameters().Count -eq 8 }
$res = $runM.Invoke($null, $argv)
Check 'Transcribe.Run עם הספק המקומי הולך למסלול שלו' ((CueText $res) -eq 'שלום לכולם. זה משפט שני. סוף.' -and @(Calls).Count -eq 1 -and [string]$res.ProviderName -eq [string]$loc.Name) ((CueText $res))
$mp3 = Join-Path $work 'c.mp3'
& $ff -y -hide_banner -v error -i $short -t 12 -ac 1 -ar 16000 -c:a libmp3lame -b:a 32k $mp3
Mode 'ok'
$argv = New-Object object[] 4; $argv[0] = [byte[]][IO.File]::ReadAllBytes($mp3); $argv[1] = 'audio/mp3'; $argv[2] = ''; $argv[3] = $null
$lines = $lsT.GetMethod('TranscribeChunk', $IF).Invoke($loc, $argv)
Check 'קטע בודד (MP3, הממשק של כל הספקים): מומר, מתומלל, ומנוקה' ($lines -ne $null -and $lines.Count -eq 2 -and @(Calls)[0] -match '\|rate=16000\|ch=1\|' -and @(Get-ChildItem (Join-Path $model 'work')).Count -eq 0) ("" + $lines.Count + ' ' + $argv[3])

# ---------------- איזה שירות נבחר ----------------
Write-Host "`n--- which provider"
$aiT = T 'Ai'
$oldKey = $aiT.GetField('Key', $SF).GetValue($null); $oldGroq = $sttT.GetField('GroqKey', $SF).GetValue($null); $oldId = $sttT.GetField('ProviderId', $SF).GetValue($null)
function Cur { return [string]$sttT.GetProperty('Current', $SF).GetValue($null, $null).Id }
$aiT.GetField('Key', $SF).SetValue($null, ''); $sttT.GetField('GroqKey', $SF).SetValue($null, ''); $sttT.GetField('ProviderId', $SF).SetValue($null, '')
Check 'בלי אף מפתח, והתמלול על המחשב מותקן - הוא הנבחר' ((Cur) -eq 'local') (Cur)
$sttT.GetField('GroqKey', $SF).SetValue($null, 'gsk_test')
Check 'יש מפתח של Groq - הוא (מי שטרח להוציא מפתח רצה אותו)' ((Cur) -eq 'groq') (Cur)
$sttT.GetField('GroqKey', $SF).SetValue($null, ''); $aiT.GetField('Key', $SF).SetValue($null, 'AIza_test')
Check 'רק מפתח של גוגל - גוגל' ((Cur) -eq 'gemini') (Cur)
$sttT.GetField('ProviderId', $SF).SetValue($null, 'local')
Check 'בחירה מפורשת ״על המחשב״ נשמרת גם כשיש מפתחות' ((Cur) -eq 'local') (Cur)
$svc = (T 'MainForm').GetMethod('AiService', $SF)
function Svc([string]$s) { $r = $svc.Invoke($null, @([string]$s)); if ($r -eq $null) { return '' } else { return [string]$r.Id } }
Check 'העוזר: local / מקומי / על המחשב / offline - התמלול על המחשב; groq; google; ריק - לא נבחר' ((Svc 'local') -eq 'local' -and (Svc 'מקומי') -eq 'local' -and (Svc 'על המחשב') -eq 'local' -and (Svc 'Offline') -eq 'local' -and (Svc 'groq') -eq 'groq' -and (Svc 'google') -eq 'gemini' -and (Svc '') -eq '') ''

# ---------------- החלונות ----------------
Write-Host "`n--- windows"
$mi = (T 'Ff').GetMethod('ProbeFile', $SF).Invoke($null, @([string]$short))
function NewDlg($n, $argv) { return [Activator]::CreateInstance((T $n), $IF -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }
function Fld($o, $f) { return $o.GetType().GetField($f, $IF).GetValue($o) }
$selfVis = (T 'Dlg').GetMethod('SelfVisible', $SF)
$d = NewDlg 'TranscribeDlg' @($mi, [int]0)
Check 'חלון התמלול, מותקן: ״על המחשב״ בחור, בלי כפתור הורדה, ובמקום האזהרה - ״נשאר במחשב״' ([string]$d.Provider.Id -eq 'local' -and -not [bool]$selfVis.Invoke($null, @((Fld $d '_connect'))) -and [string](Fld $d '_warn').Text -match 'נשאר במחשב') ([string](Fld $d '_warn').Text)
[void]$d.GetType().GetMethod('Choose', $IF).Invoke($d, @([string]'gemini'))
Check 'מעבר לגוגל: האזהרה חוזרת, והכפתור של המפתח חוזר' ([string](Fld $d '_warn').Text -match 'יישלח' -and [bool]$selfVis.Invoke($null, @((Fld $d '_connect')))) ([string](Fld $d '_warn').Text)
$d.Dispose()
$lmT.GetField('FolderOverride', $SF).SetValue($null, [string](Join-Path $work 'nothing-here'))
$d = NewDlg 'TranscribeDlg' @($mi, [int]0)
Check 'לא מותקן: כפתור ״הורדה״ עם הגודל, במקום שבו השירותים ברשת מבקשים מפתח' ([bool]$selfVis.Invoke($null, @((Fld $d '_connect'))) -and [string](Fld $d '_connect').Text -match 'הורדה' -and [string](Fld $d '_connect').Text -match 'MB') ([string](Fld $d '_connect').Text)
$d.Dispose()
UseFake
$st = NewDlg 'StorageDlg' @()
$line = [string](Fld $st '_localLine').Text
Check 'אחסון: שורה לתמלול על המחשב, עם הגודל; והסכום בהגדרות כולל אותה' ($line -match 'התמלול על המחשב' -and $line -match '\d' -and [bool]$selfVis.Invoke($null, @((Fld $st '_delLocal'))) -and [long](T 'StorageDlg').GetMethod('Total', $SF).Invoke($null, @()) -ge [long]$lmT.GetProperty('SizeOnDisk', $SF).GetValue($null, $null)) $line
$st.Dispose()
$aiT.GetField('Key', $SF).SetValue($null, $oldKey); $sttT.GetField('GroqKey', $SF).SetValue($null, $oldGroq); $sttT.GetField('ProviderId', $SF).SetValue($null, $oldId)

# ---------------- הקטלוג והמחשב (0.8.46) ----------------
Write-Host "`n--- catalog"
$miT = T 'MachineInfo'; $catT = T 'ModelCatalog'
$realTotal = [long]$miT.GetProperty('TotalMB', $SF).GetValue($null, $null)
Check 'המחשב: זיכרון כולל וליבות נקראים מווינדוס' ($realTotal -gt 1000 -and [int]$miT.GetProperty('Cores', $SF).GetValue($null, $null) -ge 1) ("" + $realTotal + ' MB')
function Budget([long]$total, [long]$free) {
    $miT.GetField('FakeTotalMB', $SF).SetValue($null, [long]$total); $miT.GetField('FakeFreeMB', $SF).SetValue($null, [long]$free)
    return [long]$miT.GetProperty('BudgetMB', $SF).GetValue($null, $null)
}
Check 'תקציב: מחשב של 8GB עם 2GB פנויים - כ-4GB; של 16GB - 8GB; של 4GB - 2GB' ((Budget 8192 2048) -eq 4096 -and (Budget 16384 3000) -eq 8192 -and (Budget 4096 3500) -eq 2048) ''
$all = $catT.GetProperty('All', $SF).GetValue($null, $null)
$byId = $catT.GetMethod('ById', $SF)
$fast = $byId.Invoke($null, @([string]'stt-he-fast')); $tg = $byId.Invoke($null, @([string]'mt-translategemma-4b'))
[void](Budget 8192 2500)
Check 'במחשב של 8GB: התמלול הקל ״מתאים״, TranslateGemma ״כבד״ - עם מספרים' ([bool]$fast.Fits -and -not [bool]$tg.Fits -and [string]$catT.GetMethod('FitText', $SF).Invoke($null, @($tg)) -match 'כבד' -and [string]$catT.GetMethod('FitText', $SF).Invoke($null, @($tg)) -match 'GB') ([string]$catT.GetMethod('FitText', $SF).Invoke($null, @($tg)))
[void](Budget 32768 20000)
Check 'במחשב של 32GB - גם הכבד מתאים' ([bool]$tg.Fits) ''
$miT.GetField('FakeTotalMB', $SF).SetValue($null, [long]-1); $miT.GetField('FakeFreeMB', $SF).SetValue($null, [long]-1)
$ids = @($all | ForEach-Object { $_.Id }); $dup = @($ids | Group-Object | Where-Object { $_.Count -gt 1 })
$forT = $catT.GetMethod('For', $SF); $stt = @($forT.Invoke($null, @([Enum]::Parse((T 'ModelTask'), 'Transcribe'))))
Check 'הקטלוג: מזהים שונים, ולכל חבילה שם, שורה, גודל וזיכרון; זמין היום - רק מה שעלה' ($dup.Count -eq 0 -and @($all | Where-Object { -not $_.Title -or -not $_.Line -or $_.DiskMB -le 0 -or $_.RamMB -le 0 }).Count -eq 0 -and $stt.Count -eq 1 -and $stt[0].Id -eq 'stt-he-fast') (($ids -join ', '))
$nc = $byId.Invoke($null, @([string]'stt-he-caspi'))
Check 'רישיון עם מגבלה נאמר (Caspi - לא מסחרי)' ([string]$nc.LicenseNote -match 'לא מסחרי') ([string]$nc.LicenseNote)

# ---------------- מודל שפה דרך llama-server (LocalLlm, 0.8.46) - שרת מדומה ----------------
Write-Host "`n--- llama-server (fake)"
$llmSrc = @'
using System; using System.IO; using System.Net; using System.Net.Sockets; using System.Text; using System.Threading;
class P {
  static int Main(string[] a) {
    int port = 0; for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "--port") port = int.Parse(a[i + 1]);
    File.WriteAllText("fake-llm-args.txt", string.Join(" ", a) + "|cwd=" + Directory.GetCurrentDirectory(), new UTF8Encoding(false));
    string mode = File.Exists("fake-llm-mode.txt") ? File.ReadAllText("fake-llm-mode.txt").Trim() : "ok";
    if (mode == "crash") return 1;
    TcpListener l = new TcpListener(IPAddress.Loopback, port); l.Start();
    while (true) {
      TcpClient c = l.AcceptTcpClient(); NetworkStream ns = c.GetStream(); ns.ReadTimeout = 5000;
      MemoryStream ms = new MemoryStream(); byte[] buf = new byte[65536]; int headEnd = -1, clen = 0;
      while (true) {
        int n = ns.Read(buf, 0, buf.Length); if (n <= 0) break; ms.Write(buf, 0, n);
        string s = Encoding.ASCII.GetString(ms.ToArray());
        if (headEnd < 0) { headEnd = s.IndexOf("\r\n\r\n"); if (headEnd >= 0) { int k = s.ToLower().IndexOf("content-length:"); if (k >= 0 && k < headEnd) clen = int.Parse(s.Substring(k + 15, s.IndexOf("\r\n", k) - k - 15).Trim()); } }
        if (headEnd >= 0 && ms.Length >= headEnd + 4 + clen) break;
      }
      byte[] all = ms.ToArray(); string head = Encoding.ASCII.GetString(all, 0, Math.Max(0, headEnd));
      string body;
      if (head.StartsWith("GET /health")) body = "{\"status\":\"ok\"}";
      else {
        File.WriteAllBytes("fake-llm-req.json", new MemoryStream(all, headEnd + 4, all.Length - headEnd - 4).ToArray());
        body = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"<think>hmm</think>שלום\"}}]}";
      }
      byte[] b = Encoding.UTF8.GetBytes(body);
      byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + b.Length + "\r\nConnection: close\r\n\r\n");
      ns.Write(h, 0, h.Length); ns.Write(b, 0, b.Length); c.Close();
    }
  }
}
'@
$llDir = Join-Path $work 'מנוע מודל'; $llModels = Join-Path $work 'מודלים'
New-Item -ItemType Directory $llDir, $llModels | Out-Null
[IO.File]::WriteAllText((Join-Path $work 'fakellm.cs'), $llmSrc)
& $csc /nologo /target:exe /out:"$llDir\llama-server.exe" "$work\fakellm.cs" | Out-Null
$llT = T 'LocalLlm'
function NewLlm([string]$model, $mmproj) { return [Activator]::CreateInstance($llT, $IF -bor [Reflection.BindingFlags]::CreateInstance, $null, @([string](Join-Path $llDir 'llama-server.exe'), [string]$llModels, [string]$model, $mmproj, [int]4096), $null) }
$msgT = [Collections.Generic.Dictionary``2].MakeGenericType([string], [object])
function Ask($ll, $msg) {
    $msgs = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($msgT)); $msgs.Add($msg)
    $argv = New-Object object[] 5; $argv[0] = $msgs; $argv[1] = [int]50; $argv[2] = [double]0; $argv[3] = $null; $argv[4] = $null
    $out = $llT.GetMethod('Chat', $IF).Invoke($ll, $argv)
    return @{ out = $out; err = [string]$argv[4] }
}
function LlmProcs { return @(Get-CimInstance Win32_Process -Filter "Name='llama-server.exe'" | Where-Object { $_.ExecutablePath -like ($llDir + '*') }) }
$ll = NewLlm 'caspi\model.gguf' 'caspi\mmproj.gguf'
$a = [string]$llT.GetMethod('Arguments', $IF).Invoke($ll, @([int]5555))
$expA = '-m caspi' + [char]92 + 'model.gguf --mmproj caspi' + [char]92 + 'mmproj.gguf --host 127.0.0.1 --port 5555 -c 4096 -t '
Check 'llama-server: שמות יחסיים, רק 127.0.0.1, ועד 4 ליבות' ($a.StartsWith($expA) -and $a -match '-t [1-4] -tb [1-4] --no-webui$') $a
$r = Ask $ll ($llT.GetMethod('AudioMessage', $SF).Invoke($null, @([byte[]](1, 2, 3), [string]'תמלל')))
$req = [IO.File]::ReadAllText((Join-Path $llModels 'fake-llm-req.json'), [Text.Encoding]::UTF8)
$args0 = [IO.File]::ReadAllText((Join-Path $llModels 'fake-llm-args.txt'), [Text.Encoding]::UTF8)
Check 'llama-server: עולה מתוך תיקיית המודלים (בשם עברי), ותשובה בלי ה״חשיבה״' ($r.out -eq 'שלום' -and $args0.EndsWith('|cwd=' + $llModels)) ([string]$r.out + ' ' + $r.err)
Check 'llama-server: קול נשלח כ-input_audio, ב-base64, עם ההוראה' ($req -match '"type":"input_audio"' -and $req -match '"data":"AQID"' -and $req -match '"format":"wav"') ''
$ll.Stop(); Start-Sleep -Milliseconds 400
Check 'llama-server: Stop סוגר את התהליך' (@(LlmProcs).Count -eq 0) ''
$llT.GetField('IdleSec', $SF).SetValue($null, 1)
$ll = NewLlm 'caspi\model.gguf' $null
$r = Ask $ll ($llT.GetMethod('TextMessage', $SF).Invoke($null, @([string]'user', [string]'שלום')))
$t0 = [DateTime]::UtcNow; while (@(LlmProcs).Count -gt 0 -and ([DateTime]::UtcNow - $t0).TotalSeconds -lt 15) { Start-Sleep -Milliseconds 300 }
Check 'llama-server: בלי בקשות - נסגר לבד (לא נשאר עם כמה GB בזיכרון)' ($r.out -eq 'שלום' -and @(LlmProcs).Count -eq 0) ("" + [int]([DateTime]::UtcNow - $t0).TotalSeconds + 's out=[' + $r.out + '] err=[' + $r.err + '] procs=' + @(LlmProcs).Count)
$llT.GetField('IdleSec', $SF).SetValue($null, 120)
$ll = NewLlm 'מודל\model.gguf' $null
$r = Ask $ll ($llT.GetMethod('TextMessage', $SF).Invoke($null, @([string]'user', [string]'x')))
Check 'llama-server: שם קובץ בעברית - נעצר עם הודעה, בלי להפעיל' ($r.out -eq $null -and $r.err -match 'תיקייה' -and @(LlmProcs).Count -eq 0) ($r.err + ' out=[' + $r.out + '] procs=' + @(LlmProcs).Count)
[IO.File]::WriteAllText((Join-Path $llModels 'fake-llm-mode.txt'), 'crash')
$ll = NewLlm 'caspi\model.gguf' $null
$r = Ask $ll ($llT.GetMethod('TextMessage', $SF).Invoke($null, @([string]'user', [string]'x')))
Check 'llama-server: נסגר בזמן הטעינה - ״אולי אין מספיק זיכרון״ והצעה למודל קל' ($r.out -eq $null -and $r.err -match 'זיכרון' -and $r.err -match 'קל יותר') $r.err
Remove-Item (Join-Path $llModels 'fake-llm-mode.txt')

# ---------------- תרגום בחלונות ופיזור חזרה (WindowTranslate, 0.8.46) ----------------
Write-Host "`n--- window translate"
$wtT = T 'WindowTranslate'
function SplitT([string]$text, [int[]]$w) { return ,[string[]]$wtT.GetMethod('Split', $SF).Invoke($null, (Pack $text $w)) }
$sp = SplitT 'אחת שתיים שלוש ארבע חמש שש' @(10, 10)
Check 'פיזור: לפי אורך המקור, חצי-חצי' (($sp -join '|') -eq 'אחת שתיים שלוש|ארבע חמש שש') ($sp -join '|')
$sp = SplitT 'כמעט סיימתי. חדש במקום, רק צריך סרט בידוד' @(12, 10)
Check 'פיזור: חותך מיד אחרי נקודה כשהיא קרובה לאמצע' ($sp[0] -eq 'כמעט סיימתי.' -or $sp[0] -eq 'כמעט סיימתי. חדש במקום,') ($sp -join '|')
$sp = SplitT 'אחת שתיים' @(5, 5, 5)
Check 'פיזור: פחות מילים מכתוביות - אף מילה לא אובדת' ((($sp | Where-Object { $_ }) -join ' ') -eq 'אחת שתיים' -and $sp.Count -eq 3) ($sp -join '|')
$sp = SplitT 'a b c d e f g h' @(1, 1, 1, 1)
Check 'פיזור: כשיש מספיק מילים - לכל כתובית לפחות מילה' (@($sp | Where-Object { -not $_ }).Count -eq 0 -and ($sp -join ' ') -eq 'a b c d e f g h') ($sp -join '|')
$wins = $wtT.GetMethod('Windows', $SF).Invoke($null, (Pack ([int]7) ([double[]]@(0, 0.3, 0.2, 0.4, 5.0, 0.1, 0.2))))
$ws = @($wins | ForEach-Object { ($_ -join ',') }) -join ' / '
Check 'חלונות: עד 3 כתוביות, ולא מעבר להפסקה של יותר משתי שניות' ($ws -eq '0,1,2 / 3 / 4,5,6') $ws
$lines = [Collections.Generic.List[string]]@('i guess it was bound to happen', 'i have my first ever failed', 'laptop review', '')
$seen = New-Object Collections.ArrayList
$tr = [Func[Collections.Generic.List[string], Collections.Generic.List[string]]]{ param($xs) [void]$seen.AddRange($xs); $o = New-Object Collections.Generic.List[string]; foreach ($x in $xs) { $o.Add('תרגום של ' + $x) }; return ,$o }
$pu = [Func[string, string]]{ param($x) return ($x.Substring(0, 1).ToUpper() + $x.Substring(1) + '.') }
$res = $wtT.GetMethod('Translate', $SF).Invoke($null, (Pack $lines ([double[]]@(0, 0.1, 0.1, 0.1)) $tr $pu))
Check 'תרגום בחלונות: המתרגם מקבל משפט מחובר עם פיסוק, והתוצאה מתפזרת לכל הכתוביות - שורה ריקה נשארת ריקה' ($seen.Count -eq 2 -and $seen[0] -eq 'I guess it was bound to happen i have my first ever failed laptop review.' -and $res.Count -eq 4 -and $res[3] -eq '' -and $res[0].StartsWith('תרגום')) (($seen -join ' | ') + ' => ' + ($res -join ' | '))
$hp = $wtT.GetMethod('HasPunctuation', $SF)
Check 'פיסוק קיים (גוגל, Groq) - לא מפעילים את מודל הפיסוק; טקסט גולמי של Whisper - כן' ([bool]$hp.Invoke($null, @([string]'Hello, world. How are you?')) -and -not [bool]$hp.Invoke($null, @([string]'i guess it was bound to happen i have my first ever failed laptop review failed and that there is'))) ''

# ---------------- ההורדה ----------------
Write-Host "`n--- download"
$dlServer = {
    param($port, $files, $faults, $logDir, $maxReq)
    $l = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $port
    $l.Start()
    try {
        for ($k = 0; $k -lt $maxReq; $k++) {
            $t0 = [DateTime]::UtcNow
            while (-not $l.Pending()) { if (([DateTime]::UtcNow - $t0).TotalSeconds -gt 12) { return }; Start-Sleep -Milliseconds 15 }
            $c = $l.AcceptTcpClient(); $ns = $c.GetStream(); $ns.ReadTimeout = 8000
            $ms = New-Object IO.MemoryStream; $buf = New-Object byte[] 8192; $head = ''
            try { while ($true) { $n = $ns.Read($buf, 0, $buf.Length); if ($n -le 0) { break }; $ms.Write($buf, 0, $n); $head = [Text.Encoding]::ASCII.GetString($ms.ToArray()); if ($head.Contains("`r`n`r`n")) { break } } } catch { }
            [IO.File]::WriteAllText((Join-Path $logDir ("req-$k.txt")), $head)
            $name = ''; $m = [regex]::Match($head, '^GET\s+\S*/([^/\s]+)\s'); if ($m.Success) { $name = $m.Groups[1].Value }
            $from = [int64]0; $m = [regex]::Match($head, '(?im)^range:\s*bytes=(\d+)-'); if ($m.Success) { $from = [int64]$m.Groups[1].Value }
            $fault = if ($k -lt $faults.Count) { [string]$faults[$k] } else { '' }
            $status = '200 OK'; $type = 'application/octet-stream'; $send = -1
            if ($fault -eq '404' -or -not $files.ContainsKey($name)) { $status = '404 Not Found'; $body = [Text.Encoding]::ASCII.GetBytes('nope') }
            elseif ($fault -eq 'html') { $type = 'text/html'; $body = [Text.Encoding]::UTF8.GetBytes('<html><body>blocked by the filter</body></html>') }
            elseif ($fault -eq '418') { $status = '418 Blocked'; $body = [Text.Encoding]::ASCII.GetBytes('x') }
            else {
                $all = [byte[]]$files[$name]
                if ($from -gt 0 -and $from -lt $all.Length) { $status = '206 Partial Content'; $body = New-Object byte[] ($all.Length - $from); [Array]::Copy($all, $from, $body, 0, $body.Length) }
                elseif ($from -gt 0) { $status = '416 Range Not Satisfiable'; $body = New-Object byte[] 0 }
                else { $body = $all }
                if ($fault -like 'cut:*') { $send = [int]$fault.Substring(4) }
            }
            $hb = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Type: $type`r`nContent-Length: " + $body.Length + "`r`nConnection: close`r`n`r`n")
            try {
                $ns.Write($hb, 0, $hb.Length)
                if ($send -ge 0) { $ns.Write($body, 0, [Math]::Min($send, $body.Length)) }
                else { $off = 0; while ($off -lt $body.Length) { $n = [Math]::Min(16384, $body.Length - $off); $ns.Write($body, $off, $n); $off += $n } }
                $ns.Flush()
            } catch { }
            $c.Close()
        }
    } finally { $l.Stop() }
}
function FreePort { $t = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), 0; $t.Start(); $p = $t.LocalEndpoint.Port; $t.Stop(); return $p }
function Gz([byte[]]$raw) { $ms = New-Object IO.MemoryStream; $z = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionMode]::Compress); $z.Write($raw, 0, $raw.Length); $z.Dispose(); return ,$ms.ToArray() }
function Sha([byte[]]$b) { $s = [Security.Cryptography.SHA256]::Create(); return (-join ($s.ComputeHash($b) | ForEach-Object { $_.ToString('X2') })) }
# שלושה קבצים קטנים במקום שבעת האמיתיים; האמצעי אקראי (לא נדחס) וגדול מספיק לכמה קריאות
$rnd = New-Object Random 7
$rawA = [Text.Encoding]::ASCII.GetBytes('engine ' * 400); $rawB = New-Object byte[] 400000; $rnd.NextBytes($rawB); $rawC = [Text.Encoding]::ASCII.GetBytes('tokens ' * 300)
$gzA = Gz $rawA; $gzB = Gz $rawB; $gzC = Gz $rawC
$served = @{ 'a.bin.gz' = $gzA; 'b.bin.gz' = $gzB; 'c.txt.gz' = $gzC }
$lmT.GetField('Files', $SF).SetValue($null, [string[]]@('a.bin', 'b.bin', 'c.txt'))
$lmT.GetField('Sizes', $SF).SetValue($null, [long[]]@($rawA.Length, $rawB.Length, $rawC.Length))
$lmT.GetField('GzSizes', $SF).SetValue($null, [long[]]@($gzA.Length, $gzB.Length, $gzC.Length))
$lmT.GetField('Sha256', $SF).SetValue($null, [string[]]@((Sha $rawA), (Sha $rawB), (Sha $rawC)))
$totalDl = $gzA.Length + $gzB.Length + $gzC.Length
$script:srv = $null
function StartDl($faults, $files) {
    if ($files -eq $null) { $files = $served }
    $port = FreePort
    $dir = Join-Path $work ('dl-' + $port); New-Item -ItemType Directory $dir | Out-Null
    $ps = [powershell]::Create()
    [void]$ps.AddScript($dlServer).AddArgument($port).AddArgument($files).AddArgument(@($faults)).AddArgument($dir).AddArgument(12)
    $h = $ps.BeginInvoke()
    Start-Sleep -Milliseconds 250
    $folder = Join-Path $dir 'model'
    $lmT.GetField('FolderOverride', $SF).SetValue($null, [string]$folder)
    $lmT.GetField('BaseUrl', $SF).SetValue($null, "http://127.0.0.1:$port/pack/")
    $script:srv = @{ ps = $ps; h = $h; dir = $dir; folder = $folder }
    return $script:srv
}
function StopDl { try { $script:srv.ps.Stop() } catch { }; try { $script:srv.ps.Dispose() } catch { } }
function Reqs { return @(Get-ChildItem $script:srv.dir -Filter 'req-*.txt' | Sort-Object { [int]($_.BaseName -replace '\D', '') } | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) }
$script:prog = @(); $script:cancelAt = -1
function Download {
    $script:prog = @()
    $progress = [Action[long, long]]{ param($d, $t) $script:prog += ,@($d, $t) }
    $cancel = [Func[bool]]{ return ($script:cancelAt -ge 0 -and $script:prog.Count -ge $script:cancelAt) }
    $argv = New-Object object[] 3; $argv[0] = $progress; $argv[1] = $cancel; $argv[2] = $null
    $ok = $lmT.GetMethod('Download', $SF).Invoke($null, $argv)
    return @{ ok = [bool]$ok; err = [string]$argv[2] }
}
function Same($path, [byte[]]$raw) { return (Test-Path $path) -and ((Sha ([IO.File]::ReadAllBytes($path))) -eq (Sha $raw)) }
function Installed { return [bool]$lmT.GetProperty('Installed', $SF).GetValue($null, $null) }

$s = StartDl @()
$r = Download
Check 'הורדה: שלושת הקבצים במקום, זהים למקור, ו״מותקן״' ($r.ok -and (Same (Join-Path $s.folder 'a.bin') $rawA) -and (Same (Join-Path $s.folder 'b.bin') $rawB) -and (Same (Join-Path $s.folder 'c.txt') $rawC) -and (Installed)) $r.err
$last = $script:prog[$script:prog.Count - 1]
Check 'ההתקדמות מגיעה בדיוק לסך כל ההורדה' ($last[0] -eq $totalDl -and $last[1] -eq $totalDl) ("" + $last[0] + ' / ' + $totalDl)
Check 'בקשה אחת לכל קובץ, ובלי קבצים זמניים אחריה' (@(Reqs).Count -eq 3 -and @(Get-ChildItem $s.folder | Where-Object { $_.Name -match '\.part$|\.download$' }).Count -eq 0) ("" + @(Reqs).Count)
Remove-Item (Join-Path $s.folder 'c.txt')
$r = Download
Check 'קובץ אחד חסר: רק הוא יורד שוב' ($r.ok -and @(Reqs).Count -eq 4 -and @(Reqs)[3] -match 'c\.txt\.gz') ("" + @(Reqs).Count)
StopDl

# החיבור נסגר באמצע הקובץ הגדול
$lmT.GetField('Retries', $SF).SetValue($null, 4)
$s = StartDl @('', 'cut:150000')
$r = Download
$reqs = @(Reqs)
$resumed = @($reqs | Where-Object { $_ -match '(?im)^range:\s*bytes=(\d+)-' })
Check 'חיבור שנסגר באמצע: ממשיך לבד מאותו מקום (Range), והקובץ שלם' ($r.ok -and $resumed.Count -ge 1 -and (Same (Join-Path $s.folder 'b.bin') $rawB) -and (Installed)) ($r.err + ' reqs ' + $reqs.Count + ' resumed ' + $resumed.Count)
$m = [regex]::Match([string]$resumed[0], '(?im)^range:\s*bytes=(\d+)-')
Check 'ההמשך מתחיל ממה שכבר ירד, לא מאפס' ($m.Success -and [int]$m.Groups[1].Value -gt 0 -and [int]$m.Groups[1].Value -le 150000) $m.Groups[1].Value
StopDl

# ביטול באמצע, והמשך בפעם הבאה
$s = StartDl @()
$script:cancelAt = 4
$r = Download
$script:cancelAt = -1
$part = Join-Path $s.folder 'b.bin.gz.part'
$partLen = if (Test-Path $part) { (Get-Item $part).Length } else { -1 }
Check 'ביטול באמצע: ״בוטלה״, לא ״מותקן״, ומה שירד נשאר' (-not $r.ok -and $r.err -match 'בוטלה' -and -not (Installed) -and $partLen -gt 0 -and $partLen -lt $gzB.Length) ($r.err + ' part ' + $partLen)
$before = @(Reqs).Count
$r = Download
$after = @(Reqs)
Check 'הפעם הבאה ממשיכה מאותו מקום, ולא מורידה שוב את מה שכבר יש' ($r.ok -and (Installed) -and ($after[$before] -match ('(?im)^range:\s*bytes=' + $partLen + '-')) -and (Same (Join-Path $s.folder 'b.bin') $rawB)) ($r.err)
StopDl

# הסינון
$s = StartDl @('html')
$r = Download
Check 'דף חסימה של סינון (200 עם HTML): אומרים שזה הסינון, ושום דבר לא נשאר' (-not $r.ok -and $r.err -match 'סינון' -and -not (Installed) -and @(Get-ChildItem $s.folder -ErrorAction SilentlyContinue).Count -eq 0) $r.err
StopDl
$s = StartDl @('418')
$r = Download
Check 'חסימה בקוד 418 - אותה הודעה' (-not $r.ok -and $r.err -match 'סינון') $r.err
StopDl
$s = StartDl @('404')
$r = Download
Check 'הקובץ לא בשרת (404): ״לעדכן את התוכנה״' (-not $r.ok -and $r.err -match 'לא נמצא בשרת') $r.err
StopDl

# קובץ שהוחלף, וקובץ שבור
$other = New-Object byte[] 400000; $rnd.NextBytes($other)
$s = StartDl @() @{ 'a.bin.gz' = $gzA; 'b.bin.gz' = (Gz $other); 'c.txt.gz' = $gzC }
$lmT.GetField('GzSizes', $SF).SetValue($null, [long[]]@($gzA.Length, (Gz $other).Length, $gzC.Length))
$r = Download
Check 'קובץ עם תוכן אחר (הוחלף בשרת): נדחה לפי הטביעה, ולא נשאר בתיקייה' (-not $r.ok -and $r.err -match 'הוחלף' -and -not (Test-Path (Join-Path $s.folder 'b.bin')) -and @(Get-ChildItem $s.folder | Where-Object { $_.Name -match 'b\.bin' }).Count -eq 0) $r.err
StopDl
$junk = New-Object byte[] 5000; $rnd.NextBytes($junk); $junk[0] = 0x1F; $junk[1] = 0x8B; $junk[2] = 8
$s = StartDl @() @{ 'a.bin.gz' = $junk; 'b.bin.gz' = $gzB; 'c.txt.gz' = $gzC }
$lmT.GetField('GzSizes', $SF).SetValue($null, [long[]]@($junk.Length, $gzB.Length, $gzC.Length))
$r = Download
Check 'קובץ דחוס שבור: ״פגום״, בלי חריגה' (-not $r.ok -and $r.err -match 'פגום' -and -not (Installed)) $r.err
StopDl

# מחיקה
$lmT.GetField('GzSizes', $SF).SetValue($null, [long[]]@($gzA.Length, $gzB.Length, $gzC.Length))
$s = StartDl @()
$r = Download
$size = [long]$lmT.GetProperty('SizeOnDisk', $SF).GetValue($null, $null)
$argv = New-Object object[] 1; $argv[0] = $null
$okDel = $lmT.GetMethod('Remove', $SF).Invoke($null, $argv)
Check 'מחיקה: התיקייה נעלמת, ״לא מותקן״, ואפס בדיסק' ($r.ok -and $size -ge ($rawA.Length + $rawB.Length + $rawC.Length) -and [bool]$okDel -and -not (Test-Path $s.folder) -and -not (Installed) -and [long]$lmT.GetProperty('SizeOnDisk', $SF).GetValue($null, $null) -eq 0) ("" + $size)
StopDl

# ---- קובץ גדול בחלקים (מעל 2GB - מגבלה של גיטהאב; כאן החלק 150KB) ----
$lmT.GetField('SplitAt', $SF).SetValue($null, [long]150000)
$big = New-Object byte[] 400000; $rnd.NextBytes($big)
function Slice([byte[]]$b, [int]$from, [int]$len) { $o = New-Object byte[] $len; [Array]::Copy($b, $from, $o, 0, $len); return ,$o }
$bigFiles = @{ 'big.gguf.part1' = (Slice $big 0 150000); 'big.gguf.part2' = (Slice $big 150000 150000); 'big.gguf.part3' = (Slice $big 300000 100000) }
$lmT.GetField('Files', $SF).SetValue($null, [string[]]@('big.gguf'))
$lmT.GetField('Sizes', $SF).SetValue($null, [long[]]@($big.Length))
$lmT.GetField('GzSizes', $SF).SetValue($null, [long[]]@($big.Length))
$lmT.GetField('Sha256', $SF).SetValue($null, [string[]]@((Sha $big)))
$s = StartDl @() $bigFiles
$r = Download
$reqs = @(Reqs)
Check 'קובץ בחלקים: שלושה חלקים יורדים, מתחברים, והקובץ זהה למקור' ($r.ok -and (Same (Join-Path $s.folder 'big.gguf') $big) -and $reqs.Count -eq 3 -and $reqs[2] -match 'big\.gguf\.part3' -and @(Get-ChildItem $s.folder | Where-Object { $_.Name -match '\.part\d|\.download$' }).Count -eq 0) ($r.err + ' reqs ' + $reqs.Count)
StopDl
$s = StartDl @('', 'cut:60000') $bigFiles
$r = Download
$reqs = @(Reqs)
Check 'קובץ בחלקים: חלק שנקטע ממשיך מאותו מקום (Range), השאר לא יורדים פעמיים' ($r.ok -and (Same (Join-Path $s.folder 'big.gguf') $big) -and @($reqs | Where-Object { $_ -match '(?im)^range:\s*bytes=60000-' }).Count -eq 1) ($r.err + ' reqs ' + $reqs.Count)
StopDl
$s = StartDl @('html') $bigFiles
$r = Download
Check 'קובץ בחלקים: דף חסימה במקום חלק - ״סינון״, ושום חלק לא נשאר' (-not $r.ok -and $r.err -match 'סינון' -and @(Get-ChildItem $s.folder -ErrorAction SilentlyContinue).Count -eq 0) $r.err
StopDl
$wrong = @{ 'big.gguf.part1' = (Slice $big 0 150000); 'big.gguf.part2' = (Slice $big 0 150000); 'big.gguf.part3' = (Slice $big 300000 100000) }
$s = StartDl @() $wrong
$r = Download
Check 'קובץ בחלקים: חלק עם תוכן אחר - הטביעה של הקובץ השלם תופסת, והחלקים נמחקים' (-not $r.ok -and $r.err -match 'הוחלף' -and @(Get-ChildItem $s.folder | Where-Object { $_.Name -match 'big' }).Count -eq 0) $r.err
StopDl
$lmT.GetField('SplitAt', $SF).SetValue($null, [long](1900L * 1024 * 1024))
$lmT.GetField('Files', $SF).SetValue($null, [string[]]@('a.bin', 'b.bin', 'c.txt'))
$lmT.GetField('Sizes', $SF).SetValue($null, [long[]]@($rawA.Length, $rawB.Length, $rawC.Length))
$lmT.GetField('GzSizes', $SF).SetValue($null, [long[]]@($gzA.Length, $gzB.Length, $gzC.Length))
$lmT.GetField('Sha256', $SF).SetValue($null, [string[]]@((Sha $rawA), (Sha $rawB), (Sha $rawC)))

# ---- הקטלוג: שתי חבילות שחולקות מנוע אחד (0.8.46) ----
$mpT = T 'ModelPack'
function FakePack([string]$id, [string]$req, [string]$file, [byte[]]$rawF, [byte[]]$gzF) {
    $x = [Activator]::CreateInstance($mpT, $true)
    $x.Id = $id; $x.Requires = $req; $x.Title = $id; $x.Line = $id; $x.Tag = 't'; $x.Available = $true
    $x.Files = [string[]]@($file); $x.Sizes = [long[]]@($rawF.Length); $x.GzSizes = [long[]]@($gzF.Length); $x.Sha256 = [string[]]@((Sha $rawF))
    $catT.GetProperty('All', $SF).GetValue($null, $null).Add($x)
    return $x
}
$eng = FakePack 'test-engine' '' 'a.bin' $rawA $gzA
$pa = FakePack 'test-a' 'test-engine' 'b.bin' $rawB $gzB
$pb = FakePack 'test-b' 'test-engine' 'c.txt' $rawC $gzC
$s = StartDl @()
$catT.GetField('RootOverride', $SF).SetValue($null, [string](Join-Path $s.dir 'models'))
$catT.GetField('UrlOverride', $SF).SetValue($null, [string]$lmT.GetField('BaseUrl', $SF).GetValue($null))
function CatDl($pk) {
    $script:prog = @()
    $progress = [Action[long, long]]{ param($d, $t) $script:prog += ,@($d, $t) }
    $argv = New-Object object[] 4; $argv[0] = $pk; $argv[1] = $progress; $argv[2] = $null; $argv[3] = $null
    $ok = $catT.GetMethod('Download', $SF).Invoke($null, $argv)
    return @{ ok = [bool]$ok; err = [string]$argv[3] }
}
function CatInst($pk) { return [bool]$catT.GetMethod('Installed', $SF).Invoke($null, @($pk)) }
function CatRemove($pk) { $argv = New-Object object[] 2; $argv[0] = $pk; $argv[1] = $null; $ok = $catT.GetMethod('Remove', $SF).Invoke($null, $argv); return @{ ok = [bool]$ok; err = [string]$argv[1] } }
$need = [long]$catT.GetMethod('DownloadBytes', $SF).Invoke($null, @($pa))
Check 'קטלוג: ההורדה הראשונה כוללת גם את המנוע' ($need -eq ($gzA.Length + $gzB.Length) -and -not (CatInst $pa)) ("" + $need)
$r = CatDl $pa
$last = $script:prog[$script:prog.Count - 1]
Check 'קטלוג: מודל ראשון - המנוע והמודל יורדים, כל אחד לתיקייה שלו, וההתקדמות אחת עד הסוף' ($r.ok -and (CatInst $pa) -and (CatInst $eng) -and (Same (Join-Path $s.dir 'models\test-engine\a.bin') $rawA) -and (Same (Join-Path $s.dir 'models\test-a\b.bin') $rawB) -and $last[0] -eq $need -and $last[1] -eq $need) ($r.err + ' ' + $last[0] + '/' + $need)
$before = @(Reqs).Count
$r = CatDl $pb
$after = @(Reqs)
Check 'קטלוג: מודל שני על אותו מנוע - המנוע לא יורד שוב' ($r.ok -and (CatInst $pb) -and ($after.Count - $before) -eq 1 -and $after[$after.Count - 1] -match 'c\.txt\.gz') ("" + ($after.Count - $before))
$r = CatRemove $eng
Check 'קטלוג: מנוע שמודל מותקן צריך - לא נמחק, ונאמר למה' (-not $r.ok -and $r.err -match 'test-a|test-b' -and (CatInst $eng)) $r.err
$r1 = CatRemove $pa; $r2 = CatRemove $pb; $r3 = CatRemove $eng
Check 'קטלוג: אחרי המודלים - גם המנוע נמחק, וכלום לא ״מותקן״' ($r1.ok -and $r2.ok -and $r3.ok -and -not (CatInst $pa) -and -not (Test-Path (Join-Path $s.dir 'models\test-engine'))) ''
StopDl
$allL = $catT.GetProperty('All', $SF).GetValue($null, $null); [void]$allL.Remove($eng); [void]$allL.Remove($pa); [void]$allL.Remove($pb)
$catT.GetField('RootOverride', $SF).SetValue($null, $null); $catT.GetField('UrlOverride', $SF).SetValue($null, $null)

$lmT.GetField('Files', $SF).SetValue($null, $realFiles); $lmT.GetField('Sizes', $SF).SetValue($null, $realSizes)
$lmT.GetField('GzSizes', $SF).SetValue($null, $realGz); $lmT.GetField('Sha256', $SF).SetValue($null, $realSha)
$lmT.GetField('BaseUrl', $SF).SetValue($null, $realUrl); $lmT.GetField('FolderOverride', $SF).SetValue($null, $null)
try { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue } catch { }
Write-Host "`n$pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
