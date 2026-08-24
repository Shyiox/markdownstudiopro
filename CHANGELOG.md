# Changelog

All notable user-facing changes to Markdown Studio Pro will be documented in this file.

The format is inspired by [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and future tagged releases can follow semantic versioning once public versioned builds are published.

## [Unreleased]

### Windows - Editorial Pro / QoL pass

#### Added

- Clear document save states: **Saved**, **Unsaved**, and **Saving...**
- Selection statistics for selected words and characters
- Live preview for theme, editor width, font size, and line height
- Persistent window size and position
- Optional reopening of the last document on startup
- Drag & drop for `.md`, `.markdown`, and `.txt` files
- Visible drop surface for valid single-file drags
- Light print/PDF paper rendering independent of the active app theme

#### Changed

- Refined the Windows WinUI shell into the current Editorial Pro layout
- Improved Find & Replace backdrop treatment with a softer blur
- Restyled Smart-Tab command help into a more compact single-column layout
- Improved print pagination for headings, paragraphs, quotes, tables, table rows, and code blocks
- Updated Windows project documentation and public repository presentation

#### Fixed

- Save/Save As no longer marks a document clean before the native write succeeds
- Cancelling Save As preserves the unsaved state
- Edits made during an in-progress save remain marked as unsaved after the earlier write completes
- Cancelling live settings preview restores the values active before the dialog was opened
- Invalid or missing remembered startup files fall back safely without overriding an explicitly supplied launch path
- Inline-code behavior and visible status separators were cleaned up in the confirmed Windows baseline

#### Notes

- Existing keyboard, shortcut, Smart-Tab execution, and editor `keydown` behavior is intentionally preserved
- The Windows Editorial Pro / QoL pass does not modify the macOS source track
- Distribution is currently source-based; no public installer/package has been published yet

## Release history

No tagged public release has been published yet. The first versioned release will be added here when a distributable build is available.
