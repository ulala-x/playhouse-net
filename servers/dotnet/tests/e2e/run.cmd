@echo off
REM E2E 테스트 실행 스크립트 (Windows)

setlocal enabledelayedexpansion

set SCRIPT_DIR=%~dp0
set PROJECT_DIR=%SCRIPT_DIR%PlayHouse.E2E

echo ==========================================
echo PlayHouse E2E Tests
echo ==========================================

cd /d "%SCRIPT_DIR%..\.."

echo [1/2] Building...
dotnet build "%PROJECT_DIR%" --configuration Release --verbosity quiet
if errorlevel 1 (
    echo Build failed!
    exit /b 1
)

echo [2/2] Running E2E tests...
dotnet run --project "%PROJECT_DIR%" --configuration Release --no-build

echo ==========================================
echo Done
echo ==========================================
