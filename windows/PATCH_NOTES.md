# Markdown Studio Pro - Windows WinUI Cleanup

## Ziel

The Windows workspace is now centered on the WinUI 3 version only.

## Änderungen

- Removed the legacy WinForms Windows app track from the workspace.
- Kept `MarkdownStudioPro.WinUI` as the single Windows app.
- Moved table row/column commands into the native WinUI CommandBar.
- Removed the duplicate HTML command surface: rail, tools sidebar, hidden topbar, web menu sheet, and web recent-files list.
- Removed non-functional Auto-Save and word-goal UI paths from the WinUI editor.
- Kept useful editor dialogs and tools: command palette, tab help, find/replace, export dialog, toast notifications, and status bar.
- Kept WebView2 startup diagnostics and native loader output checks.

## Nachtest

```bat
build_winui3.bat
start_winui3.bat
```

Expected result: clean build, editor startup completes, and only the native WinUI CommandBar is visible for app menus.
