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
for %%I in ("%SCRIPT_DIR%..\..") do set ROOT_DIR=%%~fI
set TEST_SERVER_DIR=%ROOT_DIR%\connectors\test-server

pushd "%TEST_SERVER_DIR%" >nul
  docker compose up -d
popd >nul

"%UE_EDITOR_CMD%" "%UPROJECT_PATH%" -ExecCmds="Automation RunTests PlayHouse.*;Quit" -unattended -nopause -nosplash
set UE_EXIT=%ERRORLEVEL%

pushd "%TEST_SERVER_DIR%" >nul
  docker compose down
popd >nul

exit /b %UE_EXIT%
