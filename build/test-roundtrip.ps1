# הלוך ושוב: כותבים כתובית, קוראים אותה חזרה, ומשווים.
#
# עד הסבב הזה נבדקה רק הקריאה. באג בכתיבה מסוכן יותר - הוא הורס את
# הקובץ של המשתמש, והוא מתגלה רק כשהוא פותח אותו מחר.
#
#   powershell -ExecutionPolicy Bypass -File build	est-roundtrip.ps1
#
# עד עכשיו נבדקה רק הקריאה. באג בכתיבה מסוכן יותר - הוא הורס את הקובץ
# של המשתמש, והוא מתגלה רק כשהוא פותח אותו מחר.
$ErrorActionPreference = 'Continue'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\SubtitleStudio.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i=0;$i -lt $args.Count;$i++){$a[$i]=$args[$i]}; return ,$a }
$fmt = T 'Formats'; $cueT = T 'Cue'
$ctor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))
$listT = [System.Collections.Generic.List``1].MakeGenericType($cueT)
$toSrt = $fmt.GetMethod('ToSrt', $ST)
$toVtt = $fmt.GetMethod('ToVtt', $ST)
$parseSrt = $fmt.GetMethod('ParseSrt', $ST)
$parseVtt = $fmt.GetMethod('ParseVtt', $ST)

$pass=0; $fail=0
function Trip($name, $text, $writer, $reader, $expect = $null) {
    if ($expect -eq $null) { $expect = $text }
    $l = [Activator]::CreateInstance($listT)
    $listT.GetMethod('Add').Invoke($l, (Pack ($ctor.Invoke(@([long]1000,[long]3000,[string]$text)))))
    $listT.GetMethod('Add').Invoke($l, (Pack ($ctor.Invoke(@([long]5000,[long]7000,[string]'עוגן')))))
    try {
        $out = $writer.Invoke($null, (Pack $l))
        $back = $reader.Invoke($null, (Pack ([string]$out)))
        $n = $back.Count
        $got = if ($n -gt 0) { $cueT.GetField('Text').GetValue($back[0]) } else { '<אין>' }
        $ok = ($n -eq 2) -and ($got -eq $expect)
        $d = "$n cues"
        if (-not $ok) { $d += "   got='" + $got.Replace("`n","\n") + "'  want='" + $text.Replace("`n","\n") + "'" }
        if ($ok) { $script:pass++; Write-Host ("  ok    {0,-32} {1}" -f $name, $d) }
        else { $script:fail++; Write-Host ("  FAIL  {0,-32} {1}" -f $name, $d) -ForegroundColor Red }
    } catch {
        $script:fail++
        Write-Host ("  FAIL  {0,-32} חריגה: {1}" -f $name, $_.Exception.InnerException.GetType().Name) -ForegroundColor Red
    }
}

Write-Host 'SRT - הלוך ושוב'
Trip 'טקסט רגיל'          'שלום עולם' $toSrt $parseSrt
Trip 'שתי שורות'          "שורה א`nשורה ב" $toSrt $parseSrt
Trip 'מספר בודד'          '5' $toSrt $parseSrt
Trip 'מספר בשורה שנייה'   "שלום`n42" $toSrt $parseSrt
Trip 'חץ בתוך הטקסט'      'הוא אמר --> לך' $toSrt $parseSrt
# כתובית שכל תוכנה הוא שורת זמן שוברת את הפורמט מעצם הגדרתו - אין
# ל-SRT דרך לייצג את זה, וגם שאר הכלים לא פותרים. מתועד, לא נבדק.
# שורה ריקה לא ניתנת לייצוג בפורמט; העיקר שהטקסט **לא** נאבד
Trip 'שורה ריקה מושמטת'   "שורה א`n`nשורה ב" $toSrt $parseSrt "שורה א`nשורה ב"
Trip 'תגית זווית'         'אמרתי <שלום>' $toSrt $parseSrt
Trip 'אמפרסנד'            'א & ב' $toSrt $parseSrt
Trip 'סוגריים מסולסלים'   'זה {משהו}' $toSrt $parseSrt
# רווחים בקצוות נחתכים - זו ההתנהגות הנכונה, אף אחד לא רוצה אותם
Trip 'רווחים בקצוות'      '  שלום  ' $toSrt $parseSrt 'שלום'

Write-Host ''
Write-Host 'VTT - הלוך ושוב'
Trip 'טקסט רגיל'      'שלום עולם' $toVtt $parseVtt
Trip 'שתי שורות'      "שורה א`nשורה ב" $toVtt $parseVtt
Trip 'אמפרסנד'        'א & ב' $toVtt $parseVtt
Trip 'תגית זווית'     'אמרתי <שלום>' $toVtt $parseVtt
Trip 'חץ בתוך הטקסט'  'הוא אמר --> לך' $toVtt $parseVtt

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)

if ($fail -gt 0) { exit 1 }
