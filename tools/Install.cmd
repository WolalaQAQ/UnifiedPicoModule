@echo off
rem Installs UnifiedPicoModule (tongue fork) into VRCFaceTracking. Run it from the unpacked release zip.
rem Works for VRCFaceTracking 5 and 6 (Steam): both load modules from %APPDATA%\VRCFaceTracking\CustomLibs.
rem Your existing UnifiedPicoModule.json (settings) is not touched.
setlocal
set "SRC=%~dp0"
set "DEST=%APPDATA%\VRCFaceTracking\CustomLibs\8322e16d-c38b-42f0-8d35-68f27227b442"

if not exist "%SRC%UnifiedPicoModule.dll" (
    echo UnifiedPicoModule.dll is not next to this file. Unpack the whole zip first.
    goto :fail
)
tasklist /fi "imagename eq VRCFaceTracking.exe" 2>nul | find /i "VRCFaceTracking.exe" >nul
if not errorlevel 1 (
    echo VRCFaceTracking is running. Close it and run this file again.
    goto :fail
)
if not exist "%DEST%" mkdir "%DEST%"
if exist "%DEST%\UnifiedPicoModule.dll" copy /y "%DEST%\UnifiedPicoModule.dll" "%DEST%\UnifiedPicoModule.dll.previous" >nul
copy /y "%SRC%UnifiedPicoModule.dll" "%DEST%\" >nul || goto :copyfail
copy /y "%SRC%module.json" "%DEST%\" >nul || goto :copyfail

echo.
echo Installed to:
echo   %DEST%
echo The previous DLL, if there was one, is kept as UnifiedPicoModule.dll.previous.
echo Start VRCFaceTracking now.
echo.
pause
exit /b 0

:copyfail
echo Could not copy the module to %DEST%
:fail
echo.
pause
exit /b 1
