@echo off
echo =========================================
echo Publishing NovaLite (Self-Contained)...
echo =========================================

dotnet publish src\NovaLite.UI\NovaLite.UI.csproj -c Release -r win-x64 --self-contained true

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Dotnet publish failed!
    pause
    exit /b %errorlevel%
)

@REM echo.
@REM echo =========================================
@REM echo Resetting local user data for clean run...
@REM echo =========================================

@REM if exist "%LOCALAPPDATA%\NovaLite\database.db" (
@REM     del /f /q "%LOCALAPPDATA%\NovaLite\database.db"
@REM     echo Deleted database.db
@REM )
@REM if exist "%LOCALAPPDATA%\NovaLite\database_backup.db" (
@REM     del /f /q "%LOCALAPPDATA%\NovaLite\database_backup.db"
@REM     echo Deleted database_backup.db
@REM )
@REM if exist "%APPDATA%\NovaLite\settings.json" (
@REM     del /f /q "%APPDATA%\NovaLite\settings.json"
@REM     echo Deleted settings.json
@REM )

@REM echo Local data cleared. Next launch will show the setup wizard.

echo.
echo =========================================
echo Compiling Setup.exe with Inno Setup...
echo =========================================

"C:\Program Files\Inno Setup 7\ISCC.exe" installer.iss

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Inno Setup compilation failed! Make sure Inno Setup 7 is installed.
    pause
    exit /b %errorlevel%
)

echo.
echo =========================================
echo SUCCESS! Your installer is ready at:
echo C:\Users\Fynn\Setup\NovaLiteSetup.exe
echo =========================================
pause
