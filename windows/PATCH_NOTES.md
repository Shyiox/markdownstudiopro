# Markdown Studio Pro - Windows Baseline

## Confirmed baseline

The current Windows edition is based on **WinUI 3 + WebView2** and the completed Editorial Pro / QoL pass. This document records the confirmed technical baseline for future Windows work.

## Interface

- Native top document/toolbar shell without an additional sidebar
- Light and Dark themes; Dark follows the earlier Midnight visual direction
- Dynamic document title and a cleanly centered first H1
- Refined dialogs and contrast handling
- Find & Replace uses a soft background blur instead of a heavy dark overlay
- Smart-Tab command help uses a calmer, compact single-column layout

## File and session behavior

- **Saved / Unsaved / Saving...** are synchronized between WinUI and WebView
- Auto-save for files that already have a path
- Unsaved-change confirmation before replacing or closing a document
- Drag & drop for `.md`, `.markdown`, and `.txt`
- Visible drop overlay for a valid single-file drag
- Window size and window position are persisted
- Optional reopening of the last opened document
- Explicit launch paths take precedence over session restore

## Settings

- Live preview for theme, editor width, font size, and line height
- Cancel restores the values that were active when the Settings dialog opened
- Settings content is scrollable while the footer remains fixed
- Spellcheck, Focus Mode, auto-save, and word goal remain available

## Editor and status

- Inline-code behavior corrected
- Selection statistics include selected words and characters
- Visible status separators use `|`
- Existing keyboard, shortcut, Smart-Tab execution, and editor `keydown` behavior is intentionally preserved

## Printing

- Printed content always uses a light paper palette, even when the app is in Dark mode
- Headings are kept with following content where possible
- Widow/orphan control for paragraphs
- Break avoidance for code blocks, quotes, tables, and table rows
- Explicit manual page breaks remain supported

## Build

```bat
build_winui3.bat
```

Or directly:

```powershell
dotnet build .\MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj -c Debug -r win-x64
```

## Scope note

This baseline covers the Windows edition. The Windows Editorial Pro / QoL pass does not modify macOS files.

For user-facing release history, see [`../CHANGELOG.md`](../CHANGELOG.md).
