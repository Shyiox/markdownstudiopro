# Markdown Studio Pro - Windows

This workspace now keeps the Windows app focused on the WinUI 3 shell.

```text
MarkdownStudioPro.WinUI/    Windows app: WinUI 3 + WebView2
```

The old WinForms/WebView2 implementation has been removed from this workspace to avoid duplicate Windows app tracks.

## Requirements

- Windows with Microsoft Edge WebView2 Runtime
- .NET 8 SDK
- Visual Studio with Windows App SDK / WinUI workload for full IDE development

## Build

```bat
build_winui3.bat
```

## Run

```bat
start_winui3.bat
```

## Current Windows Surface

- Native WinUI CommandBar for all visible app commands
- WebView2 editor content with no duplicate web toolbar/menu shell
- New, Open, Save, Save As, and Recent Files
- Formatting, insertion, table row/column commands, source view, find/replace, HTML export, focus mode, and theme selection
- WebView2 startup overlay and diagnostic logs for startup failures
