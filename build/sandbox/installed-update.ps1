# רץ בתוך Windows Sandbox: מתקין 0.8.9, מעדכן אותו ל-0.8.10 דרך שרת מדומה בתוך הארגז, ובודק את ההתקנה אחרי העדכון.
$ErrorActionPreference = 'Continue'
$in = 'C:\in'; $out = 'C:\out'
function Log($s) { Add-Content "$out\log.txt" ('[' + (Get-Date -Format HH:mm:ss) + '] ' + $s) -Encoding UTF8 }
function Shot($name) {
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $b = [Windows.Forms.Screen]::PrimaryScreen.Bounds; $bmp = New-Object Drawing.Bitmap $b.Width, $b.Height
    $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [Drawing.Point]::Empty, $b.Size); $bmp.Save("$out\$name.png"); $g.Dispose(); $bmp.Dispose()
}
try {
    Log 'התחלה'
    # 1. התקנה שקטה של 0.8.9, כמו שמשתמש מתקין
    $p = Start-Process "$in\Subtext-Setup-0.8.9.exe" -ArgumentList '/S' -Wait -PassThru
    Log ('מתקין 0.8.9: קוד יציאה ' + $p.ExitCode)
    $dir = Join-Path $env:LOCALAPPDATA 'Programs\Subtext'; $exe = Join-Path $dir 'Subtext.exe'
    Log ('מותקן: ' + (Test-Path $exe) + '  גרסת הקובץ: ' + (Get-Item $exe -ErrorAction SilentlyContinue).VersionInfo.FileVersion)
    $uk = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SubtitleStudio'
    $ow = 'HKCU:\Software\Classes\Subtext.Media\shell\open\command'
    $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Subtext.lnk'
    $sh = New-Object -ComObject WScript.Shell
    Log ('לפני העדכון - גרסה ברישום: ' + (Get-ItemProperty $uk -ErrorAction SilentlyContinue).DisplayVersion)
    Log ('לפני העדכון - ״פתיחה באמצעות״: ' + (Get-ItemProperty $ow -ErrorAction SilentlyContinue).'(default)')
    Log ('לפני העדכון - mts ב״פתיחה באמצעות״: ' + ((Get-Item 'HKCU:\Software\Classes\.mts\OpenWithProgids' -ErrorAction SilentlyContinue) -ne $null))
    Log ('לפני העדכון - קיצור בתפריט התחל: ' + $(if (Test-Path $lnk) { $sh.CreateShortcut($lnk).TargetPath } else { 'אין' }))

    # 2. שרת מדומה בתוך הארגז, עם הקבצים של המהדורה 0.8.10 כמו שהם
    $port = 18765; $rel = 'C:\rel'; New-Item -ItemType Directory -Force $rel | Out-Null
    Copy-Item "$in\rel\*" $rel
    $dl = "http://127.0.0.1:$port/BeniaBot/subtitle-studio/releases/download/v0.8.10/"
    $assets = foreach ($f in Get-ChildItem $rel -File) {
        '{"name":"' + $f.Name + '","size":' + $f.Length + ',"digest":"sha256:' + (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower() + '","browser_download_url":"' + $dl + $f.Name + '"}'
    }
    $json = '{"tag_name":"v0.8.10","name":"0.8.10","body":"## בדיקה בארגז","assets":[' + ($assets -join ',') + ']}'
    [IO.File]::WriteAllText("$rel\latest.json", $json, (New-Object Text.UTF8Encoding $false))
    Start-Process powershell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "$in\slim-server.ps1", '-Port', $port, '-Root', $rel,
        '-Json', "$rel\latest.json", '-Log', "$out\server.log", '-Stop', "$rel\stop") -WindowStyle Hidden | Out-Null
    Start-Sleep 2

    # 3. מפעילים את 0.8.9 המותקנת. מול השרת המקומי היא ״לוחצת״ לבד על ״לעדכן עכשיו״
    $env:SUBSTUDIO_UPDATE_API = "http://127.0.0.1:$port/repos/BeniaBot/subtitle-studio/releases/latest"
    $env:SUBSTUDIO_TEST_CLICK = 'update'
    $expected = (Get-FileHash "$rel\Subtext.exe" -Algorithm SHA256).Hash
    $old = Start-Process $exe -PassThru
    Log ('0.8.9 המותקנת עלתה, תהליך ' + $old.Id)
    Start-Sleep 6; Shot '1-offer'

    # 4. מחכים שהקובץ המותקן יהיה 0.8.10
    $sw = [Diagnostics.Stopwatch]::StartNew(); $same = $false
    while ($sw.Elapsed.TotalSeconds -lt 300) {
        Start-Sleep 2
        $h = (Get-FileHash $exe -Algorithm SHA256 -ErrorAction SilentlyContinue).Hash
        if ($h -eq $expected) { $same = $true; break }
    }
    Log ('הקובץ המותקן זהה ל-0.8.10 שפורסמה: ' + $same + '  (אחרי ' + [int]$sw.Elapsed.TotalSeconds + ' שניות)')
    Start-Sleep 25    # הגרסה החדשה עולה, ובפתיחה מרעננת את הרישום (Install.Refresh)
    Shot '2-after'
    Log ('תהליכים של Subtext שרצים: ' + ((Get-Process Subtext -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) -join ', '))
    Log ('אחרי העדכון - גרסה ברישום: ' + (Get-ItemProperty $uk -ErrorAction SilentlyContinue).DisplayVersion)
    Log ('אחרי העדכון - ״פתיחה באמצעות״: ' + (Get-ItemProperty $ow -ErrorAction SilentlyContinue).'(default)')
    $mts = Get-Item 'HKCU:\Software\Classes\.mts\OpenWithProgids' -ErrorAction SilentlyContinue
    Log ('אחרי העדכון - mts (סוג חדש ב-0.8.10) ב״פתיחה באמצעות״: ' + ($mts -ne $null -and ($mts.GetValueNames() -contains 'Subtext.Media')))
    Log ('אחרי העדכון - קיצור בתפריט התחל: ' + $(if (Test-Path $lnk) { $sh.CreateShortcut($lnk).TargetPath } else { 'אין' }))
    Log ('נשאר קובץ .old: ' + (Test-Path "$exe.old"))
    Log ('בקשות לשרת: ' + ((Get-Content "$out\server.log" -ErrorAction SilentlyContinue) -join ' | '))
    Copy-Item (Join-Path $env:APPDATA 'SubtitleStudio\ai-log.txt') "$out\ai-log.txt" -ErrorAction SilentlyContinue
}
catch { Log ('שגיאה: ' + $_) }
finally {
    Set-Content 'C:\rel\stop' 'x' -ErrorAction SilentlyContinue
    Log 'סוף'
    Set-Content "$out\done.txt" 'done'
}
