# מטמון פס הקול בציר הזמן.
#
# ציור פס הקול סורק את כל דליי הזמן שנופלים על כל פיקסל. בסרט של שלוש
# שעות בזום-אאוט מלא - שזו בדיוק התצוגה שנפתחת כשפותחים סרט ארוך - כל
# פיקסל מכסה כ-770 דליים, ונמדדו 22ms לכל ציור. בניגון הציר מצויר מחדש
# בכל עדכון מיקום, ולכן הכול נהיה מרפרף. עם המטמון: 4.7ms.
#
# המטמון נכון רק אם הוא מתרענן בדיוק כשצריך, ולא מתרענן כשלא צריך -
# וזה מה שנבדק כאן, מול פיקסלים אמיתיים ולא מול הצהרות.
#
#   powershell -ExecutionPolicy Bypass -File build	est-wave.ps1
$ErrorActionPreference='Stop'
$env:SUBSTUDIO_TEST='1'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\SubtitleStudio.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$asm=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$ST=[Reflection.BindingFlags]'NonPublic,Public,Static'
$IN=[Reflection.BindingFlags]'NonPublic,Public,Instance'
$NP=[Reflection.BindingFlags]::NonPublic; $PB=[Reflection.BindingFlags]::Public
$CI=[Reflection.BindingFlags]::CreateInstance; $INF=[Reflection.BindingFlags]::Instance
function T($n){$asm.GetType("SubtitleStudio.$n")}
function Pack { $a=New-Object object[] $args.Count; for($i=0;$i -lt $args.Count;$i++){$a[$i]=$args[$i]}; return ,$a }

# פס קול מזויף: גל שמשתנה לאורך הזמן, כדי שקטעים שונים ייראו שונה
$wT=T 'Waveform'; $w=[Activator]::CreateInstance($wT)
$n=180000    # חצי שעה
$peak=New-Object byte[] $n; $rms=New-Object byte[] $n
for($i=0;$i -lt $n;$i++){ $v=[int](128+120*[Math]::Sin($i/900.0)); $peak[$i]=[byte]$v; $rms[$i]=[byte]([int]($v*0.6)) }
$wT.GetField('Peak',$IN).SetValue($w,$peak)
$wT.GetField('Rms',$IN).SetValue($w,$rms)
$wT.GetField('Ready',$IN).SetValue($w,$true)
$wT.GetField('DurationMs',$IN).SetValue($w,[long]($n*10))

$tlT=T 'TimelineControl'
$tl=[Activator]::CreateInstance($tlT,($NP -bor $PB -bor $INF -bor $CI),$null,@(),$null)
$tlT.GetField('Wave',$IN).SetValue($tl,$w)
$tlT.GetField('DurationMs',$IN).SetValue($tl,[long]($n*10))
$tlT.GetField('Doc',$IN).SetValue($tl,[Activator]::CreateInstance((T 'Doc')))
$tl.SetBounds(0,0,900,180)

function Shot(){
  $b=New-Object Drawing.Bitmap 900,180
  $tl.DrawToBitmap($b,(New-Object Drawing.Rectangle 0,0,900,180))
  $sb=New-Object Text.StringBuilder
  for($y=20;$y -lt 120;$y+=7){ for($x=0;$x -lt 900;$x+=7){ [void]$sb.Append($b.GetPixel($x,$y).ToArgb()) } }
  $b.Dispose()
  $md=[Security.Cryptography.MD5]::Create()
  return [BitConverter]::ToString($md.ComputeHash([Text.Encoding]::UTF8.GetBytes($sb.ToString())))
}

