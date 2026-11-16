@echo off
chcp 65001 >nul
cd /d "%~dp0SerialAssist.UI"
echo Publishing single-file exe...
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
    echo.
    echo [FAILED] See publish output above.
    pause
    exit /b 1
)
echo.
echo [OK] Single-file exe:
echo   SerialAssist.UI\bin\Release\net10.0-windows\win-x64\publish\LuckC.exe
explorer "%~dp0SerialAssist.UI\bin\Release\net10.0-windows\win-x64\publish"
pause