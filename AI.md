# AI.md

## Project workflow

- Check this file before each larger project step.
- Check Git status before patching when a Git checkout is available.
- Read relevant source files before changing them.
- Deliver changes as a structure ZIP with complete replacement files when working outside a live Git checkout.
- Update this file when a stage is completed.

## Branch strategy

- `main`: stable shared repository state.
- `platform/mac`: macOS app development.
- `platform/windows`: Windows WinUI app development.
- Shared editor logic should stay platform-neutral where possible.
- Platform shells may evolve independently.

## Completed stages

### 2026-06-25 - Windows WinUI source added

- Added the Windows source version under `windows/`.
- Included the cleaned `MarkdownStudioPro.WinUI` app as the single Windows shell.
- Documented Windows build, start, and diagnostic scripts.
- Kept Windows build artifacts out of Git and preserved the existing macOS platform folder.
- Updated the root README so the repository now documents both macOS and Windows source versions.

### 2026-06-12 — GitHub project page polish

- Updated the root `README.md` to read more like a project landing page.
- Clarified that Markdown Studio Pro is an early macOS source prototype, not a finished app release.
- Documented current macOS capabilities, repository structure, known limitations, roadmap, support link, and license notes.
- Kept `macos/README.md` as the platform-specific macOS setup page.

### 2026-06-12 — macOS source prototype prepared for GitHub

- Prepared a GitHub-oriented source layout with `macos/` as the macOS platform folder.
- Included the macOS Xcode project and Swift/AppKit/WKWebView source.
- Added root `README.md` for the GitHub project page.
- Added `macos/README.md` for platform-specific macOS notes.
- Added `.gitignore` for macOS/Xcode build artifacts and local files.
- Confirmed that no DMG is required for the first source upload.
- Planned a future Windows platform folder under `windows/`.

### 2026-06-12 — macOS editor shell and file workflow

- Kept the bundled HTML/JS editor at `macos/MarkdownStudioProMac/App/editor.html`.
- Used `EditorWindowController` for window lifecycle, native menu actions, toolbar actions, file handling, export, appearance, and WebView messaging.
- Used `MacEditorShellView` as the SwiftUI container around the existing `WKWebView`.
- Added native macOS toolbar/menu workflows for new, open, save, save as, insert, copy markdown, appearance, and export.
- Added broader text-file opening support for Markdown and common text-based file types.

## Notes for next stages

- Build and run locally in Xcode with `Cmd+B` and `Cmd+R`.
- Keep generated macOS folders such as `__MACOSX`, `.DS_Store`, `DerivedData`, `build`, and `xcuserdata` out of Git.
- Add screenshots after the first GitHub page is online.
- Add a packaged `.dmg` later only when a beta app release is intended.
- Fix or track remaining app-termination unsaved-change behavior for `Cmd+Q`.
