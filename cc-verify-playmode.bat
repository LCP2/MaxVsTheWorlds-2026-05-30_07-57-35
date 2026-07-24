@echo off
setlocal
set "PROJECT=%CD%"
if not defined UNITY_PATH set "UNITY_PATH=C:\Program Files\Unity\Hub\Editor\6000.4.9f1\Editor\Unity.exe"

echo Running PlayMode tests...
"%UNITY_PATH%" ^
  -batchmode -nographics -projectPath "%PROJECT%" ^
  -runTests -testPlatform PlayMode ^
  -testResults "%PROJECT%\Logs\playmode-yt191-results.xml" ^
  -logFile "%PROJECT%\Logs\playmode-yt191.log"

echo Unity exit code: %ERRORLEVEL%
exit /b %ERRORLEVEL%
