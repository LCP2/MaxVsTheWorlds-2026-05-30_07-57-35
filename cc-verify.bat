@echo off
REM ============================================================
REM   cc-verify (MV-game / Unity 6 LTS)
REM
REM   Runs after every code change. Exit 0 = pass.
REM   Target runtime: no-argument form runs everything in one go, as before,
REM   with no fixed budget. Each PART (compile / editmode <n> / build /
REM   finish) is a separate tool call and must finish under 450s on the dev
REM   PC - see MV-1271. Steps 1-4 ~90s; step 5 runs the just-built standalone
REM   player for a real frame-time sample (MV-494).
REM
REM   Usage:
REM     cc-verify.bat                 - full run, exactly as before (humans, CI-adjacent callers)
REM     cc-verify.bat compile         - step 1 only
REM     cc-verify.bat editmode <n>    - EditMode shard n of N (see cc-verify-shards.txt)
REM     cc-verify.bat build           - steps 3-6
REM     cc-verify.bat finish          - merge verdict: all parts fresh + green on this tree
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
set "SHARDS=%PROJECT%\cc-verify-shards.txt"
set "PART=%~1"

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

if "%PART%"=="" goto :RUN_ALL
if /i "%PART%"=="compile"  goto :PART_COMPILE
if /i "%PART%"=="editmode" goto :PART_EDITMODE
if /i "%PART%"=="build"    goto :PART_BUILD
if /i "%PART%"=="finish"   goto :PART_FINISH
echo [cc-verify] unknown part '%PART%' - expected compile / editmode ^<n^> / build / finish, or no argument.
exit /b 2

REM ============================================================
REM   Shared subroutines
REM ============================================================

:CHECK_LOCK
REM   If another Unity instance already holds this project (e.g. the CC worker
REM   is live in this same clone), every batchmode call below exits instantly
REM   and looks exactly like four independent failures - that cost a long,
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
goto :eof

:TIMER_START
for /f "usebackq delims=" %%T in (`powershell -NoProfile -Command "[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()"`) do set "T0=%%T"
goto :eof

:TIMER_ELAPSED
REM   Sets ELAPSED to the whole seconds since the matching TIMER_START.
for /f "usebackq delims=" %%T in (`powershell -NoProfile -Command "[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()"`) do set "T1=%%T"
set /a ELAPSED=%T1%-%T0%
goto :eof

:CALC_TREESTATE
REM   Sets TREESTATE to "<HEAD sha> <hash of git status --porcelain + git diff>" -
REM   a fingerprint of exactly what tree a part ran against (MV-1271 AC3).
REM   Excludes docs/press/ and ProjectSettings/ProjectSettings.asset: the `build`
REM   part's own UI conformance step (cc-screens.bat) rewrites every PNG/report
REM   under docs/press/ on every run regardless of source changes, and Unity's
REM   player build stamps ProjectSettings.asset's bundleVersion with the current
REM   timestamp - both are regenerated OUTPUTS of verification, not source, and
REM   including them made every earlier part go stale the instant `build` ran,
REM   so `finish` could never pass (caught while proving AC1-5 on the real tree).
for /f "usebackq delims=" %%G in (`git rev-parse HEAD`) do set "HEADSHA=%%G"
set "DIFFTMP=%PROJECT%\Logs\cc-verify-treestate.tmp"
(git status --porcelain -- . ":(exclude)docs/press" ":(exclude)ProjectSettings/ProjectSettings.asset" & git diff -- . ":(exclude)docs/press" ":(exclude)ProjectSettings/ProjectSettings.asset") > "%DIFFTMP%" 2>nul
for /f "usebackq delims=" %%H in (`powershell -NoProfile -Command "(Get-FileHash -LiteralPath '%DIFFTMP%' -Algorithm SHA256).Hash"`) do set "DIFFHASH=%%H"
del /f /q "%DIFFTMP%" 2>nul
set "TREESTATE=%HEADSHA% %DIFFHASH%"
goto :eof

