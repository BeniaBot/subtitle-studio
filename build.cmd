@echo off
setlocal
rem ============================================================
rem  Subtitle Studio - build script
rem  Produces ONE portable EXE with FFmpeg embedded inside.
rem  Uses the C# compiler that ships with Windows. No SDK needed.
rem ============================================================
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] C# compiler not found in Windows.
  exit /b 1
)

if not exist "build\app.ico" (
  echo Creating icon...
  powershell -NoProfile -ExecutionPolicy Bypass -File "build\make-icon.ps1"
)

echo Preparing embedded engine...
powershell -NoProfile -ExecutionPolicy Bypass -File "build\make-payload.ps1"
if errorlevel 1 exit /b 1

if not exist "dist" mkdir "dist"

set RES=
if exist "build\payload\ffmpeg.pack" set RES=/resource:"build\payload\ffmpeg.pack",ffmpeg.pack

rem  UI font (IBM Plex Sans Hebrew, SIL OFL) - embedded so the app looks the same everywhere.
rem  Listed one by one on purpose: delayed expansion inside the csc line is fragile.
rem  License texts are embedded too, so the single EXE carries its own
rem  paperwork: MIT for our code, GPLv3 for FFmpeg, OFL for the font.

echo Compiling...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:"dist\Subtext.exe" ^
  /win32icon:"build\app.ico" ^
  /win32manifest:"build\app.manifest" ^
  %RES% ^
  /resource:"assets\fonts\IBMPlexSansHebrew-Regular.ttf",IBMPlexSansHebrew-Regular.ttf ^
  /resource:"assets\fonts\IBMPlexSansHebrew-SemiBold.ttf",IBMPlexSansHebrew-SemiBold.ttf ^
  /resource:"assets\fonts\IBMPlexSansHebrew-Bold.ttf",IBMPlexSansHebrew-Bold.ttf ^
  /resource:"licenses\GPL-3.0.txt",GPL-3.0.txt ^
  /resource:"licenses\OFL-1.1-IBM-Plex.txt",OFL-1.1.txt ^
  /resource:"LICENSE",MIT.txt ^
  /resource:"licenses\AGPL-3.0-Hspell.txt",AGPL-3.0.txt ^
  /resource:"assets\spell\torah-he.txt",torah-he.txt ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Web.Extensions.dll ^
  /reference:System.Security.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed.
  exit /b 1
)

for %%F in ("dist\Subtext.exe") do set SIZE=%%~zF
echo.
echo Done:  %cd%\dist\Subtext.exe   (%SIZE% bytes)
endlocal
