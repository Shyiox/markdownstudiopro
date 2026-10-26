# Windows candidate acceptance checklist

> Applies only to the isolated .NET 10 patch candidate based on `8af7647`.
> Do **not** run against personal documents or the installed production app. Use throwaway test documents and an isolated user profile or Windows Sandbox.

## Verified in the 2026-10-26 candidate

- [x] Self-contained Windows Debug build completed; WinUI 3 executable, WebView2Loader and editor page exist.
- [x] Revised candidate native startup and core text editing, save, undo/redo, close confirmation, and document reopening were verified in an isolated Windows test profile. Extended GUI acceptance remains deferred for private use.
- [x] The browser/Node/.NET regression suite passed **147/147**, plus offline preview and 1,200-paragraph performance checks.
- [x] ZIP member SHA-256 verification against the tested staged source.

## Privatnutzung – bewusst aufgeschoben („erstmal unwichtig“), Entscheidung 2026-10-26

**Projektzweck:** Der Nutzer verwendet Markdown Studio Pro derzeit hauptsächlich privat. Die verbleibenden vollständigen GUI-/Release-Abnahmen sind **niedrige Priorität (später)** und werden nur auf ausdrücklichen Wunsch oder bei konkreten Problemen wieder aufgenommen. Dies bedeutet **nicht bestanden** und ist **keine Freigabe für einen öffentlichen Release**.

- **Bereits bestätigt:** 147/147 automatisierte Regressionstests; nativer Start, Texteingabe, Undo/Redo, Suchen/Ersetzen, Speichern, Dirty-State und Schließen/Wiederöffnen mit temporären Dokumenten in isolierten QA-Kopien.
- **Vorläufig unwichtig / aufgeschoben:** nativer „Speichern unter“-Dateidialog, Smart-Tab, Textauswahl und Tastatur-A11y, vollständige PDF-/HTML-/Druck-Exporte, Mermaid-/LaTeX-Detailabnahme, großer Editor-Stresstest und Absturz-Wiederherstellung im echten GUI.
- **Nicht als unproblematisch verbuchen:** Externe Dateiänderungen sind durch Unit-/Regressionstests gegen Überschreiben geschützt, aber der echte native Konfliktdialog ist noch nicht vollständig abgenommen. Bei tatsächlich beobachtetem Konflikt oder Datenverlust diese Prüfung sofort priorisieren.
- **Nur vor Weitergabe/öffentlichem Release relevant:** vollständige Drittanbieter-Lizenzen (`fastdom`, `khroma`), Installer/Signatur, Release-Kanal und öffentliche Release-Abnahme. Vor einer Veröffentlichung müssen diese Anforderungen wieder als Pflichtgate gelten.
- **Arbeitsgrenzen:** Bestehende Installation, persönliche Dokumente, `main`, vorhandene ZIPs und andere Prozesse nicht ohne gesonderte Freigabe ändern. GitHub-Codeabgleich erfolgt getrennt und konfliktbewusst; eine Backlog-Notiz ist kein Merge.

## Required before declaring a release

- [ ] Run native GUI on target Win10 19041+ and Win11 with verified WebView2 runtime.
- [ ] Open a *temporary* Markdown file containing headings, lists, images, a table, footnote, inline/block formula, Mermaid code and a fenced code block. Check that the document displays correctly.
- [ ] Edit each construct, undo and redo, close/reopen, and verify that Markdown source still contains the expected information.
- [ ] Check keyboard behavior: shortcuts, Smart-Tab, code block navigation, selection, Find & Replace, mouse and keyboard focus.
- [ ] Save and Save As to a temporary location. Check the visible dirty/saving/saved states, auto-save and window-close prompt.
- [ ] Open the same temporary Markdown file in a second editor, change and save it externally, then try to save in Markdown Studio Pro. Confirm the external file stays unchanged, the app shows an understandable conflict and no image assets are copied.
- [ ] With a *temporary* image, check clipboard insert, persisted local asset paths, share ZIP, HTML/PDF export and print preview, including Dark mode.
- [ ] Check zoom/text scaling, accessible names, keyboard-only navigation, theme contrast and screen-reader navigation.
- [ ] Use several larger representative documents to check input latency, caret continuity, undo/redo and responsiveness.
- [ ] Test crash/restart recovery in a disposable profile; confirm no truncated or empty Markdown file and no leaked temporary save files.
- [ ] Complete a comprehensive third-party and packaged-release review before shipping a public app. The previously missing `fastdom@1.0.12` MIT notice and `khroma@2.1.0` upstream license were checked and annotated before the source branch was shared.
- [ ] Decide installer, signatures, release version, release channel and rollback procedure explicitly.
- [ ] Record final reviewer, environment, date, exact artifact hash and outstanding findings.

## Observed limitation

Earlier Windows Sandbox runs were inconclusive. Subsequent isolated Windows QA **did** verify native startup, Markdown display, real text input, undo/redo, save, Find & Replace, the unsaved-close prompt and document reopen (see the project Obsidian QA records, 2026-10-26). These are partial GUI tests, **not** full accessibility, Save As, export, security or public-release certification. Full CommonMark/GFM certification remains outstanding.
