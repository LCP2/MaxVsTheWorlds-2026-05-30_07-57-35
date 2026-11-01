@echo off
setlocal
set "PROJECT=%~dp0"
set "PROJECT=%PROJECT:~0,-1%"
if exist "%PROJECT%\Logs\mv1334-capture.log" del /f /q "%PROJECT%\Logs\mv1334-capture.log"
start "" /min /wait "%PROJECT%\Builds\cc-verify\MaxVsTheWorlds.exe" -batchmode -mv1334shot -logFile "%PROJECT%\Logs\mv1334-capture.log"
echo EXIT: %ERRORLEVEL%