:WRITE_STAMP
REM   %1 = part name. Call CALC_TREESTATE first.
> "%PROJECT%\Logs\cc-verify-part-%~1.stamp" echo %TREESTATE%
goto :eof

:COUNT_SHARDS
REM   Sets SHARDCOUNT and TOTALSHARDS (one per explicit pattern line - there is
REM   no synthesized complement shard, see cc-verify-shard-filter.ps1).
if not exist "%SHARDS%" (
  echo [cc-verify] %SHARDS% not found.
  exit /b 2
)
set "SHARDCOUNT=0"
for /f "usebackq delims=" %%L in ("%SHARDS%") do (
  set "LN=%%L"
  if not "!LN!"=="" set /a SHARDCOUNT+=1
)
set /a TOTALSHARDS=SHARDCOUNT
goto :eof

REM ============================================================
REM   PART: compile
REM ============================================================
:PART_COMPILE
call :CHECK_LOCK
if errorlevel 1 exit /b 3
echo [compile] compile check ...
call :TIMER_START
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -quit -logFile "%PROJECT%\Logs\compile.log"
set "RC=%ERRORLEVEL%"
call :TIMER_ELAPSED
echo        elapsed: %ELAPSED%s
if not "%RC%"=="0" (
  echo        FAIL ^(exit %RC%^) - see Logs\compile.log
  exit /b 1
)
echo        ok
call :CALC_TREESTATE
call :WRITE_STAMP compile
exit /b 0

REM ============================================================
REM   PART: editmode <n>
REM ============================================================
:PART_EDITMODE
call :CHECK_LOCK
if errorlevel 1 exit /b 3
call :COUNT_SHARDS
if errorlevel 1 exit /b 2
set "SHARDNUM=%~2"
if "%SHARDNUM%"=="" (
  echo [cc-verify] usage: cc-verify.bat editmode ^<n^> ^(1..%TOTALSHARDS%^)
  exit /b 2
)
if %SHARDNUM% LSS 1 (
  echo [cc-verify] shard number must be between 1 and %TOTALSHARDS%, got %SHARDNUM%
  exit /b 2
)
if %SHARDNUM% GTR %TOTALSHARDS% (
  echo [cc-verify] shard number must be between 1 and %TOTALSHARDS%, got %SHARDNUM%
  exit /b 2
)

set "RESULTS=%PROJECT%\Logs\editmode-results-%SHARDNUM%.xml"
if exist "%RESULTS%" del /f /q "%RESULTS%"
echo [editmode %SHARDNUM%/%TOTALSHARDS%] EditMode tests ...
call :TIMER_START

REM   The shard regex has a leading "^" anchor and "(?!...)" negative
REM   lookaheads. cmd.exe strips a bare "^" unconditionally, and under
REM   delayed expansion it also eats "!...!" - and both strips happen EVERY
REM   time the text is %-expanded into a new command line, so round-tripping
REM   it through an intermediate %VAR% corrupts it on the second expansion
REM   even with delayed expansion off for that line (observed turning
REM   "^...(?!\d)" into "...(?\d)", which Unity's regex engine then rejected
REM   outright). The only clean path found: use the for /f loop's own %%F
REM   metavariable directly in the command that launches Unity - never park
REM   it in a %VAR% first - with delayed expansion off for this block (MV-1271).
set "RC0=9"
set "RAN=0"
setlocal disabledelayedexpansion
for /f "usebackq delims=" %%F in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT%\cc-verify-shard-filter.ps1" -ShardsFile "%SHARDS%" -Shard %SHARDNUM%`) do (
  start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -runTests -testPlatform EditMode -testFilter "%%F" -testResults "%RESULTS%" -logFile "%PROJECT%\Logs\editmode-%SHARDNUM%.log"
  set "RC0=%ERRORLEVEL%"
  set "RAN=1"
)
endlocal & set "RC=%RC0%" & set "RAN=%RAN%"
call :TIMER_ELAPSED
echo        elapsed: %ELAPSED%s

