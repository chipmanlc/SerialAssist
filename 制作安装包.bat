@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo [1/2] publish single-file exe...
dotnet publish SerialAssist.UI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto fail

echo [2/2] build EXE installer...
set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo Inno Setup 6 not found. Install it first: https://jrsoftware.org/isdl.php
  goto fail
)
"%ISCC%" "installer\LuckyC SerialAssist.iss"
if errorlevel 1 goto fail

echo.
echo OK: dist\LuckyC-SerialAssist-Setup.exe
explorer dist
pause
exit /b 0

:fail
echo.
echo FAILED
pause
exit /b 1