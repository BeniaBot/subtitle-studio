﻿# test-installed-update - עדכון קטן של עותק **מותקן**, מקצה לקצה, בתוך Windows Sandbox (מנותק מהרשת ומהמחשב).
# נבדק לראשונה 11.10.2026 (0.8.9 ← 0.8.10): הכול עבר. ‏**לא להריץ את המתקין מחוץ לארגז:** אם ההפרדה של Claude מ-HKCU
# ומ-AppData לא מלאה, הוא דורס את הקיצור ואת הרישום של ההתקנה האמיתית של בנימין. הארגז פותח חלון - לבקש קודם.
#
# הכנה (בצד המארח), ל-vX ← vY:
#   gh release download vX -p Subtext-Setup.exe        -> D:\Claude\_ss-installed\in\Subtext-Setup-0.8.9.exe (או לשנות את השם ב-ps1)
#   gh release download vY -p Subtext.exe -p 'Subtext-app-update-*.gz'  -> D:\Claude\_ss-installed\in\rel\
#   build\slim-server.ps1 -> in\ ; build\sandbox\installed-update.ps1 -> in\run.ps1 ; installed-update.wsb -> D:\Claude\_ss-installed\test.wsb
#   ‏tag_name וה-URL ב-ps1 (v0.8.10) - לעדכן ל-vY
# הרצה: WindowsSandbox.exe test.wsb, לחכות ל-out\done.txt, לקרוא out\log.txt ולהסתכל על out\*.png;
# לסגור: Stop-Process על WindowsSandboxRemoteSession ו-WindowsSandboxServer.