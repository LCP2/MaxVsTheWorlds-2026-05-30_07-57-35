@echo off
REM ============================================================
REM   cc-verify (YT-game / Unity 6 LTS)
REM
REM   Runs after every code change. Exit 0 = pass.
REM   Target runtime: under 90 s in steady state.
REM
REM   Pre-reqs:
REM     - %UNITY_PATH% env var pointing at Unity.exe
REM       (e.g. C:\Program Files\Unity\Hub\Editor\6000.4.9f1\Editor\Unity.exe)
REM     - Run from the repo root (C:\dev\MaxVsTheWorlds).
REM ============================================================

setlocal enabledelayedexpansion
set "FAIL=0"
set "PROJECT=%CD%"
set "LOG=%PROJECT%\Logs\cc-verify.log"
set "BUILD=%PROJECT%\Builds\cc-verify"

if not defined UNITY_PATH (
  echo [cc-verify] UNITY_PATH not set. Point it at Unity.exe and retry.
  exit /b 2
)
if not exist "%UNITY_PATH%" (
  echo [cc-verify] UNITY_PATH does not exist: %UNITY_PATH%
  exit /b 2
)

if not exist "%PROJECT%\Logs"   mkdir "%PROJECT%\Logs"
if not exist "%PROJECT%\Builds" mkdir "%PROJECT%\Builds"

echo.
echo === cc-verify (YT-game) start ===
echo Project : %PROJECT%
echo Unity   : %UNITY_PATH%
echo Log     : %LOG%
echo.

REM ----- 1. Compile check (open project headless, exit) ----------------------
echo [1/4] compile check ...
"%UNITY_PATH%" ^
  -batchmode -nographics -projectPath "%PROJECT%" -quit ^
  -logFile "%PROJECT%\Logs\compile.log"
if errorlevel 1 (
  echo        FAIL — see Logs\compile.log
  set "FAIL=1"
) else (
  echo        ok
)

REM ----- 2. EditMode tests ----------------------------------------------------
echo [2/4] EditMode tests ...
"%UNITY_PATH%" ^
  -batchmode -nographics -projectPath "%PROJECT%" ^
  -runTests -testPlatform EditMode ^
  -testResults "%PROJECT%\Logs\editmode-results.xml" ^
  -logFile "%PROJECT%\Logs\editmode.log"
if errorlevel 1 (
  echo        FAIL — see Logs\editmode.log
  set "FAIL=1"
) else (
  echo        ok
)

REM ----- 3. Windows standalone smoke build -----------------------------------
echo [3/4] Windows standalone build (Bootstrap.unity) ...
if exist "%BUILD%" rmdir /S /Q "%BUILD%"
mkdir "%BUILD%"
"%UNITY_PATH%" ^
  -batchmode -nographics -projectPath "%PROJECT%" -quit ^
  -buildTarget Win64 ^
  -executeMethod MaxWorlds.Editor.HeadlessBuild.WindowsBootstrap ^
  -buildOutput "%BUILD%\MaxVsTheWorlds.exe" ^
  -logFile "%PROJECT%\Logs\build.log"
if errorlevel 1 (
  echo        FAIL — see Logs\build.log
  set "FAIL=1"
) else (
  echo        ok
)

REM ----- 4. Log assertions ----------------------------------------------------
echo [4/4] log assertions ...
findstr /C:"targetFrameRate" "%PROJECT%\Logs\build.log" >nul
if errorlevel 1 (
  echo        FAIL — targetFrameRate not referenced in build log
  set "FAIL=1"
) else (
  echo        ok
)

echo.
echo === cc-verify (YT-game) end (fail=%FAIL%) ===

if "%FAIL%"=="0" (
  exit /b 0
) else (
  exit /b 1
)
