@echo off
setlocal
pushd "%~dp0" || exit /b 1

set "DOTNET_EXE=dotnet"
if exist ".dotnet\dotnet.exe" set "DOTNET_EXE=%CD%\.dotnet\dotnet.exe"
set "DOTNET_CLI_HOME=%CD%\.dotnet-home"
set "NUGET_PACKAGES=%CD%\.nuget\packages"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"
set "APP_PROJECT=src\Llano.App\Llano.App.csproj"
set "CHECK_PROJECT=tests\Llano.Checks\Llano.Checks.csproj"
set "MSI_PROJECT=installer\Llano.Installer.wixproj"
if not defined LLANO_PUBLISH_DIR set "LLANO_PUBLISH_DIR=%CD%\artifacts\publish"
for %%I in ("%LLANO_PUBLISH_DIR%") do set "LLANO_PUBLISH_DIR=%%~fI"

if "%~1"=="" goto restore_app
if /i "%~1"=="--portable" goto restore_portable
if /i "%~1"=="--installer" goto restore_portable
if /i "%~1"=="--release" goto release
echo Usage: build.cmd [--portable ^| --installer ^| --release]
goto failure

:release
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%CD%\release.ps1" || goto failure
popd
exit /b 0

:restore_app
"%DOTNET_EXE%" restore "%APP_PROJECT%" --configfile NuGet.config || goto failure
goto checks

:restore_portable
"%DOTNET_EXE%" restore "%APP_PROJECT%" --configfile NuGet.config -r win-x64 -p:SelfContained=true || goto failure
if /i "%~1"=="--installer" (
    "%DOTNET_EXE%" restore "%MSI_PROJECT%" --configfile NuGet.config || goto failure
)

:checks
"%DOTNET_EXE%" restore "%CHECK_PROJECT%" --configfile NuGet.config || goto failure
"%DOTNET_EXE%" run --project "%CHECK_PROJECT%" -c Release --no-restore || goto failure
if not "%~1"=="" goto publish

"%DOTNET_EXE%" build "%APP_PROJECT%" -c Release --no-restore || goto failure
popd
exit /b 0

:publish
"%DOTNET_EXE%" publish "%APP_PROJECT%" -c Release -r win-x64 --self-contained --no-restore -o "%LLANO_PUBLISH_DIR%" || goto failure
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%CD%\package-presentmon.ps1" "%LLANO_PUBLISH_DIR%" || goto failure
copy /y LICENSE "%LLANO_PUBLISH_DIR%\LICENSE" >nul || goto failure
copy /y THIRD-PARTY-NOTICES.md "%LLANO_PUBLISH_DIR%\THIRD-PARTY-NOTICES.md" >nul || goto failure
copy /y README.md "%LLANO_PUBLISH_DIR%\README.md" >nul || goto failure
copy /y CHANGELOG.md "%LLANO_PUBLISH_DIR%\CHANGELOG.md" >nul || goto failure
if not exist "%LLANO_PUBLISH_DIR%\docs" mkdir "%LLANO_PUBLISH_DIR%\docs" || goto failure
copy /y docs\*.md "%LLANO_PUBLISH_DIR%\docs\" >nul || goto failure
if not exist "%LLANO_PUBLISH_DIR%\docs\images" mkdir "%LLANO_PUBLISH_DIR%\docs\images" || goto failure
copy /y docs\images\*.png "%LLANO_PUBLISH_DIR%\docs\images\" >nul || goto failure
copy /y "%NUGET_PACKAGES%\hidsharp\2.6.4\LICENSE.txt" "%LLANO_PUBLISH_DIR%\HidSharp-LICENSE.txt" >nul || goto failure
if /i "%~1"=="--installer" (
    "%DOTNET_EXE%" build "%MSI_PROJECT%" -c Release --no-restore -o artifacts "-p:PublishDirectory=%LLANO_PUBLISH_DIR%" || goto failure
)
popd
exit /b 0

:failure
popd
exit /b 1
