@echo off
REM ============================================================
REM   cc-max-detail (MV-game / Unity 6 LTS) — MV-453
REM
REM   Captures one 1920x1080 screenshot of Max per compass yaw, at
REM   pitch 64.88 / distance 15.81m, via MaxWorlds.Editor.MaxDetailCapture.
REM   Writes to docs\press\max-detail\ and
REM   C:\Dev\MaxVsTheWorlds-Images\_screens\max-detail\.
REM   Exit 0 = every shot captured. Read _maxdetail_done.txt for detail.
REM
REM   Pre-reqs:
REM     - %UNITY_PATH% env var pointing at Unity.exe (same as cc-verify.bat)
REM     - Run from the repo root.
REM
REM   Deliberately NOT -quit / -nographics: MaxDetailCapture.CaptureAll
REM   enters play mode to film the shots and exits the process itself once
REM   every shot has landed (or it times out) — see its own doc comment.
REM   The capture also needs a live GL context.
REM ============================================================

setlocal enabledelayedexpansion
set "PROJECT=%CD%"
set "LOG=%PROJECT%\Logs\maxdetail-run.log"

if not defined UNITY_PATH (
  echo [cc-max-detail] UNITY_PATH not set. Point it at Unity.exe and retry.
  exit /b 2
)
if not exist "%UNITY_PATH%" (
  echo [cc-max-detail] UNITY_PATH does not exist: %UNITY_PATH%
  exit /b 2
)

if not exist "%PROJECT%\Logs" mkdir "%PROJECT%\Logs"

echo.
echo === cc-max-detail (MV-game) start ===
echo Project : %PROJECT%
echo Unity   : %UNITY_PATH%
echo Log     : %LOG%
echo.

REM ----- Unity project-lock guard (MV-465) -------------------------------
REM   Same exposure as cc-verify.bat/cc-screens.bat: if another Unity instance
REM   already holds this project, the capture would exit instantly and look
REM   like a capture failure rather than "nothing ran". Abort distinctly
REM   instead. A stale lockfile (no live process holding it) must not block
REM   forever.
set "LOCKFILE=%PROJECT%\Temp\UnityLockfile"
if exist "%LOCKFILE%" (
  powershell -NoProfile -Command "try { $fs = [System.IO.File]::Open('%LOCKFILE%', 'Open', 'ReadWrite', 'None'); $fs.Close(); exit 0 } catch { exit 1 }"
  if errorlevel 1 (
    echo === cc-max-detail ABORTED - another Unity instance has this project open ===
    echo     Nothing was captured. This is NOT a capture failure.
    exit /b 3
  ) else (
    echo [cc-max-detail] stale UnityLockfile found, no live Unity holds it - continuing.
  )
)

"%UNITY_PATH%" ^
  -batchmode -projectPath "%PROJECT%" ^
  -executeMethod MaxWorlds.Editor.MaxDetailCapture.CaptureAll ^
  -logFile "%LOG%"

set "RESULT=%errorlevel%"

echo.
echo === cc-max-detail (MV-game) end (exit=%RESULT%) ===

exit /b %RESULT%
