@echo off
REM ============================================================
REM   cc-screens (MV-game / Unity 6 LTS) — MV-441
REM
REM   Captures the ui-screens evidence PNGs (THE RIG board + the
REM   WEAPONS button alert states) via MaxWorlds.Editor.UiScreensCapture.
REM   Writes to docs\press\ and C:\Dev\MaxVsTheWorlds-Images\_screens\.
REM   Exit 0 = every shot captured, no assertion failures
REM   (canvas-overlay check, probe 6). Read _uiscreens_done.txt for detail.
REM
REM   Pre-reqs:
REM     - %UNITY_PATH% env var pointing at Unity.exe (same as cc-verify.bat)
REM     - Run from the repo root.
REM
REM   Deliberately NOT -quit / -nographics: UiScreensCapture.CaptureAll
REM   enters play mode to film the shots and exits the process itself once
REM   every shot has landed (or it times out) — see its own doc comment.
REM   The capture also needs a live GL context.
REM ============================================================

setlocal enabledelayedexpansion
set "PROJECT=%CD%"
set "LOG=%PROJECT%\Logs\uiscreens-run.log"

if not defined UNITY_PATH (
  echo [cc-screens] UNITY_PATH not set. Point it at Unity.exe and retry.
  exit /b 2
)
if not exist "%UNITY_PATH%" (
  echo [cc-screens] UNITY_PATH does not exist: %UNITY_PATH%
  exit /b 2
)

if not exist "%PROJECT%\Logs" mkdir "%PROJECT%\Logs"

echo.
echo === cc-screens (MV-game) start ===
echo Project : %PROJECT%
echo Unity   : %UNITY_PATH%
echo Log     : %LOG%
echo.

REM ----- Unity project-lock guard (MV-465) -------------------------------
REM   Same exposure as cc-verify.bat: if another Unity instance already holds
REM   this project, the capture would exit instantly and look like a capture
REM   failure rather than "nothing ran". Abort distinctly instead. A stale
REM   lockfile (no live process holding it) must not block forever.
set "LOCKFILE=%PROJECT%\Temp\UnityLockfile"
if exist "%LOCKFILE%" (
  powershell -NoProfile -Command "try { $fs = [System.IO.File]::Open('%LOCKFILE%', 'Open', 'ReadWrite', 'None'); $fs.Close(); exit 0 } catch { exit 1 }"
  if errorlevel 1 (
    echo === cc-screens ABORTED - another Unity instance has this project open ===
    echo     Nothing was captured. This is NOT a capture failure.
    exit /b 3
  ) else (
    echo [cc-screens] stale UnityLockfile found, no live Unity holds it - continuing.
  )
)

"%UNITY_PATH%" ^
  -batchmode -projectPath "%PROJECT%" ^
  -executeMethod MaxWorlds.Editor.UiScreensCapture.CaptureAll ^
  -logFile "%LOG%"

set "RESULT=%errorlevel%"

echo.
echo === cc-screens (MV-game) end (exit=%RESULT%) ===

exit /b %RESULT%
