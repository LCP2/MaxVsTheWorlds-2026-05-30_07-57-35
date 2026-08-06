@echo off
REM ============================================================
REM   cc-verify (MV-game / Unity 6 LTS)
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
echo === cc-verify (MV-game) start ===
echo Project : %PROJECT%
echo Unity   : %UNITY_PATH%
echo Log     : %LOG%
echo.

REM ----- 1. Compile check (open project headless, exit) ----------------------
echo [1/5] compile check ...
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
echo [2/5] EditMode tests ...
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

REM ----- 3. PlayMode boot-and-play smoke test (MV-259) ------------------------
REM   cc-verify proves it compiles and holds 60fps; it says nothing about
REM   whether the opening is actually playable (MV-256 slipped through here).
REM   This runs just the boot-and-play gate test, not the full PlayMode suite
REM   (that already runs in CI via build.yml's testMode: all) — keeps local
REM   verify fast while still blocking on the one test that IS the play-check.
echo [3/5] PlayMode boot-and-play check ...
"%UNITY_PATH%" ^
  -batchmode -nographics -projectPath "%PROJECT%" ^
  -runTests -testPlatform PlayMode ^
  -testFilter "MaxWorlds.Tests.PlayMode.BootAndPlaySmokeTests" ^
  -testResults "%PROJECT%\Logs\playmode-results.xml" ^
  -logFile "%PROJECT%\Logs\playmode.log"
if errorlevel 1 (
  echo        FAIL — see Logs\playmode.log ^(play-check did not pass^)
  set "FAIL=1"
) else (
  echo        ok
)

REM ----- 4. Windows standalone smoke build -----------------------------------
echo [4/5] Windows standalone build (Bootstrap.unity) ...
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

REM ----- 5. Log assertions ----------------------------------------------------
echo [5/5] log assertions ...
findstr /C:"targetFrameRate" "%PROJECT%\Logs\build.log" >nul
if errorlevel 1 (
  echo        FAIL — targetFrameRate not referenced in build log
  set "FAIL=1"
) else (
  echo        ok
)

echo.
echo === cc-verify (MV-game) end (fail=%FAIL%) ===

if "%FAIL%"=="0" (
  exit /b 0
) else (
  exit /b 1
)
