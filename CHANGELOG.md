# Changelog

All notable user-facing changes to Markdown Studio Pro will be documented in this file.

The format is inspired by [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases follow semantic versioning.

## [Unreleased]

## [0.1.0] - 2026-09-13

### Added

- Editorial Pro Windows interface built with WinUI 3 and WebView2
- Light and Dark themes with synchronized native-shell and editor state
- Command Palette with keyboard and mouse navigation
- Smart-Tab editing commands and Find & Replace previous/next navigation
- Markdown, HTML, and print/PDF workflows, including `Ctrl+P` and native WebView2 print preview
- Drag and drop for Markdown and text files, recent files, and optional last-document restore
- Selection and reading statistics, configurable editor sizing, and persistent window geometry
- Automated regression coverage for Markdown roundtrips, save/session safety, editor interactions, and native robustness

### Changed

- Refined the Windows shell and editor into the finalized Windows V2 layout
- Improved print pagination and theme-independent white-paper output
- Improved table editing, selection restoration, undo/redo behavior, Command Palette contrast, and theme synchronization
- Improved native dialog, WebView2, and shutdown lifecycle handling

### Fixed

- Lossy Markdown roundtrips involving empty table cells, nested lists, blockquotes, hard breaks, code blocks, and table alignment
- Inline-code and task-list formatting loss, including task links
- Save, Save As, session, and explicit-launch-path races
- Programmatic undo/redo selection issues, formatting leaks, and Smart-Tab code-block caret placement
- Stale Find & Replace query/index state and Markdown export using the wrong action
- Dark-theme print contrast, Command Palette readability, and native theme synchronization
- Re-entrant native dialogs, unbounded async UI exceptions, false shutdown/save prompts, and WebView2 print/download UI issues

### Notes

- Windows x64, .NET 8, WinUI 3, and the Microsoft Edge WebView2 Runtime are required.
- The macOS source track remains separate and is unchanged by this release.
- Frontmatter is preserved during Markdown roundtrips; a dedicated Document Information / Metadata UI is planned for a later release.

## Release history

Version 0.1.0 is the first public release preparation for the Windows V2 application.
