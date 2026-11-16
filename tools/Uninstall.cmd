@echo off
rem Undoes Install.cmd: puts back the UnifiedPicoModule DLL that was there before (kept as
rem UnifiedPicoModule.dll.previous). If there was none, the module is removed from VRCFaceTracking.
rem Your UnifiedPicoModule.json (settings) is kept either way.
setlocal
set "DEST=%APPDATA%\VRCFaceTracking\CustomLibs\8322e16d-c38b-42f0-8d35-68f27227b442"

if not exist "%DEST%\UnifiedPicoModule.dll" (
    echo UnifiedPicoModule is not installed in:
    echo   %DEST%
    goto :fail
)
tasklist /fi "imagename eq VRCFaceTracking.exe" 2>nul | find /i "VRCFaceTracking.exe" >nul
if not errorlevel 1 (
    echo VRCFaceTracking is running. Close it and run this file again.
    goto :fail
)
if exist "%DEST%\UnifiedPicoModule.dll.previous" (
    copy /y "%DEST%\UnifiedPicoModule.dll.previous" "%DEST%\UnifiedPicoModule.dll" >nul || goto :copyfail
    del "%DEST%\UnifiedPicoModule.dll.previous"
    echo.
    echo The previous UnifiedPicoModule DLL is back. The tongue receiver is removed.
) else (
    del "%DEST%\UnifiedPicoModule.dll" || goto :copyfail
    del "%DEST%\module.json" 2>nul
    echo.
    echo UnifiedPicoModule is removed from VRCFaceTracking. Your settings file was kept.
)
del "%DEST%\tongue-status.json" 2>nul
del "%DEST%\tongue-diagnostics.enabled" 2>nul
echo To remove the headset side: Magisk app, Modules, PicoET-Enhance, Remove, then reboot.
echo.
pause
exit /b 0

:copyfail
echo Could not change files in %DEST%
:fail
echo.
pause
exit /b 1
