# Markdown Studio Pro - Windows

Diese Spur ist die Windows-Version von Markdown Studio Pro. Die macOS-App bleibt die Lead-Version; Windows folgt ihr funktional und visuell so weit wie sinnvoll mit WinUI 3, Fluent-UI und WebView2.

```text
MarkdownStudioPro.WinUI/    Windows-App: WinUI 3 Shell + WebView2 Editor
```

## Stand

- Native WinUI-3-Shell mit kompakter CommandBar
- WebView2-basierter Markdown-Editor ohne doppelte Web-Menuleiste
- Dateiaktionen: Neu, Öffnen, Speichern, Speichern unter, zuletzt verwendet
- Markdown-Start per Dateipfad/Startargument, auch mit Leerzeichen und Umlauten
- Schreiborientierter Startzustand: Dokumenttitel ist vormarkiert und kann direkt ersetzt werden
- Dirty-State mit Abfrage beim Schließen, auch wenn direkt nach dem Tippen geschlossen wird
- Export: Markdown, HTML und PDF/Print-Fallback
- Quelle bearbeiten, Suche/Ersetzen, Command Palette, Fokusmodus
- Themes: Hell, Dunkel, Sepia, Midnight
- Native Einstellungen für Theme, Fokusmodus, Editorbreite, Schriftgröße, Zeilenhöhe, Rechtschreibung, Auto-Save und Wortziel
- Cleaner Über-Dialog mit Logo, Version, Kontakt und dezentem PayPal-Link

## Voraussetzungen

- Windows 10 19041 oder neuer
- Microsoft Edge WebView2 Runtime
- .NET 8 SDK für Entwicklung und Build
- Visual Studio mit Windows App SDK / WinUI Workload für IDE-Entwicklung

Die gebaute Windows-App ist self-contained für .NET und bringt die benötigte .NET Runtime im Output mit. WebView2 bleibt eine Windows-Runtime-Voraussetzung.

## Build

```bat
build_winui3.bat
```

Das Skript bereinigt `bin` und `obj`, führt Restore/Build für `win-x64` aus und prüft danach EXE, `editor.html` und `WebView2Loader.dll`.

## Start

```bat
start_winui3.bat
```

Direkter Start der Debug-Ausgabe:

```text
MarkdownStudioPro.WinUI\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Markdown Studio Pro.exe
```

Eine Markdown-Datei kann auch als Argument übergeben werden:

```bat
"MarkdownStudioPro.WinUI\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Markdown Studio Pro.exe" "C:\Pfad\Dokument.md"
```

## Aufraeumen

```bat
clean_project_artifacts.bat
```

Entfernt Build-Artefakte und lokale WebView2-Profile aus dem Projektordner. Danach bei Bedarf neu bauen.

## Testcheck

Vor einem Commit mindestens ausführen:

```bat
dotnet build MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj
```

Manuell prüfen:

- Start ohne Datei: Titel vormarkiert, Tab springt in den Schreibbereich
- Datei öffnen per Menü und per Startargument
- Schreiben und direkt schließen: Dialog "Änderungen sichern?" erscheint
- Themes Hell, Dunkel, Sepia und Midnight
- Über-Dialog in hellem und dunklem Theme
- Export/Quelle/Suche bei Bedarf