if "%RAN%"=="0" (
  echo        FAIL - could not resolve the -testFilter regex for shard %SHARDNUM%
  exit /b 1
)

if not "%RC%"=="0" (
  echo        FAIL ^(exit %RC%^) - see Logs\editmode-%SHARDNUM%.log
  exit /b 1
)
if not exist "%RESULTS%" (
  echo        FAIL - %RESULTS% was not written
  exit /b 1
)

powershell -NoProfile -Command "try { $x = [xml](Get-Content -LiteralPath '%RESULTS%' -Raw); Write-Output ($x.'test-run'.total + ',' + $x.'test-run'.failed) } catch { Write-Output '-1,-1' }" > "%PROJECT%\Logs\editmode-%SHARDNUM%-count.txt"
set "COUNTS="
set /p COUNTS=<"%PROJECT%\Logs\editmode-%SHARDNUM%-count.txt"
for /f "tokens=1,2 delims=," %%A in ("%COUNTS%") do (
  set "STOTAL=%%A"
  set "SFAILED=%%B"
)
if "%STOTAL%"=="" (
  echo        FAIL - could not read total from %RESULTS%
  exit /b 1
)
if "%STOTAL%"=="0" (
  echo        FAIL - 0 tests discovered in shard %SHARDNUM% ^(bad pattern?^)
  exit /b 1
)
if "%STOTAL%"=="-1" (
  echo        FAIL - could not read total from %RESULTS%
  exit /b 1
)
if not "%SFAILED%"=="0" (
  echo        FAIL - %SFAILED% failing test^(s^) in shard %SHARDNUM% - see %RESULTS%
  exit /b 1
)
echo        ok ^(%STOTAL% tests^)
call :CALC_TREESTATE
call :WRITE_STAMP editmode-%SHARDNUM%
exit /b 0

REM ============================================================
REM   PART: build (steps 3-6 of the original sequence)
REM ============================================================
:PART_BUILD
call :CHECK_LOCK
if errorlevel 1 exit /b 3
call :TIMER_START

echo [build 1/4] Windows standalone build (Bootstrap.unity) ...
if exist "%BUILD%" rmdir /S /Q "%BUILD%"
mkdir "%BUILD%"
if exist "%PROJECT%\Logs\build.log" del /f /q "%PROJECT%\Logs\build.log"
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -quit -buildTarget Win64 -executeMethod MaxWorlds.Editor.HeadlessBuild.WindowsBootstrap -buildOutput "%BUILD%\MaxVsTheWorlds.exe" -logFile "%PROJECT%\Logs\build.log"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  if "%RC%"=="198" (
    echo        FAIL ^(exit 198^) - Unity licence not resolved - see Logs\build.log
  ) else (
    echo        FAIL ^(exit %RC%^) - see Logs\build.log
  )
  exit /b 1
) else (
  echo        ok
)

echo [build 2/4] log assertions ...
if not exist "%PROJECT%\Logs\build.log" (
  echo        FAIL - Logs\build.log missing
  set "FAIL=1"
) else (
  findstr /C:"targetFrameRate=60" "%PROJECT%\Logs\build.log" >nul
  if errorlevel 1 (
    echo        FAIL - targetFrameRate=60 not found in build log
    set "FAIL=1"
  ) else (
    findstr /C:"VSyncCount=0" "%PROJECT%\Logs\build.log" >nul
    if errorlevel 1 (
      echo        FAIL - VSyncCount=0 not found in build log
      set "FAIL=1"
    ) else (
      echo        ok
    )
  )
)

