@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 (
  echo.
  echo Build failed.
) else (
  echo.
  echo Build finished.
)
pause
