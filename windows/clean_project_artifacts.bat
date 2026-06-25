@echo off
setlocal
cd /d "%~dp0"

echo Entferne WinUI-Build-Artefakte und lokale WebView2-Profile aus dem Projektordner...

if exist "MarkdownStudioPro.WinUI\bin" rmdir /s /q "MarkdownStudioPro.WinUI\bin"
if exist "MarkdownStudioPro.WinUI\obj" rmdir /s /q "MarkdownStudioPro.WinUI\obj"
for /d /r %%D in ("*.exe.WebView2") do (
  if exist "%%~fD" rmdir /s /q "%%~fD"
)
for /d /r %%D in ("EBWebView") do (
  if exist "%%~fD" rmdir /s /q "%%~fD"
)

echo Fertig. Danach bei Bedarf build_winui3.bat ausfuehren.
pause