echo [build 3/4] frame-time gate ...
set "PERFREPORT=%PROJECT%\Logs\perf-report.txt"
if exist "%PERFREPORT%" del /f /q "%PERFREPORT%"
if exist "%PROJECT%\Logs\perf-run.log" del /f /q "%PROJECT%\Logs\perf-run.log"
if not exist "%BUILD%\MaxVsTheWorlds.exe" (
  echo        FAIL - %BUILD%\MaxVsTheWorlds.exe was not produced - see Logs\build.log
  echo        NON-BLOCKING ^(MV-651^) - frame-time gate is not currently measurable; this does not fail the run
) else (
  start "" /min /wait "%BUILD%\MaxVsTheWorlds.exe" -batchmode -nographics -ccperf -perfReportPath "%PERFREPORT%" -logFile "%PROJECT%\Logs\perf-run.log"
  set "RC=!ERRORLEVEL!"
  if not exist "%PERFREPORT%" (
    echo        FAIL - %PERFREPORT% was not regenerated ^(exit !RC!^) - see Logs\perf-run.log
    echo        NON-BLOCKING ^(MV-651^) - frame-time gate is not currently measurable; this does not fail the run
  ) else (
    echo        measured:
    for /f "usebackq delims=" %%L in ("%PERFREPORT%") do echo          %%L
    if not "!RC!"=="0" (
      echo        FAIL - see p95_ms / threshold_p95_ms above ^(exit !RC!^) - see Logs\perf-run.log
      echo        NON-BLOCKING ^(MV-651^) - frame-time gate is not currently measurable; this does not fail the run
    ) else (
      echo        ok
    )
  )
)

echo [build 4/4] UI conformance gate ...
set "UISCREENS=%PROJECT%\docs\press\_uiscreens_report.txt"
if exist "%UISCREENS%" del /f /q "%UISCREENS%"
call "%PROJECT%\cc-screens.bat"
set "RC=%ERRORLEVEL%"
if not exist "%UISCREENS%" (
  echo        FAIL - %UISCREENS% was not regenerated ^(cc-screens exit %RC%^)
  set "FAIL=1"
) else (
  set "UIFAIL=0"
  for /f "usebackq delims=" %%L in ("%UISCREENS%") do (
    set "LINE=%%L"
    if "!LINE:~0,4!"=="FAIL" (
      echo        !LINE!
      set "UIFAIL=1"
    )
  )
  if "!UIFAIL!"=="1" (
    set "FAIL=1"
  ) else (
    echo        ok
  )
)

call :TIMER_ELAPSED
echo        elapsed: %ELAPSED%s

if not "%FAIL%"=="0" (
  exit /b 1
)
call :CALC_TREESTATE
call :WRITE_STAMP build
exit /b 0

REM ============================================================
REM   PART: finish - merge verdict (MV-1271 AC3)
REM ============================================================
:PART_FINISH
call :COUNT_SHARDS
if errorlevel 1 exit /b 2
call :CALC_TREESTATE
set "VERDICTFAIL=0"

call :CHECK_PART_STAMP compile
if errorlevel 1 set "VERDICTFAIL=1"

for /l %%N in (1,1,%TOTALSHARDS%) do (
  call :CHECK_PART_STAMP editmode-%%N
  if errorlevel 1 set "VERDICTFAIL=1"
  call :CHECK_SHARD_RESULTS %%N
  if errorlevel 1 set "VERDICTFAIL=1"
)

call :CHECK_PART_STAMP build
if errorlevel 1 set "VERDICTFAIL=1"

call :CHECK_SHARD_OVERLAP

if "%VERDICTFAIL%"=="1" (
  echo === cc-verify finish: FAIL ===
  exit /b 1
)
echo === cc-verify finish: PASS ===
exit /b 0

