@echo off
rem Builds AutoSorter 0.3.0, runs the tests, makes AutoSorter-0.3.0.zip and installs it to D:\SE2Mods\AutoSorter.
cd /d "%~dp0"
set "DOTNET=dotnet"
set "INSTALL=D:\SE2Mods\AutoSorter"
if exist "%~dp0..\dotnet\dotnet.exe" set "DOTNET=%~dp0..\dotnet\dotnet.exe"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package.ps1" -Dotnet "%DOTNET%" -OutputDirectory "%~dp0." > build-log.txt 2>&1
set "RC=%ERRORLEVEL%"
type build-log.txt
echo.
if not "%RC%"=="0" (echo BUILD FAILED - send build-log.txt to Claude& pause& exit /b 1)
echo OK: AutoSorter-0.3.0.zip is ready
if not exist "%INSTALL%\" (echo Install folder %INSTALL% not found - copy files from the zip manually.& pause& exit /b 0)
set "BIN=%~dp0bin\Release\net9.0"
copy /Y "%BIN%\AutoSorter.dll" "%INSTALL%\" >nul && copy /Y "%BIN%\AutoSorter.deps.json" "%INSTALL%\" >nul && copy /Y "%BIN%\AutoSorter.runtimeconfig.json" "%INSTALL%\" >nul
if errorlevel 1 (echo INSTALL FAILED: close Space Engineers 2 and run build.cmd again.) else (echo INSTALLED to %INSTALL% - start the game.)
pause
