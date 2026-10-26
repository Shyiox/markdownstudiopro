# Changelog

All notable user-facing changes to Markdown Studio Pro will be documented in this file.

The format is inspired by [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases follow semantic versioning.

## [Unreleased]

This section describes a **private integration candidate** combining GitHub `1374454`, local `8af7647` and the verified October modernization ZIP. It has not been merged into `main`, installed or publicly released.

### Added

- Guarded atomic Markdown saves with detection of external file modifications; detect conflicts before copying referenced image assets.
- Trusted-editor navigation and WebView2 message checks.
- CommonMark/GFM compatibility for reference links, escaped punctuation, Setext headings, tilde code fences, nested blockquotes, balanced URL parentheses, autolinks, alternate list markers, underscore emphasis and escaped table pipes.
- Footnotes, local/offline LaTeX preview using KaTeX MathML, Mermaid diagrams and syntax highlighting for common code-block languages.
- Regression tests for file integrity, WebView2 security, editor roundtrips, preview rendering and large documents.
- Windows `test_all.ps1` verification entrypoint and third-party notices.

### Changed

- Replaced the previous colorful app branding with the approved A1 x A3 geometric icon: Windows ICO/PNG, seven macOS AppIcon sizes, and five editable SVG masters for light, dark and monochrome use. The Windows About dialog and app-window icon now reference the new assets; no additional legacy icon copies were introduced.
- Windows project target updated to .NET 10.
- Windows build helper no longer removes generated output directories or a legacy source file.

### Security

- Executable URL schemes do not create active rendered Markdown links or images.
- Navigation is restricted to the packaged editor document and untrusted bridge messages are rejected.

### Fixed

- Preserve `+` and `3)` style list markers through editing and saving.
- Keep footnote references correctly resolved after nested blockquotes.
- Prevent managed image assets from being copied when the target Markdown file has an external-change conflict.

### Remaining release gates

- Basic native WinUI editing, undo/redo, saving, close confirmation and document reopen passed in an isolated profile; complete export, Smart-Tab and accessibility verification remains deferred for private use.
- Full CommonMark/GFM specification compliance remains unverified.
- Third-party license review, signing and installer distribution remain separate decisions.


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
