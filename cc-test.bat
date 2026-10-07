@echo off
REM ============================================================
REM   cc-test (MV-game / Unity 6 LTS)
REM
REM   Runs ONE filtered EditMode pass for fast fail-first proof and fix
REM   confirmation (MV-1138) - never a substitute for cc-verify.bat, which
REM   still gates every merge. See CC_AUTONOMY.md, "When to run what".
REM
REM   Usage: cc-test.bat <testFilter>
REM     e.g. cc-test.bat MaxWorlds.Tests.EditMode.LevelDesignVerifierLogOnceTests
REM
REM   Exit codes:
REM     0 - results file exists, total > 0, failed == 0
REM     2 - total == 0 (filter matched nothing)
REM     1 - anything else, including no argument given
REM ============================================================

setlocal enabledelayedexpansion
set "PROJECT=%CD%"

if "%~1"=="" (
  echo [cc-test] usage: cc-test.bat ^<testFilter^>
  exit /b 1
)
set "FILTER=%~1"

if not defined UNITY_PATH (
  echo [cc-test] UNITY_PATH not set. Point it at Unity.exe and retry.
  exit /b 1
)
if not exist "%UNITY_PATH%" (
  echo [cc-test] UNITY_PATH does not exist: %UNITY_PATH%
  exit /b 1
)

if not exist "%PROJECT%\Logs" mkdir "%PROJECT%\Logs"

REM ----- Unity project-lock guard (same as cc-verify.bat step 0) -------------
set "LOCKFILE=%PROJECT%\Temp\UnityLockfile"
if exist "%LOCKFILE%" (
  powershell -NoProfile -Command "try { $fs = [System.IO.File]::Open('%LOCKFILE%', 'Open', 'ReadWrite', 'None'); $fs.Close(); exit 0 } catch { exit 1 }"
  if errorlevel 1 (
    echo === cc-test ABORTED - another Unity instance has this project open ===
    echo     Nothing was run. This is NOT a test failure.
    exit /b 1
  ) else (
    echo [cc-test] stale UnityLockfile found, no live Unity holds it - continuing.
  )
)

set "RESULTS=%PROJECT%\Logs\editmode-filtered-results.xml"
if exist "%RESULTS%" del /f /q "%RESULTS%"

echo [cc-test] filtered EditMode run: %FILTER% ...
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -runTests -testPlatform EditMode -testFilter "%FILTER%" -testResults "%RESULTS%" -logFile "%PROJECT%\Logs\editmode-filtered.log"
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
  echo        FAIL ^(exit %RC%^) — see Logs\editmode-filtered.log
  exit /b 1
)

if not exist "%RESULTS%" (
  echo        FAIL — %RESULTS% was not written
  exit /b 1
)

powershell -NoProfile -Command "try { $x = [xml](Get-Content -LiteralPath '%RESULTS%' -Raw); Write-Output ($x.'test-run'.total + ',' + $x.'test-run'.passed + ',' + $x.'test-run'.failed) } catch { Write-Output '-1,-1,-1' }" > "%PROJECT%\Logs\editmode-filtered-count.txt"
set "COUNTS="
set /p COUNTS=<"%PROJECT%\Logs\editmode-filtered-count.txt"

for /f "tokens=1,2,3 delims=," %%A in ("%COUNTS%") do (
  set "TOTAL=%%A"
  set "PASSED=%%B"
  set "FAILED=%%C"
)

if "%TOTAL%"=="" (
  echo        FAIL — could not read total from %RESULTS%
  exit /b 1
)
if "%TOTAL%"=="-1" (
  echo        FAIL — could not read total from %RESULTS%
  exit /b 1
)

echo total=%TOTAL% passed=%PASSED% failed=%FAILED%

if "%TOTAL%"=="0" (
  exit /b 2
)
if not "%FAILED%"=="0" (
  exit /b 1
)

exit /b 0
