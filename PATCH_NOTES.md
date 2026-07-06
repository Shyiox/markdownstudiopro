# Markdown Studio Pro - Windows Stand

## Ziel

Die Windows-Version bleibt eine eigenständige WinUI-3-App, orientiert sich aber an der macOS-App als Lead-Version. Ziel ist Parität dort, wo sie unter Windows sinnvoll und stabil umsetzbar ist.

## Aktueller Schwerpunkt

- WinUI-3-Shell mit nativer CommandBar
- WebView2-Editor als Kern des Markdown-Erlebnisses
- Ruhige, produktive Schreiboberfläche ohne doppelte Toolbars
- Saubere Datei- und Dirty-State-Logik
- Robuste Theme-Kontraste
- Self-contained Windows-Build fuer .NET

## Wichtige Änderungen

- Neues Logo in Windows-Assets und App-Icon eingebunden
- App-Name und Logo aus der Topbar entfernt; Fokus liegt auf Dokument und Aktionen
- Dokumenttitel, Speicherstatus und Pfad in der nativen Shell sichtbar
- Wort-/Zeichenstatistik nur noch in der Statusbar
- Titel bleibt beim Start vormarkiert, damit er direkt ersetzt werden kann
- `Tab` springt vom Titel direkt in den Schreibbereich
- Öffnen von Markdown-Dateien per Menü und Startargument stabilisiert
- `.md`-Startargumente mit Leerzeichen und Umlauten getestet
- Schließen prüft aktiv den aktuellen WebView-Inhalt und zeigt bei Änderungen den Speichern-Dialog
- `Clean`-Theme entfernt; alte `clean`-Settings werden auf `light` normalisiert
- Kontrast-Tokens für Hell, Dunkel, Sepia und Midnight überarbeitet
- Über-Dialog neu aufgebaut: flach, clean, theme-adaptiv, ohne generische Cards/Chips
- PayPal-Spendenlink dezent als Link statt als breiter Button
- Einstellungen erweitert und dauerhaft gespeichert
- Export, Quelle bearbeiten, Suche/Ersetzen, Command Palette und Fokusmodus bleiben aktiv

## Nachtest

```bat
dotnet build MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj
```

Zusätzlich manuell prüfen:

- Start ohne Datei
- Titel direkt überschreiben
- Schreiben und sofort per `X` schließen
- Datei per Menü öffnen
- Datei per Startargument öffnen
- Hell/Dunkel/Sepia/Midnight
- Über-Dialog in hellem und dunklem Theme
