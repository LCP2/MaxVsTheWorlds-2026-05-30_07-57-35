@echo off
setlocal
set "PROJECT=%~dp0"
set "PROJECT=%PROJECT:~0,-1%"
cd /d "%PROJECT%"
if exist "%PROJECT%\Logs\mv1419-capture.log" del /f /q "%PROJECT%\Logs\mv1419-capture.log"
start "" /min /wait "%PROJECT%\Builds\cc-verify\MaxVsTheWorlds.exe" -batchmode -mv1419shot -logFile "%PROJECT%\Logs\mv1419-capture.log"
echo EXIT: %ERRORLEVEL%
