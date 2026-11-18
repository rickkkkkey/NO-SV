@echo off
cd /d "%~dp0"

if not exist "tosu\tosu.exe" (
  echo tosu not found, installing it...
  powershell -ExecutionPolicy Bypass -File "scripts\install-tosu.ps1"
  if errorlevel 1 (
    echo Could not install tosu. See README.md for the manual steps.
    pause
    exit /b 1
  )
)

rem Only start tosu if it is not already running, and only stop it afterwards if we started it.
tasklist /fi "imagename eq tosu.exe" | find /i "tosu.exe" >nul
if errorlevel 1 (
  set STARTED_TOSU=1
  start "" /min "tosu\tosu.exe"
)

dotnet run -c Release --project NoSvMania.csproj

if defined STARTED_TOSU taskkill /im tosu.exe /f >nul 2>&1
