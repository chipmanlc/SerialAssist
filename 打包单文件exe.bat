@echo off
cd /d "%~dp0SerialAssist.UI"
echo 正在发布单文件 exe(约 1~2 分钟)...
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
    echo.
    echo [失败] 编译出错,请看上方报错信息
    pause
    exit /b 1
)
echo.
echo [完成] 单文件 exe 在:
echo   SerialAssist.UI\bin\Release\net10.0-windows\win-x64\publish\CHEN串口助手.exe
explorer "%~dp0SerialAssist.UI\bin\Release\net10.0-windows\win-x64\publish"
pause
