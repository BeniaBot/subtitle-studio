# הרשימה: הוספה בין שורות וגרירה בזמן.
#
# שתי הפעולות האלה נוגעות ישירות במסמך, ושתיהן נשברות בשקט: כתובית
# שנוחתת בחפיפה נראית כשתי שורות על המסך בו-זמנית, ואינדקס שגוי בהוספה
# מכניס אותה למקום אחר לגמרי ממה שהמשתמש הצביע עליו.
#
#   powershell -ExecutionPolicy Bypass -File build	est-list.ps1
$ErrorActionPreference='Stop'
$env:SUBSTUDIO_TEST='1'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\SubtitleStudio.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$asm=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$NP=[Reflection.BindingFlags]'NonPublic,Instance'
$ST=[Reflection.BindingFlags]'NonPublic,Public,Static'
$PI=[Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n){$asm.GetType("SubtitleStudio.$n")}
function Pack { $a=New-Object object[] $args.Count; for($i=0;$i -lt $args.Count;$i++){$a[$i]=$args[$i]}; return ,$a }
$pass=0;$fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok   $n   $d"}else{$script:fail++;Write-Host "  FAIL $n   $d" -ForegroundColor Red} }

# מסמך עם ארבע כתוביות ורווחים ביניהן
$docT=T 'Doc'; $cueT=T 'Cue'
$ctor=$cueT.GetConstructor([Type[]]@([long],[long],[string]))
$doc=[Activator]::CreateInstance($docT)
$cues=$docT.GetField('Cues',$PI).GetValue($doc)
foreach($r in @(@(1000,3000,'ראשונה'),@(5000,7000,'שנייה'),@(9000,11000,'שלישית'),@(13000,15000,'רביעית'))){
  $cues.Add($ctor.Invoke(@([long]$r[0],[long]$r[1],[string]$r[2])))
}

$listT=T 'CueList'
$list=[Activator]::CreateInstance($listT)
$listT.GetField('Doc',$PI).SetValue($list,$doc)
$list.SetBounds(0,0,400,600)

$rowH=$listT.GetField('_rowH',$NP).GetValue($list)
$hdr=$listT.GetProperty('HeaderH',$ST).GetValue($null,$null)
Write-Host ("גובה שורה=$rowH  כותרת=$hdr")

# GapAt: על הקו כן, באמצע שורה לא
$gapAt=$listT.GetMethod('GapAt',$NP)
Check 'קו לפני הראשונה מזוהה' ($gapAt.Invoke($list,(Pack ([int]$hdr))) -eq 0) ''
Check 'קו בין 1 ל-2 מזוהה'   ($gapAt.Invoke($list,(Pack ([int]($hdr+$rowH)))) -eq 1) ''
Check 'אמצע שורה לא מזוהה'   ($gapAt.Invoke($list,(Pack ([int]($hdr+$rowH/2)))) -eq -1) ''
Check 'קו אחרי האחרונה מזוהה' ($gapAt.Invoke($list,(Pack ([int]($hdr+4*$rowH)))) -eq 4) ''

# האירוע נורה עם האינדקס הנכון
$script:got=-99
$handler=[EventHandler[int]]{ param($s,$i) $script:got=$i }
$listT.GetEvent('InsertRequested').AddEventHandler($list,$handler)
$md=$listT.GetMethod('OnMouseDown',$NP)
$ev=New-Object Windows.Forms.MouseEventArgs ([Windows.Forms.MouseButtons]::Left),1,50,([int]($hdr+2*$rowH)),0
$md.Invoke($list,(Pack ([Windows.Forms.MouseEventArgs]$ev.PSObject.BaseObject)))
Check 'לחיצה על הקו יורה InsertRequested' ($script:got -eq 2) ("got=" + $script:got)

# גרירה: מזיזה בזמן, לא מחליפה טקסט
$dragRow=$listT.GetField('_dragRow',$NP)
$dropB=$listT.GetField('_dropBefore',$NP)
$dragging=$listT.GetField('_dragging',$NP)
$finish=$listT.GetMethod('FinishDrag',$NP)
$fS=$cueT.GetField('Start'); $fT=$cueT.GetField('Text')

# גוררים את הרביעית (13s) לראש הרשימה
$dragRow.SetValue($list,3); $dropB.SetValue($list,0); $dragging.SetValue($list,$true)
$moved=$finish.Invoke($list,@())
Check 'הגרירה החזירה כתובית' ($null -ne $moved) ''
if ($moved) {
  $t=$fT.GetValue($moved); $s=$fS.GetValue($moved)
  Check 'הטקסט לא השתנה' ($t -eq 'רביעית') ("text=$t")
  Check 'הזמן זז לראש'   ($s -lt 1000) ("start=$s")
  Check 'הרשימה ממוינת'  ($fT.GetValue($cues[0]) -eq 'רביעית') ("first=" + $fT.GetValue($cues[0]))
  Check 'עדיין 4 כתוביות' ($cues.Count -eq 4) ("count=" + $cues.Count)
}
# אין חפיפה עם השכנה
$fE=$cueT.GetField('End')
$overlap = $false
for ($i=0; $i -lt $cues.Count-1; $i++) {
  if ($fE.GetValue($cues[$i]) -gt $fS.GetValue($cues[$i+1])) { $overlap = $true }
}
Check 'הגרירה לא יצרה חפיפה' (-not $overlap) ''

# ואפשר לבטל. Undo מחליף את כל הרשימה, אז קוראים אותה מחדש.
$docT.GetMethod('Undo',$PI).Invoke($doc,@())
$cues2 = $docT.GetField('Cues',$PI).GetValue($doc)
Check 'Ctrl+Z מחזיר את הסדר' ($fT.GetValue($cues2[0]) -eq 'ראשונה') ("first=" + $fT.GetValue($cues2[0]))

$list.Dispose()
Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass,$fail)
if($fail -gt 0){exit 1}
