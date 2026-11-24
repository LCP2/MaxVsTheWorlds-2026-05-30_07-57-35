@echo off
setlocal
set "PROJECT=%~dp0"
set "PROJECT=%PROJECT:~0,-1%"
cd /d "%PROJECT%"
if exist "%PROJECT%\Logs\mv1436-capture.log" del /f /q "%PROJECT%\Logs\mv1436-capture.log"
start "" /min /wait "%PROJECT%\Builds\cc-verify\MaxVsTheWorlds.exe" -batchmode -mv1436shot -logFile "%PROJECT%\Logs\mv1436-capture.log"
echo EXIT: %ERRORLEVEL%