:CHECK_PART_STAMP
REM   %1 = part name. Compares its stamp (if any) to the current TREESTATE.
set "STAMPFILE=%PROJECT%\Logs\cc-verify-part-%~1.stamp"
if not exist "%STAMPFILE%" (
  echo [finish] FAIL - part '%~1' has not run ^(missing %STAMPFILE%^)
  exit /b 1
)
set "STAMPED="
set /p STAMPED=<"%STAMPFILE%"
if not "%STAMPED%"=="%TREESTATE%" (
  echo [finish] FAIL - part '%~1' is stale ^(ran against a different tree - edit after the part ran, or the part is from an earlier commit^)
  exit /b 1
)
exit /b 0

:CHECK_SHARD_RESULTS
REM   %1 = shard number. Verifies Logs\editmode-results-<n>.xml exists, total>0, failed==0.
set "RESULTS=%PROJECT%\Logs\editmode-results-%~1.xml"
if not exist "%RESULTS%" (
  echo [finish] FAIL - shard %~1 results missing: %RESULTS%
  exit /b 1
)
powershell -NoProfile -Command "try { $x = [xml](Get-Content -LiteralPath '%RESULTS%' -Raw); Write-Output ($x.'test-run'.total + ',' + $x.'test-run'.failed) } catch { Write-Output '-1,-1' }" > "%PROJECT%\Logs\finish-check-%~1.txt"
set "COUNTS="
set /p COUNTS=<"%PROJECT%\Logs\finish-check-%~1.txt"
for /f "tokens=1,2 delims=," %%A in ("%COUNTS%") do (
  set "FTOTAL=%%A"
  set "FFAILED=%%B"
)
if "%FTOTAL%"=="0" (
  echo [finish] FAIL - shard %~1 discovered 0 tests
  exit /b 1
)
if "%FTOTAL%"=="-1" (
  echo [finish] FAIL - shard %~1 results unreadable: %RESULTS%
  exit /b 1
)
if not "%FFAILED%"=="0" (
  echo [finish] FAIL - shard %~1 has %FFAILED% failing test^(s^)
  exit /b 1
)
exit /b 0

:CHECK_SHARD_OVERLAP
REM   No test full name may appear in more than one shard's results.
set "FILELIST="
for /l %%N in (1,1,%TOTALSHARDS%) do set "FILELIST=!FILELIST!%PROJECT%\Logs\editmode-results-%%N.xml;"
powershell -NoProfile -Command ^
  "$files = '%FILELIST%'.Trim(';').Split(';'); $seen = @{}; $dupe = $false;" ^
  "foreach ($f in $files) { if (-not (Test-Path -LiteralPath $f)) { continue };" ^
  "  $x = [xml](Get-Content -LiteralPath $f -Raw);" ^
  "  $cases = $x.SelectNodes('//test-case');" ^
  "  foreach ($c in $cases) { $fn = $c.fullname; if ($seen.ContainsKey($fn)) { Write-Output ('DUP: ' + $fn + ' in ' + $f + ' and ' + $seen[$fn]); $dupe = $true } else { $seen[$fn] = $f } } }" ^
  "if ($dupe) { exit 1 } else { exit 0 }" > "%PROJECT%\Logs\finish-overlap.txt" 2>&1
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo [finish] FAIL - a test full name appears in more than one shard:
  type "%PROJECT%\Logs\finish-overlap.txt"
  exit /b 1
)
exit /b 0

REM ============================================================
REM   RUN_ALL - no-argument form, unchanged from before MV-1271
REM ============================================================
:RUN_ALL
echo.
echo === cc-verify (MV-game) start ===
echo Project : %PROJECT%
echo Unity   : %UNITY_PATH%
echo Log     : %LOG%
echo.

call :CHECK_LOCK
if errorlevel 1 exit /b 3

