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
REM   Every Unity call goes through `start "" /min /wait` on purpose:
REM   a bare batchmode launch pops a console window that STEALS FOCUS from
REM   whatever Lee is typing in. /min = SW_SHOWMINNOACTIVE (no activation).
REM   Keep it. Same rule for any ad-hoc Unity -batchmode run.
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

REM ----- 0. Unity project-lock guard (MV-465) ---------------------------------
REM   If another Unity instance already holds this project (e.g. the CC worker
REM   is live in this same clone), every batchmode call below exits instantly
REM   and looks exactly like four independent failures — that cost a long,
REM   wrong investigation once (see MV-465 comment). Detect the live lock and
REM   abort with a distinct message/exit code instead of reporting false FAILs.
REM   A stale lockfile (process died, file left behind) must NOT block forever,
REM   so the check is "can it be opened exclusively", not just "does it exist".
set "LOCKFILE=%PROJECT%\Temp\UnityLockfile"
if exist "%LOCKFILE%" (
  powershell -NoProfile -Command "try { $fs = [System.IO.File]::Open('%LOCKFILE%', 'Open', 'ReadWrite', 'None'); $fs.Close(); exit 0 } catch { exit 1 }"
  if errorlevel 1 (
    echo === cc-verify ABORTED - another Unity instance has this project open ===
    echo     Nothing was verified. This is NOT a test failure.
    exit /b 3
  ) else (
    echo [cc-verify] stale UnityLockfile found, no live Unity holds it - continuing.
  )
)

REM ----- 1. Compile check (open project headless, exit) ----------------------
echo [1/4] compile check ...
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -quit -logFile "%PROJECT%\Logs\compile.log"
if errorlevel 1 (
  echo        FAIL — see Logs\compile.log
  set "FAIL=1"
) else (
  echo        ok
)

REM ----- 2. EditMode tests ----------------------------------------------------
echo [2/4] EditMode tests ...
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -runTests -testPlatform EditMode -testResults "%PROJECT%\Logs\editmode-results.xml" -logFile "%PROJECT%\Logs\editmode.log"
if errorlevel 1 (
  echo        FAIL — see Logs\editmode.log
  set "FAIL=1"
) else (
  echo        ok
)

REM ----- 3. Windows standalone smoke build -----------------------------------
REM   The PlayMode boot-and-play gate (MaxWorlds.Tests.PlayMode.BootAndPlaySmokeTests,
REM   MV-259) does NOT run here — Unity batch-mode PlayMode runs don't stream
REM   output, so an in-session run risks backgrounding and stalling the ticket
REM   (MV-307). PlayMode coverage belongs in CI (build.yml) instead; the browser
REM   play-check remains the release gate — see CC_AUTONOMY.md.
echo [3/4] Windows standalone build (Bootstrap.unity) ...
if exist "%BUILD%" rmdir /S /Q "%BUILD%"
mkdir "%BUILD%"
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -quit -buildTarget Win64 -executeMethod MaxWorlds.Editor.HeadlessBuild.WindowsBootstrap -buildOutput "%BUILD%\MaxVsTheWorlds.exe" -logFile "%PROJECT%\Logs\build.log"
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
echo === cc-verify (MV-game) end (fail=%FAIL%) ===

if "%FAIL%"=="0" (
  exit /b 0
) else (
  exit /b 1
)
