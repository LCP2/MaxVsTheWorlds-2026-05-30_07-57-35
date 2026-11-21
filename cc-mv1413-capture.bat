@echo off
setlocal
set "PROJECT=%~dp0"
set "PROJECT=%PROJECT:~0,-1%"
cd /d "%PROJECT%"
if exist "%PROJECT%\Logs\mv1413-capture.log" del /f /q "%PROJECT%\Logs\mv1413-capture.log"
start "" /min /wait "%PROJECT%\Builds\cc-verify\MaxVsTheWorlds.exe" -batchmode -mv1413shot -logFile "%PROJECT%\Logs\mv1413-capture.log"
echo EXIT: %ERRORLEVEL%
