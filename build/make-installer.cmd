@echo off
setlocal
rem ============================================================
rem  Subtitle Studio - installer build script
rem  Wraps dist\SubtitleStudio.exe into a per-user setup EXE.
rem  Same in-box C# compiler as build.cmd. No SDK, no third
rem  party installer tooling - the setup is plain WinForms.
rem  ASCII only: one Hebrew byte here breaks cmd parsing.
rem ============================================================
cd /d "%~dp0.."

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] C# compiler not found in Windows.
  exit /b 1
)

if not exist "dist\SubtitleStudio.exe" (
  echo [ERROR] dist\SubtitleStudio.exe is missing.
  echo         Run build.cmd first - the setup carries that file inside it.
  exit /b 1
)

if not exist "build\app.ico" (
  echo Creating icon...
  powershell -NoProfile -ExecutionPolicy Bypass -File "build\make-icon.ps1"
)

set SRC=src\Installer
set OUT=build\installer
if not exist "%OUT%" mkdir "%OUT%"

rem  One source of truth for the version: App.Version in src\Updater.cs.
set VER=
for /f tokens^=2^ delims^=^" %%V in ('findstr /c:"public const string Version" "src\Updater.cs"') do set VER=%%V
if "%VER%"=="" (
  echo [ERROR] Could not read App.Version from src\Updater.cs
  exit /b 1
)
echo Version: %VER%
> "%OUT%\version.txt" echo %VER%

echo Compiling uninstaller...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:"%OUT%\uninstall.exe" ^
  /win32icon:"build\app.ico" ^
  /win32manifest:"%OUT%\setup.manifest" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  "%SRC%\Common.cs" "%SRC%\Skin.cs" "%SRC%\ShellLink.cs" ^
  "%SRC%\UninstallUi.cs" "%SRC%\UninstallMain.cs"
if errorlevel 1 (
  echo.
  echo [ERROR] Uninstaller build failed.
  exit /b 1
)

echo Compiling setup (embedding the app - takes a few seconds)...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:"dist\SubtitleStudio-Setup.exe" ^
  /win32icon:"build\app.ico" ^
  /win32manifest:"%OUT%\setup.manifest" ^
  /resource:"dist\SubtitleStudio.exe",SubtitleStudio.exe ^
  /resource:"%OUT%\uninstall.exe",uninstall.exe ^
  /resource:"%OUT%\version.txt",version.txt ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  "%SRC%\Common.cs" "%SRC%\Skin.cs" "%SRC%\ShellLink.cs" ^
  "%SRC%\UninstallUi.cs" "%SRC%\Setup.cs" "%SRC%\SetupForm.cs"
if errorlevel 1 (
  echo.
  echo [ERROR] Setup build failed.
  exit /b 1
)

for %%F in ("%OUT%\uninstall.exe") do set USIZE=%%~zF
for %%F in ("dist\SubtitleStudio-Setup.exe") do set SSIZE=%%~zF
echo.
echo Done:  %cd%\dist\SubtitleStudio-Setup.exe   (%SSIZE% bytes)
echo        uninstaller %USIZE% bytes (embedded, dropped next to the app)
echo.
echo Silent install:    SubtitleStudio-Setup.exe /S
echo Silent to folder:  SubtitleStudio-Setup.exe /S /D="C:\path\SubtitleStudio"
echo Uninstall:         SubtitleStudio-Setup.exe /uninstall   (or uninstall.exe in the app folder)
endlocal
