@echo off
cd /d "%~dp0"
echo ============================================
echo  提交并推送到 GitHub (chipmanlc/SerialAssist)
echo ============================================
if "%~1"=="" (
    set /p MSG=请输入本次改动说明:
) else (
    set "MSG=%~1"
)
if "%MSG%"=="" (
    echo [取消] 没有填写改动说明
    pause
    exit /b 1
)
git add -A
git commit -m "%MSG%"
if errorlevel 1 (
    echo [提示] 没有检测到改动,或提交失败
    git push
    pause
    exit /b 0
)
git push
if errorlevel 1 (
    echo [失败] 推送失败,检查网络或 SSH 配置
    pause
    exit /b 1
)
echo.
echo [完成] 已推送到 GitHub
pause
