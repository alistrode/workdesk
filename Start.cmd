@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
if not exist "%~dp0launcher\WorkDeskLauncher.exe" (
  echo [错误] 找不到 launcher\WorkDeskLauncher.exe
  pause
  exit /b 1
)
start "" "%~dp0launcher\WorkDeskLauncher.exe"
