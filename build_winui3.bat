@echo off
setlocal
cd /d "%~dp0"

set "PROJECT=MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj"
set "WINUI_DIR=MarkdownStudioPro.WinUI"
set "TFM=net8.0-windows10.0.19041.0"
set "RID=win-x64"
set "OUT=%WINUI_DIR%\bin\Debug\%TFM%\%RID%"
set "EXE=%OUT%\Markdown Studio Pro.exe"
set "EDITOR=%OUT%\App\editor.html"
set "LOADER=%OUT%\WebView2Loader.dll"
set "WEBVIEW2_ROOT=%ProgramFiles(x86)%\Microsoft\EdgeWebView\Application"

echo Markdown Studio Pro Windows build
echo.

if not exist "%WINUI_DIR%" (
  echo Fehler: Ordner "%WINUI_DIR%" wurde nicht gefunden.
  pause
  exit /b 1
)

if not exist "%PROJECT%" (
  echo Fehler: Projektdatei "%PROJECT%" wurde nicht gefunden.
  pause
  exit /b 1
)

if not exist "%WINUI_DIR%\App\editor.html" (
  echo Fehler: "%WINUI_DIR%\App\editor.html" wurde nicht gefunden.
  pause
  exit /b 1
)

if not exist "%WEBVIEW2_ROOT%" echo Hinweis: Microsoft Edge WebView2 Runtime wurde unter "%WEBVIEW2_ROOT%" nicht gefunden.
if not exist "%WEBVIEW2_ROOT%" echo Falls die App beim Start WebView2 meldet, Runtime installieren oder reparieren.
if not exist "%WEBVIEW2_ROOT%" echo.

if exist "%WINUI_DIR%\Program.cs" (
  echo Entferne alten Program.cs-Einstiegspunkt...
  del /f /q "%WINUI_DIR%\Program.cs"
)

echo Bereinige WinUI bin/obj...
if exist "%WINUI_DIR%\obj" rmdir /s /q "%WINUI_DIR%\obj"
if exist "%WINUI_DIR%\bin" rmdir /s /q "%WINUI_DIR%\bin"

echo.
echo Restore...
dotnet restore "%PROJECT%" -r %RID%
if errorlevel 1 (
  echo.
  echo Restore fehlgeschlagen.
  pause
  exit /b 1
)

echo.
echo Build...
dotnet build "%PROJECT%" -c Debug -r %RID% --no-restore
if errorlevel 1 (
  echo.
  echo Build fehlgeschlagen. Das Fenster bleibt offen.
  pause
  exit /b 1
)

echo.
echo Pruefe Output:
echo   %OUT%

if not exist "%EXE%" (
  echo Fehler: EXE fehlt: "%EXE%"
  pause
  exit /b 1
)

if not exist "%EDITOR%" (
  echo Fehler: editor.html fehlt im Output: "%EDITOR%"
  pause
  exit /b 1
)

if not exist "%LOADER%" (
  echo Fehler: WebView2Loader.dll fehlt im Output-Root: "%LOADER%"
  echo Erwartet wird eine Kopie direkt neben der EXE.
  echo Starte diagnose_winui3.bat fuer Details.
  pause
  exit /b 1
)

echo.
echo Build erfolgreich.
echo EXE:    %EXE%
echo Editor: %EDITOR%
echo Loader: %LOADER%
pause
