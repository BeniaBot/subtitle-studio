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

rem  UI font (Assistant, SIL OFL) - embedded so the app looks the same on every machine.
rem  Listed one by one on purpose: delayed expansion inside the csc line is fragile.

echo Compiling...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:"dist\SubtitleStudio.exe" ^
  /win32icon:"build\app.ico" ^
  /win32manifest:"build\app.manifest" ^
  %RES% ^
  /resource:"assets\fonts\Assistant-Regular.ttf",Assistant-Regular.ttf ^
  /resource:"assets\fonts\Assistant-SemiBold.ttf",Assistant-SemiBold.ttf ^
  /resource:"assets\fonts\Assistant-Bold.ttf",Assistant-Bold.ttf ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed.
  exit /b 1
)

for %%F in ("dist\SubtitleStudio.exe") do set SIZE=%%~zF
echo.
echo Done:  %cd%\dist\SubtitleStudio.exe   (%SIZE% bytes)
endlocal
