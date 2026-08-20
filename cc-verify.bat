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
REM   Capture the raw exit code into a variable rather than testing with
REM   `if errorlevel N`: errorlevel comparisons treat a negative code (e.g.
REM   -1073741819 on a Unity access violation) as less than 1, so a hard crash
REM   silently passed this gate before (MV-483).
echo [1/4] compile check ...
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -quit -logFile "%PROJECT%\Logs\compile.log"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo        FAIL ^(exit %RC%^) — see Logs\compile.log
  set "FAIL=1"
) else (
  echo        ok
)

REM ----- 2. EditMode tests ----------------------------------------------------
REM   Delete any stale results XML first so a broken asmdef that discovers zero
REM   tests can't be masked by a leftover file from a previous run, then require
REM   the file to be regenerated and its <test-run total="..."> to be nonzero
REM   (MV-483).
set "RESULTS=%PROJECT%\Logs\editmode-results.xml"
if exist "%RESULTS%" del /f /q "%RESULTS%"
echo [2/4] EditMode tests ...
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -runTests -testPlatform EditMode -testResults "%RESULTS%" -logFile "%PROJECT%\Logs\editmode.log"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo        FAIL ^(exit %RC%^) — see Logs\editmode.log
  set "FAIL=1"
) else if not exist "%RESULTS%" (
  echo        FAIL — %RESULTS% was not written
  set "FAIL=1"
) else (
  powershell -NoProfile -Command "try { $x = [xml](Get-Content -LiteralPath '%RESULTS%' -Raw); Write-Output $x.'test-run'.total } catch { Write-Output '-1' }" > "%PROJECT%\Logs\editmode-count.txt"
  set "TESTCOUNT="
  set /p TESTCOUNT=<"%PROJECT%\Logs\editmode-count.txt"
  if "!TESTCOUNT!"=="" (
    echo        FAIL — could not read total from %RESULTS%
    set "FAIL=1"
  ) else if "!TESTCOUNT!"=="0" (
    echo        FAIL — 0 tests discovered ^(broken asmdef?^)
    set "FAIL=1"
  ) else if "!TESTCOUNT:~0,1!"=="-" (
    echo        FAIL — could not read total from %RESULTS%
    set "FAIL=1"
  ) else (
    echo        ok ^(!TESTCOUNT! tests^)
  )
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
if exist "%PROJECT%\Logs\build.log" del /f /q "%PROJECT%\Logs\build.log"
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -quit -buildTarget Win64 -executeMethod MaxWorlds.Editor.HeadlessBuild.WindowsBootstrap -buildOutput "%BUILD%\MaxVsTheWorlds.exe" -logFile "%PROJECT%\Logs\build.log"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo        FAIL ^(exit %RC%^) — see Logs\build.log
  set "FAIL=1"
) else (
  echo        ok
)

REM ----- 4. Log assertions ----------------------------------------------------
echo [4/4] log assertions ...
if not exist "%PROJECT%\Logs\build.log" (
  echo        FAIL — Logs\build.log missing
  set "FAIL=1"
) else (
  findstr /C:"targetFrameRate=60" "%PROJECT%\Logs\build.log" >nul
  if errorlevel 1 (
    echo        FAIL — targetFrameRate=60 not found in build log
    set "FAIL=1"
  ) else (
    findstr /C:"VSyncCount=0" "%PROJECT%\Logs\build.log" >nul
    if errorlevel 1 (
      echo        FAIL — VSyncCount=0 not found in build log
      set "FAIL=1"
    ) else (
      echo        ok
    )
  )
)

echo.
echo === cc-verify (MV-game) end (fail=%FAIL%) ===

if "%FAIL%"=="0" (
  exit /b 0
) else (
  exit /b 1
)
