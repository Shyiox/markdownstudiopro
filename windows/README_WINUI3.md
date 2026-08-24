# Markdown Studio Pro - Windows

The Windows edition of Markdown Studio Pro is a standalone **WinUI 3 application with a WebView2-based Markdown editor**. Its interface is designed around calm, focused writing: a native Windows shell around a flexible document surface, without duplicate web toolbars or additional sidebars.

## Current status

- Native WinUI 3 shell with the Editorial Pro command surface
- WebView2-based Markdown editor
- **Light** and **Dark** themes
- File actions: New, Open, Save, Save As, and recent files
- Launch directly with a Markdown file path, including paths with spaces and Unicode characters
- Clear save states: **Saved**, **Unsaved**, and **Saving...**
- Auto-save for documents that already have a file path
- Unsaved-change protection when closing or replacing a document
- Drag & drop for `.md`, `.markdown`, and `.txt` with a visible drop surface
- Persistent window size and position
- Optional reopening of the last document on startup
- Live settings preview for theme, editor width, font size, and line height
- Spellcheck, Focus Mode, and word goal
- Status bar with word/character statistics and selection statistics
- Source editing, Find & Replace, Command Palette, and Smart-Tab commands
- Markdown and HTML export
- Print/PDF workflow with a white paper surface independent of the app theme
- Improved print pagination for headings, tables, quotes, and code blocks

The existing keyboard, shortcut, Smart-Tab execution, and editor `keydown` behavior are part of the confirmed baseline and should not be changed incidentally.

## Requirements

- Windows 10 build 19041 or newer
- x64
- .NET 8 SDK for development and builds
- Microsoft Edge WebView2 Runtime
- Optional: Visual Studio with Windows App SDK / WinUI tooling

The project is built self-contained for `win-x64`, so the .NET runtime is included in the build output. WebView2 remains a Windows runtime requirement.

## Build

From the `windows` directory:

```bat
build_winui3.bat
```

The script:

1. cleans `bin` and `obj`,
2. restores dependencies for `win-x64`,
3. builds the Debug configuration,
4. verifies the executable, `App/editor.html`, and `WebView2Loader.dll`.

Direct build with `dotnet`:

```powershell
dotnet build .\MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj -c Debug -r win-x64
```

## Run

After a successful build:

```bat
start_winui3.bat
```

Direct path to the Debug executable:

```text
MarkdownStudioPro.WinUI\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Markdown Studio Pro.exe
```

Open a file directly at launch:

```bat
"MarkdownStudioPro.WinUI\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Markdown Studio Pro.exe" "C:\Path\Document.md"
```

An explicitly supplied launch path always takes precedence over optional last-document restore.

## Diagnostics

```bat
diagnose_winui3.bat
```

The diagnostic script checks the project files, build output, WebView2 Runtime, `WebView2Loader.dll`, relevant NuGet packages, and available startup logs.

## Cleanup

```bat
clean_project_artifacts.bat
```

This removes build artifacts and local WebView2 profiles from the project directory.

## Manual smoke test

Before publishing a Windows build, verify at least:

- launch without a file
- open a file from the menu
- open `.md`, `.markdown`, and `.txt` via drag & drop
- unsaved-change prompts when replacing or closing a document
- Save and Save As
- auto-save for an already saved document
- Light and Dark themes
- live editor width, font size, and line-height preview plus Cancel rollback
- window move/resize persistence after restart
- last-document restore enabled and disabled
- Find & Replace
- Smart-Tab help and the existing Smart-Tab execution behavior
- Markdown/HTML export
- print preview in Dark mode: the document paper remains white
- a longer document for clean page breaks

## Related documentation

- [`../CHANGELOG.md`](../CHANGELOG.md) - public release-oriented history
- [`PATCH_NOTES.md`](PATCH_NOTES.md) - detailed Windows baseline and implementation notes