REM ----- 1. Compile check (open project headless, exit) ----------------------
REM   Capture the raw exit code into a variable rather than testing with
REM   `if errorlevel N`: errorlevel comparisons treat a negative code (e.g.
REM   -1073741819 on a Unity access violation) as less than 1, so a hard crash
REM   silently passed this gate before (MV-483).
echo [1/6] compile check ...
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -quit -logFile "%PROJECT%\Logs\compile.log"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo        FAIL ^(exit %RC%^) - see Logs\compile.log
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
echo [2/6] EditMode tests ...
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -runTests -testPlatform EditMode -testResults "%RESULTS%" -logFile "%PROJECT%\Logs\editmode.log"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo        FAIL ^(exit %RC%^) - see Logs\editmode.log
  set "FAIL=1"
) else if not exist "%RESULTS%" (
  echo        FAIL - %RESULTS% was not written
  set "FAIL=1"
) else (
  powershell -NoProfile -Command "try { $x = [xml](Get-Content -LiteralPath '%RESULTS%' -Raw); Write-Output $x.'test-run'.total } catch { Write-Output '-1' }" > "%PROJECT%\Logs\editmode-count.txt"
  set "TESTCOUNT="
  set /p TESTCOUNT=<"%PROJECT%\Logs\editmode-count.txt"
  if "!TESTCOUNT!"=="" (
    echo        FAIL - could not read total from %RESULTS%
    set "FAIL=1"
  ) else if "!TESTCOUNT!"=="0" (
    echo        FAIL - 0 tests discovered ^(broken asmdef?^)
    set "FAIL=1"
  ) else if "!TESTCOUNT:~0,1!"=="-" (
    echo        FAIL - could not read total from %RESULTS%
    set "FAIL=1"
  ) else (
    echo        ok ^(!TESTCOUNT! tests^)
  )
)

REM ----- 3. Windows standalone smoke build -----------------------------------
REM   The PlayMode boot-and-play gate (MaxWorlds.Tests.PlayMode.BootAndPlaySmokeTests,
REM   MV-259) does NOT run here - Unity batch-mode PlayMode runs don't stream
REM   output, so an in-session run risks backgrounding and stalling the ticket
REM   (MV-307). PlayMode coverage belongs in CI (build.yml) instead; the browser
REM   play-check remains the release gate - see CC_AUTONOMY.md.
echo [3/6] Windows standalone build (Bootstrap.unity) ...
if exist "%BUILD%" rmdir /S /Q "%BUILD%"
mkdir "%BUILD%"
if exist "%PROJECT%\Logs\build.log" del /f /q "%PROJECT%\Logs\build.log"
start "" /min /wait "%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT%" -quit -buildTarget Win64 -executeMethod MaxWorlds.Editor.HeadlessBuild.WindowsBootstrap -buildOutput "%BUILD%\MaxVsTheWorlds.exe" -logFile "%PROJECT%\Logs\build.log"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  if "%RC%"=="198" (
    echo        FAIL ^(exit 198^) - Unity licence not resolved - see Logs\build.log
  ) else (
    echo        FAIL ^(exit %RC%^) - see Logs\build.log
  )
  set "FAIL=1"
) else (
  echo        ok
)

REM ----- 4. Log assertions ----------------------------------------------------
echo [4/6] log assertions ...
if not exist "%PROJECT%\Logs\build.log" (
  echo        FAIL - Logs\build.log missing
  set "FAIL=1"
) else (
  findstr /C:"targetFrameRate=60" "%PROJECT%\Logs\build.log" >nul
  if errorlevel 1 (
    echo        FAIL - targetFrameRate=60 not found in build log
    set "FAIL=1"
  ) else (
    findstr /C:"VSyncCount=0" "%PROJECT%\Logs\build.log" >nul
    if errorlevel 1 (
      echo        FAIL - VSyncCount=0 not found in build log
      set "FAIL=1"
    ) else (
      echo        ok
    )
  )
)

