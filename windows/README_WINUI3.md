# Markdown Studio Pro - Windows

Die Windows-Version von Markdown Studio Pro ist eine eigenständige **WinUI-3-App mit WebView2-Editor**. Die Oberfläche ist auf ruhiges Schreiben ausgelegt: native Windows-Shell außen, Markdown-Editor innen, ohne doppelte Web-Menüleisten oder zusätzliche Seitenleisten.

## Aktueller Stand

- Native WinUI-3-Shell mit Editorial-Pro-Toolbar
- WebView2-basierter Markdown-Editor
- Themes: **Hell** und **Dunkel**
- Dateiaktionen: Neu, Öffnen, Speichern, Speichern unter und zuletzt verwendet
- Start mit Markdown-Dateipfad, auch mit Leerzeichen und Umlauten
- Speicherstatus: **Gespeichert**, **Ungespeichert**, **Speichert...**
- Auto-Save für bereits gespeicherte Dokumente
- Schutz vor Datenverlust beim Schließen oder Ersetzen eines ungespeicherten Dokuments
- Drag & Drop für `.md`, `.markdown` und `.txt` mit sichtbarer Drop-Fläche
- Fenstergröße und Fensterposition werden gespeichert
- Optionales Wiederöffnen des zuletzt verwendeten Dokuments
- Einstellungen mit Live-Vorschau für Theme, Editorbreite, Schriftgröße und Zeilenhöhe
- Rechtschreibprüfung, Fokusmodus und Wortziel
- Statusleiste mit Wort-/Zeichenstatistik und Auswahlstatistik
- Quelle bearbeiten, Suchen & Ersetzen, Command Palette und Smart-Tab-Befehle
- Export als Markdown und HTML
- Drucken/PDF mit heller Papierdarstellung unabhängig vom App-Theme
- Verbesserte Druckumbrüche für Überschriften, Tabellen, Zitate und Codeblöcke

Die vorhandene Tastatur-, Shortcut-, Smart-Tab- und Editor-`keydown`-Logik gehört zum bestätigten Baseline-Verhalten und sollte nicht nebenbei verändert werden.

## Voraussetzungen

- Windows 10 Build 19041 oder neuer
- x64
- .NET 8 SDK für Entwicklung und Build
- Microsoft Edge WebView2 Runtime
- Optional: Visual Studio mit Windows App SDK / WinUI Tooling

Das Projekt wird für `win-x64` self-contained gebaut. Die .NET Runtime wird daher in den Build-Output aufgenommen. WebView2 bleibt eine Windows-Runtime-Voraussetzung.

## Build

Im Ordner `windows`:

```bat
build_winui3.bat
```

Das Skript:

1. bereinigt `bin` und `obj`,
2. führt Restore für `win-x64` aus,
3. baut die Debug-Version,
4. prüft EXE, `App/editor.html` und `WebView2Loader.dll`.

Direkt mit `dotnet`:

```powershell
dotnet build .\MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj -c Debug -r win-x64
```

## Start

Nach erfolgreichem Build:

```bat
start_winui3.bat
```

Direkter Pfad der Debug-Ausgabe:

```text
MarkdownStudioPro.WinUI\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Markdown Studio Pro.exe
```

Datei direkt mitgeben:

```bat
"MarkdownStudioPro.WinUI\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Markdown Studio Pro.exe" "C:\Pfad\Dokument.md"
```

Ein explizit übergebener Dateipfad hat Vorrang vor der optionalen Wiederherstellung des zuletzt geöffneten Dokuments.

## Diagnose

```bat
diagnose_winui3.bat
```

Die Diagnose prüft Projektdateien, Output, WebView2 Runtime, `WebView2Loader.dll`, relevante NuGet-Pakete und vorhandene Startup-Logs.

## Aufräumen

```bat
clean_project_artifacts.bat
```

Entfernt Build-Artefakte und lokale WebView2-Profile aus dem Projektordner.

## Manueller Smoke-Test

Vor einer Veröffentlichung mindestens prüfen:

- Start ohne Datei
- Datei per Menü öffnen
- `.md`, `.markdown` und `.txt` per Drag & Drop öffnen
- ungespeicherte Änderungen beim Öffnen/Schließen abfangen
- Speichern und Speichern unter
- Auto-Save bei bereits gespeicherter Datei
- Hell/Dunkel
- Editorbreite, Schriftgröße und Zeilenhöhe live ändern und per Abbrechen zurücksetzen
- Fenster verschieben/vergrößern und nach Neustart wiederherstellen
- optionales Wiederöffnen des letzten Dokuments an/aus
- Suchen & Ersetzen
- Smart-Tab-Hilfe und bestehende Smart-Tab-Ausführung
- Markdown-/HTML-Export
- Druckvorschau im Darkmode: Dokumentblatt bleibt weiß
- längeres Dokument auf saubere Seitenumbrüche prüfen
