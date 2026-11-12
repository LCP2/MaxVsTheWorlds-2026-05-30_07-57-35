@echo off
setlocal
set "PROJECT=%~dp0"
set "PROJECT=%PROJECT:~0,-1%"
if exist "%PROJECT%\Logs\mv1345-capture.log" del /f /q "%PROJECT%\Logs\mv1345-capture.log"
start "" /min /wait "%PROJECT%\Builds\cc-verify\MaxVsTheWorlds.exe" -batchmode -mv1345shot -logFile "%PROJECT%\Logs\mv1345-capture.log"
echo EXIT: %ERRORLEVEL%
