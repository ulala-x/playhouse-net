@echo off
setlocal enabledelayedexpansion

rem Usage:
rem set UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.3\Engine\Binaries\Win64\UnrealEditor-Cmd.exe
rem set UPROJECT_PATH=C:\Work\MyGame\MyGame.uproject
rem connectors\unreal\scripts\run-local-e2e.cmd

if "%UE_EDITOR_CMD%"=="" (
  echo UE_EDITOR_CMD is required
  exit /b 1
)

if "%UPROJECT_PATH%"=="" (
  echo UPROJECT_PATH is required
  exit /b 1
)

set SCRIPT_DIR=%~dp0
for %%I in ("%SCRIPT_DIR%..\..\..") do set ROOT_DIR=%%~fI
set TEST_SERVER_DIR=%ROOT_DIR%\connectors\test-server
set REPORT_DIR=%ROOT_DIR%\_ue\automation-reports

if not exist "%REPORT_DIR%" (
  mkdir "%REPORT_DIR%"
)

pushd "%TEST_SERVER_DIR%" >nul
  docker compose up -d
popd >nul

rem Wait for test-server health endpoint
powershell -NoProfile -Command ^
  "for ($i = 0; $i -lt 60; $i++) { " ^
  "  try { Invoke-WebRequest -UseBasicParsing http://localhost:8080/api/health | Out-Null; exit 0 } " ^
  "  catch { Start-Sleep -Seconds 1 } " ^
  "} exit 1"
if errorlevel 1 (
  echo test-server health check failed
  exit /b 1
)

"%UE_EDITOR_CMD%" "%UPROJECT_PATH%" ^
  -ExecCmds="Automation RunTests StartsWith:PlayHouse" ^
  -TestExit="Automation Test Queue Empty" ^
  -ReportExportPath="%REPORT_DIR%" ^
  -unattended -nopause -nosplash
set UE_EXIT=%ERRORLEVEL%

pushd "%TEST_SERVER_DIR%" >nul
  docker compose down
popd >nul

exit /b %UE_EXIT%