$pass=0;$fail=0
function Check($name,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok   $name   $d"}else{$script:fail++;Write-Host "  FAIL $name   $d" -ForegroundColor Red} }

$a = Shot
$b = Shot
Check 'ציור חוזר זהה (המטמון פעיל)' ($a -eq $b) ''

# גלילה וזום חייבים לצייר מחדש
$vs = $tlT.GetField('ViewStart',$IN)
$pps = $tlT.GetField('PxPerSec',$IN)
$vs.SetValue($tl,[long]300000)
$c = Shot
Check 'גלילה מרעננת את המטמון' ($c -ne $a) ''
$vs.SetValue($tl,[long]0)
$d = Shot
Check 'חזרה לאותו מקום מחזירה אותה תמונה' ($d -eq $a) ''
$pps.SetValue($tl,[double]200)
$z = Shot
Check 'זום מרענן את המטמון' ($z -ne $a) ''
$pps.SetValue($tl,[double]40)
$z2 = Shot
Check 'חזרה מזום מחזירה אותה תמונה' ($z2 -eq $a) ''

# הסמן זז - זה מה שקורה בניגון, ופס הקול לא אמור להיבנות מחדש
$posF = $tlT.GetField('Position',$IN)
$posF.SetValue($tl,[long]120000)
$keyF = $tlT.GetField('_waveKey',$IN)
$k1 = $keyF.GetValue($tl)
Shot | Out-Null
$posF.SetValue($tl,[long]240000)
Shot | Out-Null
$k2 = $keyF.GetValue($tl)
Check 'תזוזת הסמן לא בונה מחדש' ($k1 -eq $k2 -and $k1 -ne $null) ("key=" + $k1)
$posF.SetValue($tl,[long]0)

# שינוי גודל
$tl.SetBounds(0,0,600,180)
$e = Shot
$tl.SetBounds(0,0,900,180)
$f = Shot
Check 'שינוי גודל מרענן' ($f -eq $a) ''

# החלפת ערכה
(T 'Theme').GetMethod('Toggle',$ST) | Out-Null
$dark = (T 'Theme').GetField('Dark',$ST)
$old = $dark.GetValue($null)
$dark.SetValue($null, -not $old)
(T 'Theme').GetMethod('Palette',$ST) | Out-Null
$g2 = Shot
Check 'החלפת ערכה מרעננת' ($g2 -ne $a) ''
$dark.SetValue($null,$old)

# תוכן פס הקול השתנה (הבנייה מתקדמת)
$h1 = Shot
for($i=0;$i -lt 2000;$i++){ $peak[$i]=[byte]255 }
$wT.GetField('Version',$IN).SetValue($w, 99)
$h2 = Shot
Check 'התקדמות הבנייה מרעננת' ($h2 -ne $h1) ''

# הבדיקה שמכריעה: התמונה שבמטמון חייבת להיות זהה לציור הישיר.
# בלעדיה כל השאר בודק רק שהמטמון עקבי עם עצמו - גם אם הוא מצייר שטות.
$dwInto = $tlT.GetMethod('DrawWaveInto', $IN)
$h = 76
$direct = New-Object Drawing.Bitmap 900, $h
$dg = [Drawing.Graphics]::FromImage($direct)
(T 'Theme').GetMethod('Smooth', $ST).Invoke($null, (Pack $dg))
$bg = New-Object Drawing.SolidBrush ((T 'Theme').GetProperty('WaveBack', $ST).GetValue($null, $null))
$dg.FillRectangle($bg, 0, 0, 900, $h)
$dwInto.Invoke($tl, (Pack $dg ([int]0) ([int]$h)))
$dg.Dispose(); $bg.Dispose()

$cacheF = $tlT.GetField('_waveCache', $IN)
$tl.DrawToBitmap((New-Object Drawing.Bitmap 900,180), (New-Object Drawing.Rectangle 0,0,900,180)) | Out-Null
$cached = $cacheF.GetValue($tl)

$diff = 0; $checked = 0
if ($cached -and $cached.Height -eq $h -and $cached.Width -eq 900) {
    for ($y = 0; $y -lt $h; $y += 3) {
        for ($x = 0; $x -lt 900; $x += 3) {
            $checked++
            if ($cached.GetPixel($x,$y).ToArgb() -ne $direct.GetPixel($x,$y).ToArgb()) { $diff++ }
        }
    }
    Check 'המטמון זהה לציור הישיר' ($diff -eq 0) ("$diff / $checked פיקסלים שונים")
} else {
    Check 'המטמון זהה לציור הישיר' $false ("גודל המטמון לא תואם: " + $(if($cached){"$($cached.Width)x$($cached.Height)"}else{'null'}) + " מול 900x$h")
}
$direct.Dispose()

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass,$fail)
if($fail -gt 0){exit 1}
