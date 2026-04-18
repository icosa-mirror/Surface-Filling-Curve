@echo off
setlocal

set REPO_ROOT=%~dp0
set BUILD_DIR=%REPO_ROOT%build_plugin
set PKG_DIR=%REPO_ROOT%unity\com.iota97.surface-filling-curve

cmake -B "%BUILD_DIR%" -S "%REPO_ROOT%"
cmake --build "%BUILD_DIR%" --target surface_filling_curve --config Release

copy /Y "%BUILD_DIR%\Release\surface_filling_curve.dll" ^
         "%PKG_DIR%\Plugins\Windows\x86_64\surface_filling_curve.dll"

echo Installed: %PKG_DIR%\Plugins\Windows\x86_64\surface_filling_curve.dll
