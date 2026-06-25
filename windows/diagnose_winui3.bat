@echo off
setlocal
cd /d "%~dp0"

set "PROJECT=MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj"
set "WINUI_DIR=MarkdownStudioPro.WinUI"
set "TFM=net8.0-windows10.0.19041.0"
set "RID=win-x64"
set "OUT=%WINUI_DIR%\bin\Debug\%TFM%\%RID%"
set "EXE=%OUT%\Markdown Studio Pro.exe"
set "EDITOR_SRC=%WINUI_DIR%\App\editor.html"
set "EDITOR_OUT=%OUT%\App\editor.html"
set "LOADER_ROOT=%OUT%\WebView2Loader.dll"
set "LOADER_NATIVE=%OUT%\runtimes\win-x64\native\WebView2Loader.dll"
set "WEBVIEW2_ROOT=%ProgramFiles(x86)%\Microsoft\EdgeWebView\Application"

echo Markdown Studio Pro Windows Diagnose
echo ==================================
echo.

if exist "%WINUI_DIR%\" (echo [OK]     WinUI-Projektordner: %WINUI_DIR%) else echo [FEHLT]  WinUI-Projektordner: %WINUI_DIR%
if exist "%PROJECT%" (echo [OK]     WinUI-Projektdatei: %PROJECT%) else echo [FEHLT]  WinUI-Projektdatei: %PROJECT%
if exist "%EDITOR_SRC%" (echo [OK]     Quell-editor.html: %EDITOR_SRC%) else echo [FEHLT]  Quell-editor.html: %EDITOR_SRC%
if exist "%OUT%\" (echo [OK]     Output-Ordner: %OUT%) else echo [FEHLT]  Output-Ordner: %OUT%
if exist "%EXE%" (echo [OK]     WinUI-EXE: %EXE%) else echo [FEHLT]  WinUI-EXE: %EXE%
if exist "%EDITOR_OUT%" (echo [OK]     Output-editor.html: %EDITOR_OUT%) else echo [FEHLT]  Output-editor.html: %EDITOR_OUT%
if exist "%LOADER_ROOT%" (echo [OK]     Root-WebView2Loader.dll: %LOADER_ROOT%) else echo [FEHLT]  Root-WebView2Loader.dll: %LOADER_ROOT%
if exist "%LOADER_NATIVE%" (echo [OK]     Native Runtime-WebView2Loader.dll: %LOADER_NATIVE%) else echo [INFO]   Native Runtime-WebView2Loader.dll nicht separat vorhanden; Root-Kopie ist entscheidend: %LOADER_NATIVE%
if exist "%WEBVIEW2_ROOT%\" echo [OK]     Microsoft Edge WebView2 Runtime: %WEBVIEW2_ROOT%
if not exist "%WEBVIEW2_ROOT%\" echo [FEHLT]  Microsoft Edge WebView2 Runtime: %WEBVIEW2_ROOT%

echo.
echo WebView2 Runtime Versionen:
if exist "%WEBVIEW2_ROOT%\" dir /b "%WEBVIEW2_ROOT%"
if not exist "%WEBVIEW2_ROOT%\" echo   Keine Runtime unter dem Standardpfad gefunden.

echo.
echo NuGet-Pakete im Projekt:
if exist "%PROJECT%" (
  findstr /i "Microsoft.WindowsAppSDK Microsoft.Web.WebView2 TargetFramework RuntimeIdentifier PlatformTarget" "%PROJECT%"
) else (
  echo   Projektdatei fehlt.
)

echo.
echo dotnet --info Kurzstatus:
dotnet --info | findstr /i "Version RID OS"

echo.
echo WebView2Loader.dll Fundstellen im WinUI-Output:
if exist "%WINUI_DIR%\bin\" (
  dir /s /b "%WINUI_DIR%\bin\WebView2Loader.dll" 2>nul
  dir /s /b "%WINUI_DIR%\bin\*\WebView2Loader.dll" 2>nul
  dir /s /b "%WINUI_DIR%\bin\*\*\WebView2Loader.dll" 2>nul
  dir /s /b "%WINUI_DIR%\bin\*\*\*\WebView2Loader.dll" 2>nul
  dir /s /b "%WINUI_DIR%\bin\*\*\*\*\WebView2Loader.dll" 2>nul
) else (
  echo   Kein bin-Ordner vorhanden.
)

echo.
echo Letzte Startup-Logs:
if exist "%OUT%\winui-startup-error.log" (
  echo --- winui-startup-error.log ---
  type "%OUT%\winui-startup-error.log"
) else (
  echo   Kein winui-startup-error.log vorhanden.
)

if exist "%OUT%\winui-unhandled-error.log" (
  echo.
  echo --- winui-unhandled-error.log ---
  type "%OUT%\winui-unhandled-error.log"
) else (
  echo   Kein winui-unhandled-error.log vorhanden.
)

echo.
echo Diagnose abgeschlossen. Die App wurde nicht gestartet.
pause
exit /b 0
