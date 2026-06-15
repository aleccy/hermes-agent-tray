@echo off
setlocal

set PROJECT=HermesAgentTray.csproj
set OUT_DIR=bin\Release\publish

echo ========================================
echo  HermesAgentTray Release Build Script
echo ========================================
echo.

:: Parse arguments
set MODE=selfcontained
set COMPRESS=true
set R2R=false
set RUNTIME=win-x64

:parse_args
if "%~1"=="" goto :done_args
if /i "%~1"=="--fdd" set MODE=frameworkdependent
if /i "%~1"=="--sc" set MODE=selfcontained
if /i "%~1"=="--no-compress" set COMPRESS=false
if /i "%~1"=="--r2r" set R2R=true
if /i "%~1"=="--runtime" (
    set RUNTIME=%~2
    shift
)
if /i "%~1"=="--help" goto :show_help
shift
goto :parse_args

:done_args

:: Set output subdirectory based on mode
if "%MODE%"=="frameworkdependent" (
    set OUT_SUBDIR=fdd
    set SC_FLAG=--self-contained false
) else (
    set OUT_SUBDIR=sc
    set SC_FLAG=--self-contained true
)

:: Build publish options
set PUBLISH_OPTS=-c Release -r %RUNTIME% %SC_FLAG% -p:PublishSingleFile=true

if "%COMPRESS%"=="true" (
    set PUBLISH_OPTS=%PUBLISH_OPTS% -p:EnableCompressionInSingleFile=true
)

if "%R2R%"=="true" (
    set PUBLISH_OPTS=%PUBLISH_OPTS% -p:ReadyToRun=true
) else (
    set PUBLISH_OPTS=%PUBLISH_OPTS% -p:ReadyToRun=false
)

if "%MODE%"=="selfcontained" (
    set PUBLISH_OPTS=%PUBLISH_OPTS% -p:IncludeNativeLibrariesForSelfExtract=true
)

set FINAL_DIR=%OUT_DIR%-%OUT_SUBDIR%

echo  Mode:          %MODE%
echo  Runtime:       %RUNTIME%
echo  Compression:   %COMPRESS%
echo  ReadyToRun:    %R2R%
echo  Output:        %FINAL_DIR%
echo.
echo Publishing...
echo.

dotnet publish %PROJECT% %PUBLISH_OPTS% -o "%FINAL_DIR%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Build failed!
    exit /b 1
)

echo.
echo ========================================
echo  Build successful!
echo ========================================

:: Show output file size
for %%F in ("%FINAL_DIR%\HermesAgentTray.exe") do (
    set SIZE=%%~zF
)
echo  Output: %FINAL_DIR%\HermesAgentTray.exe

:: Calculate MB
powershell -command "$s = %SIZE%; Write-Host ('  Size:   {0:N1} MB' -f ($s / 1MB))"

echo.
echo All files in output directory:
dir /b "%FINAL_DIR%"

goto :eof

:show_help
echo.
echo Usage: build-release.bat [options]
echo.
echo Options:
echo   --sc            Self-contained build (default)
echo   --fdd           Framework-dependent build (requires .NET 8 runtime)
echo   --no-compress   Disable compression in single file
echo   --r2r           Enable ReadyToRun (faster startup, larger file)
echo   --runtime RID   Target runtime (default: win-x64)
echo   --help          Show this help
echo.
echo Examples:
echo   build-release.bat                    Self-contained, compressed, ~70MB
echo   build-release.bat --fdd              Framework-dependent, ~10MB
echo   build-release.bat --fdd --no-compress  Framework-dependent, uncompressed
echo   build-release.bat --sc --r2r         Self-contained with ReadyToRun
echo.
goto :eof
