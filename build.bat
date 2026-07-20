@echo off
echo =========================================
echo Publishing NovaLite (Self-Contained)...
echo =========================================

REM Ensure any running NovaLite instance is stopped so build artifacts aren't locked
echo Stopping running NovaLite processes (if any)...
tasklist /FI "IMAGENAME eq NovaLite.exe" 2>NUL | find /I "NovaLite.exe" >NUL
if "%ERRORLEVEL%"=="0" (
    echo Found running NovaLite, attempting to terminate...
    taskkill /F /IM NovaLite.exe >nul 2>&1 || echo Failed to kill NovaLite by image name
    for /f "tokens=2" %%P in ('tasklist /FI "IMAGENAME eq NovaLite.exe" /NH ^| findstr /I "NovaLite.exe"') do (
        taskkill /F /PID %%P >nul 2>&1
    )
    timeout /t 1 /nobreak >nul
)


dotnet publish src\NovaLite.UI\NovaLite.UI.csproj -c Release -r win-x64 --self-contained true

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Dotnet publish failed!
    pause
    exit /b %errorlevel%
)

echo.
echo =========================================
echo Resetting local user data for clean run...
echo =========================================

if exist "%LOCALAPPDATA%\NovaLite\database.db" (
    del /f /q "%LOCALAPPDATA%\NovaLite\database.db"
    echo Deleted database.db
)
if exist "%LOCALAPPDATA%\NovaLite\database_backup.db" (
    del /f /q "%LOCALAPPDATA%\NovaLite\database_backup.db"
    echo Deleted database_backup.db
)
if exist "%APPDATA%\NovaLite\settings.json" (
    del /f /q "%APPDATA%\NovaLite\settings.json"
    echo Deleted settings.json
)

echo Local data cleared. Next launch will show the setup wizard.

echo.
echo =========================================
echo Compiling Setup.exe with Inno Setup...
echo =========================================

set "OUTDIR=C:\Users\Fynn\Setup"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
set "TEMP_EXE=%OUTDIR%\NovaLiteSetup.tmp.exe"
set "FINAL_EXE=%OUTDIR%\NovaLiteSetup.exe"

for /f "tokens=2" %%P in ('tasklist /fi "imagename eq NovaLiteSetup.exe" /nh ^| findstr /i "NovaLiteSetup.exe" 2^>nul') do (
    echo Closing existing installer process...
    taskkill /f /pid %%P >nul 2>&1
)

if exist "%TEMP_EXE%" del /f /q "%TEMP_EXE%"
if exist "%FINAL_EXE%" del /f /q "%FINAL_EXE%"

"C:\Program Files\Inno Setup 7\ISCC.exe" /O"%OUTDIR%" /F"NovaLiteSetup" installer.iss

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Inno Setup compilation failed! Make sure Inno Setup 7 is installed.
    pause
    exit /b %errorlevel%
)

if exist "%TEMP_EXE%" move /y "%TEMP_EXE%" "%FINAL_EXE%" >nul 2>&1
if not exist "%FINAL_EXE%" (
    echo.
    echo [ERROR] Installer was not produced at the expected output path.
    pause
    exit /b 1
)

echo.
echo =========================================
echo SUCCESS! Your installer is ready at:
echo %FINAL_EXE%
echo =========================================
pause
