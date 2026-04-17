@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

set PROJECT_PATH=%~dp0..\
set LOG_PATH=%PROJECT_PATH%\HotfixOutput\build.log

echo ========================================
echo        Unity Build Tool
echo ========================================
echo.

tasklist /FI "IMAGENAME eq Unity.exe" 2>nul | find /I "Unity.exe" >nul
if %ERRORLEVEL% equ 0 (
    echo [Error] Unity Editor is running!
    echo Please close Unity Editor before using command line build.
    echo.
    goto :end
)

if not exist "%PROJECT_PATH%\HotfixOutput" mkdir "%PROJECT_PATH%\HotfixOutput"

for /f "tokens=2 delims==" %%a in ('findstr /r "^UnityPath" "%~dp0build.config" 2^>nul') do set UNITY_PATH=%%a

if "%UNITY_PATH%"=="" (
    echo [Error] Unity path not configured!
    echo Please edit build.config and set UnityPath=your_unity_path
    echo Example: UnityPath=C:\Program Files\Unity\Hub\Editor\2021.3.0f1\Editor\Unity.exe
    echo.
    goto :end
)

echo [1] Hotfix Build (Patch+1)
echo [2] Full Package Build (Major+1)
echo [3] Hotfix + ConfirmRelease
echo.
set /p CHOICE="Select build mode (1/2/3): "

if "%CHOICE%"=="1" (
    set BUILD_TYPE=hotfix
    set EXTRA_ARGS=
    echo.
    echo [Build] Starting hotfix build...
) else if "%CHOICE%"=="2" (
    set BUILD_TYPE=full
    set EXTRA_ARGS=
    echo.
    echo [Build] Starting full package build...
) else if "%CHOICE%"=="3" (
    set BUILD_TYPE=hotfix
    set EXTRA_ARGS= -confirmRelease
    echo.
    echo [Build] Starting hotfix build with auto-confirm...
) else (
    echo.
    echo [Error] Invalid choice!
    goto :end
)

echo [Build] Unity: %UNITY_PATH%
echo [Build] Project: %PROJECT_PATH%
echo [Build] Log: %LOG_PATH%
echo.

"%UNITY_PATH%" ^
    -batchmode -quit ^
    -projectPath "%PROJECT_PATH%" ^
    -executeMethod BuildCommandLine.Build ^
    -buildType %BUILD_TYPE%%EXTRA_ARGS% ^
    -logFile "%LOG_PATH%"

echo.
echo ========================================
if %ERRORLEVEL% equ 0 (
    echo [Build] SUCCESS! Exit code: 0
) else (
    echo [Build] FAILED! Exit code: %ERRORLEVEL%
)
echo ========================================
echo Log file: %LOG_PATH%

:end
echo.
pause
