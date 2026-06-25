@echo off
setlocal
cd /d "%~dp0"

set "WINUI_DIR=MarkdownStudioPro.WinUI"
set "TFM=net8.0-windows10.0.19041.0"
set "RID=win-x64"
set "OUT=%WINUI_DIR%\bin\Debug\%TFM%\%RID%"
set "EXE=%OUT%\Markdown Studio Pro.exe"
set "EDITOR=%OUT%\App\editor.html"
set "LOADER=%OUT%\WebView2Loader.dll"
set "WEBVIEW2_ROOT=%ProgramFiles(x86)%\Microsoft\EdgeWebView\Application"

echo Markdown Studio Pro Windows start
echo.

if not exist "%WEBVIEW2_ROOT%" echo Hinweis: Microsoft Edge WebView2 Runtime wurde unter "%WEBVIEW2_ROOT%" nicht gefunden.
if not exist "%WEBVIEW2_ROOT%" echo Installiere oder repariere die Runtime, falls WebView2 beim Start fehlschlaegt.
if not exist "%WEBVIEW2_ROOT%" echo.

if not exist "%EXE%" (
  echo EXE fehlt: "%EXE%"
  echo Fuehre zuerst build_winui3.bat aus.
  pause
  exit /b 1
)

if not exist "%EDITOR%" (
  echo editor.html fehlt im Output: "%EDITOR%"
  echo Fuehre build_winui3.bat erneut aus.
  pause
  exit /b 1
)

if not exist "%LOADER%" (
  echo WebView2Loader.dll fehlt im Output-Root: "%LOADER%"
  echo Fuehre build_winui3.bat aus oder starte diagnose_winui3.bat.
  pause
  exit /b 1
)

echo Starte:
echo   %EXE%
echo.
"%EXE%"
set EXITCODE=%ERRORLEVEL%

echo.
if not "%EXITCODE%"=="0" (
  echo WinUI wurde mit Exitcode %EXITCODE% beendet.
  echo.
  echo Logs, falls vorhanden:
  echo   %OUT%\winui-startup-error.log
  echo   %OUT%\winui-unhandled-error.log
  echo.
)

pause
exit /b %EXITCODE%