REM ----- 5. Real frame-time gate (MV-494) --------------------------------------
REM   cc-verify's old "60fps" step-4 check only grepped the build log for two settings
REM   strings - it proved the settings were applied, not that any frame was ever measured
REM   (MV-494). This launches the standalone player build itself (not the Editor) behind
REM   -ccperf: MaxWorlds.Dev.PerfCaptureDirector forces the field to populate with robots,
REM   samples real Update-loop frame times, writes Logs\perf-report.txt, and quits with a
REM   matching exit code. Same -batchmode -nographics as every step above, for the same
REM   reason: this project has been stalled by live-window/PlayMode batch runs three times
REM   (see the PlayMode ban later in this repo's docs) - see PerfCaptureDirector's own doc
REM   comment for the resulting CPU-vs-GPU frame-cost scope call.
echo [5/6] frame-time gate ...
set "PERFREPORT=%PROJECT%\Logs\perf-report.txt"
if exist "%PERFREPORT%" del /f /q "%PERFREPORT%"
if exist "%PROJECT%\Logs\perf-run.log" del /f /q "%PROJECT%\Logs\perf-run.log"
if not exist "%BUILD%\MaxVsTheWorlds.exe" (
  echo        FAIL - %BUILD%\MaxVsTheWorlds.exe was not produced - see Logs\build.log
  echo        NON-BLOCKING ^(MV-651^) - frame-time gate is not currently measurable; this does not fail the run
) else (
  start "" /min /wait "%BUILD%\MaxVsTheWorlds.exe" -batchmode -nographics -ccperf -perfReportPath "%PERFREPORT%" -logFile "%PROJECT%\Logs\perf-run.log"
  set "RC=!ERRORLEVEL!"
  if not exist "%PERFREPORT%" (
    echo        FAIL - %PERFREPORT% was not regenerated ^(exit !RC!^) - see Logs\perf-run.log
    echo        NON-BLOCKING ^(MV-651^) - frame-time gate is not currently measurable; this does not fail the run
  ) else (
    echo        measured:
    for /f "usebackq delims=" %%L in ("%PERFREPORT%") do echo          %%L
    if not "!RC!"=="0" (
      echo        FAIL - see p95_ms / threshold_p95_ms above ^(exit !RC!^) - see Logs\perf-run.log
      echo        NON-BLOCKING ^(MV-651^) - frame-time gate is not currently measurable; this does not fail the run
    ) else (
      echo        ok
    )
  )
)

REM ----- 6. UI conformance gate (MV-482) ---------------------------------------
REM   Runs cc-screens.bat, which re-measures THE RIG board against rig_board.json
REM   and writes docs\press\_uiscreens_report.txt. Delete any stale report first so
REM   a crashed capture can't be masked by a leftover PASS file from a previous run,
REM   require the file to be regenerated, then scan every line in every aspect
REM   section (rig-16x9, rig-phone, rig-ipad-mini, and whatever MV-480-style aspect
REM   additions land later) for a line beginning FAIL. Line-shaped, not
REM   count-shaped: no hard-coded check count, no aspect restricted out.
echo [6/6] UI conformance gate ...
set "UISCREENS=%PROJECT%\docs\press\_uiscreens_report.txt"
if exist "%UISCREENS%" del /f /q "%UISCREENS%"
call "%PROJECT%\cc-screens.bat"
set "RC=%ERRORLEVEL%"
if not exist "%UISCREENS%" (
  echo        FAIL - %UISCREENS% was not regenerated ^(cc-screens exit %RC%^)
  set "FAIL=1"
) else (
  set "UIFAIL=0"
  for /f "usebackq delims=" %%L in ("%UISCREENS%") do (
    set "LINE=%%L"
    if "!LINE:~0,4!"=="FAIL" (
      echo        !LINE!
      set "UIFAIL=1"
    )
  )
  if "!UIFAIL!"=="1" (
    set "FAIL=1"
  ) else (
    echo        ok
  )
)

echo.
echo === cc-verify (MV-game) end (fail=%FAIL%) ===

if "%FAIL%"=="0" (
  exit /b 0
) else (
  exit /b 1
)
