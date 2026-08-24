# Markdown Studio Pro - Windows Stand

## Bestätigte Baseline

Die aktuelle Windows-Version basiert auf **WinUI 3 + WebView2** und dem abgeschlossenen Editorial-Pro-/QoL-Pass. Sie ist die Referenz für weitere Windows-Arbeiten.

## Oberfläche

- Native obere Dokument-/Toolbar-Shell ohne zusätzliche Sidebar
- Light und Dark; Dark basiert auf der früheren Midnight-Richtung
- Dynamischer Dokumentname und sauber zentrierte erste H1
- Überarbeitete Dialoge und Kontraste
- Suchen & Ersetzen mit weichem Hintergrund-Blur statt starker Abdunklung
- Kompaktere Smart-Tab-Befehlsübersicht

## Datei- und Sitzungslogik

- Gespeichert / Ungespeichert / Speichert... synchron zwischen WinUI und WebView
- Auto-Save für bereits gespeicherte Dateien
- Abfrage bei ungespeicherten Änderungen
- Drag & Drop für `.md`, `.markdown` und `.txt`
- Drop-Overlay bei gültigem Einzeldatei-Drop
- Fenstergröße und Fensterposition werden gespeichert
- Optionales Wiederöffnen des zuletzt geöffneten Dokuments
- Explizit übergebene Startdateien haben Vorrang vor Session-Restore

## Einstellungen

- Live-Vorschau für Theme, Editorbreite, Schriftgröße und Zeilenhöhe
- Abbrechen stellt den Zustand vor Öffnen des Settings-Dialogs wieder her
- Settings-Inhalt scrollbar, Footer bleibt fest
- Rechtschreibung, Fokusmodus, Auto-Save und Wortziel bleiben verfügbar

## Editor und Status

- Inline-Code-Verhalten korrigiert
- Auswahlstatistik ergänzt Wörter und Zeichen der markierten Textmenge
- Sichtbare Status-Trenner verwenden `|`
- Bestehende Tastatur-, Shortcut-, Smart-Tab-Ausführungs- und Editor-`keydown`-Logik bleibt unangetastet

## Drucken

- Druckinhalt verwendet immer eine helle Papierpalette, auch bei aktivem Darkmode
- Überschriften werden möglichst mit folgendem Inhalt zusammengehalten
- Widow-/Orphan-Regeln für Absätze
- Umbruchvermeidung für Codeblöcke, Zitate, Tabellen und Tabellenzeilen
- Explizite manuelle Seitenumbrüche bleiben erhalten

## Build

```bat
build_winui3.bat
```

Oder direkt:

```powershell
dotnet build .\MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj -c Debug -r win-x64
```

## Scope-Hinweis

Dieser Stand betrifft die Windows-Version. macOS-Dateien werden durch diesen Windows-Pass nicht verändert.
