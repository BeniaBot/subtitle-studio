# הלולאה המלאה של הצ׳אט: שולח, מריץ את הפעולה, מחזיר תוצאה, וממשיך -
# בדיוק כמו AfterReply. כאן מתגלה אם הסבב השני נשבר.
param([string]$Key = $env:GEMKEY, [string]$Model = "", [int]$Rounds = 6,
      [string[]]$Prompts = @(
        "תקרא את הכתוביות",
        "מה כתוב בכתובית השנייה?",
        "תזיז הכל שנייה קדימה",
        "איך השיר?",
        "תציג"))
$ErrorActionPreference = 'Continue'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes('D:\Claude\subtitle-studio\dist\SubtitleStudio.exe'))
$NP=[Reflection.BindingFlags]::NonPublic; $PB=[Reflection.BindingFlags]::Public
$ST=[Reflection.BindingFlags]::Static; $IN=[Reflection.BindingFlags]::Instance
$CI=[Reflection.BindingFlags]::CreateInstance
function T($n){ $asm.GetType("SubtitleStudio.$n") }
function Pack { $a=New-Object object[] $args.Count; for($i=0;$i -lt $args.Count;$i++){$a[$i]=$args[$i]}; return ,$a }
function New1($t,$argv){ [Activator]::CreateInstance($t, ($NP -bor $PB -bor $IN -bor $CI), $null, $argv, $null) }

$ai = T 'Ai'
$ai.GetField('Key', $NP -bor $PB -bor $ST).SetValue($null, $Key)
if ($Model) { $ai.GetField('Model', $NP -bor $PB -bor $ST).SetValue($null, $Model) }
$using = $ai.GetField('Model', $NP -bor $PB -bor $ST).GetValue($null)

$mfT = T 'MainForm'
$mf  = New1 $mfT $null
$tools = $mfT.GetMethod('AiTools').Invoke($mf, @())
$chatT = T 'AiChatForm'
$chat = New1 $chatT (Pack $mf)
$sysM = $chatT.GetMethod('SystemPrompt', $NP -bor $IN)

# מסמך עם כמה כתוביות, כדי שיהיה מה לקרוא
$docF = $mfT.GetField('_doc', $NP -bor $IN)
if ($docF) {
    $doc = $docF.GetValue($mf)
    if ($doc) {
        $cueT = T 'Cue'
        $ctor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))
        $cues = $doc.GetType().GetField('Cues', $NP -bor $IN -bor $PB).GetValue($doc)
        $texts = @('שלום לכולם', 'ברוכים הבאים לשיעור', 'נתחיל מהתחלה')
        for ($i=0; $i -lt 3; $i++) {
            [void]$cues.Add($ctor.Invoke(@([long](1000*($i*3+1)), [long](1000*($i*3+3)), [string]$texts[$i])))
        }
    }
}

$msgT = T 'AiMsg'; $rt = T 'AiReply'; $callT = T 'AiCall'
$listT = [System.Collections.Generic.List``1].MakeGenericType($msgT)
$hist = [Activator]::CreateInstance($listT)
$send = $ai.GetMethod('Send', $NP -bor $PB -bor $ST)
$run  = $mfT.GetMethod('RunAiAction')

$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }

Write-Host ("דגם: $using   פעולות: " + $tools.Count)
foreach ($p in $Prompts) {
    Write-Host ''
    Write-Host ("=== " + $p) -ForegroundColor Cyan
    $m = New1 $msgT $null
    $msgT.GetField('Text').SetValue($m, $p)
    [void]$listT.GetMethod('Add').Invoke($hist, (Pack $m))

    $calls = @(); $lastText = ''; $err = $null
    for ($r = 0; $r -lt $Rounds; $r++) {
        $sys = $sysM.Invoke($chat, @())
        $reply = $send.Invoke($null, (Pack $sys $hist $tools $false))
        $err = $rt.GetField('Error').GetValue($reply)
        $fin = $rt.GetField('Finish').GetValue($reply)
        $txt = $rt.GetField('Text').GetValue($reply)
        $call = $rt.GetField('Call').GetValue($reply)
        if ($err) { Write-Host ("  סבב $r שגיאה: $err") -ForegroundColor Red; break }
        if ($txt) {
            $lastText = $txt
            $mm = New1 $msgT $null
            $msgT.GetField('Role').SetValue($mm, 'model')
            $msgT.GetField('Text').SetValue($mm, $txt)
            [void]$listT.GetMethod('Add').Invoke($hist, (Pack $mm))
        }
        if (-not $call) { Write-Host ("  סבב $r  finish=$fin  טקסט: " + $txt); break }
        $name = $callT.GetField('Name').GetValue($call)
        $calls += $name
        $ref = New-Object object[] 2
        $ref[0] = $call; $ref[1] = $false
        $res = $run.Invoke($mf, $ref)
        $shown = ''
        if ($res) { foreach ($k in $res.Keys) { $shown += "$k=" + (('' + $res[$k]) -replace "`n",' ') + '; ' } }
        Write-Host ("  סבב $r  קרא ל-$name  ->  " + $shown.Substring(0, [Math]::Min(160, $shown.Length)))
        $mm = New1 $msgT $null
        $msgT.GetField('Role').SetValue($mm, 'model')
        $msgT.GetField('Call').SetValue($mm, $call)
        $msgT.GetField('Hidden').SetValue($mm, $true)
        [void]$listT.GetMethod('Add').Invoke($hist, (Pack $mm))
        $tm = New1 $msgT $null
        $msgT.GetField('Role').SetValue($tm, 'tool')
        $msgT.GetField('ToolName').SetValue($tm, $name)
        $msgT.GetField('ToolResult').SetValue($tm, $res)
        [void]$listT.GetMethod('Add').Invoke($hist, (Pack $tm))
    }
    Check ("״" + $p + "״") (($calls.Count -gt 0) -and (-not $err)) ("פעולות: " + ($calls -join ' > '))
    if ($lastText) { Write-Host ("        תשובה: " + $lastText) -ForegroundColor DarkGray }
}
Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
