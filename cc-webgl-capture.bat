@echo off
REM ============================================================
REM   cc-webgl-capture (MV-1376) - one-call local WebGL frame-stability capture.
REM   Usage: cc-webgl-capture.bat <world> <area> <frameGap> [crop x,y,w,h | full] [outDir] [timeoutSec]
REM     e.g. cc-webgl-capture.bat 4 a1 10
REM   Serves Builds\cc-webgl, boots headless Edge at ?hud=0&world=N&area=aK, waits for the in-build
REM   "[MV-1376] READY" line, takes two screenshots <frameGap> rAF frames apart, prints one RESULT: line.
REM   Exit: 0 ok | 1 bad args / missing build or tools | 3 timeout or no READY | 4 browser failure | 5 diff failure
REM   Self-terminating (internal timeout, default 240s). Never backgrounds.
REM ============================================================
setlocal
set "HERE=%~dp0"
set "WORLD=%~1"
set "AREA=%~2"
set "GAP=%~3"
set "CROP=%~4"
set "OUT=%~5"
set "TMO=%~6"
if "%WORLD%"=="" goto usage
if "%AREA%"=="" goto usage
if "%GAP%"=="" goto usage
if "%CROP%"=="" set "CROP=full"
if "%OUT%"=="" set "OUT=%HERE%Logs\webgl-capture"
if "%TMO%"=="" set "TMO=240"
set "BUILD=%HERE%Builds\cc-webgl"
if not exist "%BUILD%\index.html" (
  echo FAIL: WebGL build not found at %BUILD%\index.html - build it first with HeadlessBuild.WebGLBootstrap -buildOutput Builds\cc-webgl
  exit /b 1
)
where node >nul 2>&1 || (echo FAIL: node not on PATH& exit /b 1)
where python >nul 2>&1 || (echo FAIL: python not on PATH& exit /b 1)
node "%HERE%Tools\WebGLCapture\capture.mjs" "%BUILD%" "%WORLD%" "%AREA%" "%GAP%" "%OUT%" "%TMO%"
if errorlevel 1 exit /b %errorlevel%
python -I "%HERE%Tools\WebGLCapture\diff.py" "%OUT%\capture_a.png" "%OUT%\capture_b.png" "%CROP%"
exit /b %errorlevel%

:usage
echo FAIL: usage: cc-webgl-capture.bat ^<world^> ^<area^> ^<frameGap^> [crop x,y,w,h ^| full] [outDir] [timeoutSec]
exit /b 1
