# Markdown Studio Pro

Markdown Studio Pro is a native desktop Markdown editor. This repository is structured for platform-specific apps:

- `macos/` - native macOS app using Swift, SwiftUI, AppKit, and WKWebView
- `windows/` - planned Windows version

## macOS

The macOS app wraps the Markdown editor in a native shell with:

- native menu bar and toolbar
- document open, save, and save as
- support for common text-based files such as Markdown, TXT, JSON, YAML, HTML, CSS, JavaScript, Swift, Python, shell scripts, logs, CSV, and config files
- light, dark, and system appearance modes
- HTML export

### Build

Requirements:

- macOS
- Xcode

Steps:

1. Open `macos/MarkdownStudioProMac.xcodeproj`.
2. Select the `MarkdownStudioProMac` scheme.
3. Build with `Cmd+B`.
4. Run with `Cmd+R`.

From the command line:

```sh
xcodebuild -project macos/MarkdownStudioProMac.xcodeproj -scheme MarkdownStudioProMac -configuration Debug build
```

## Windows

The Windows version is planned and should live in `windows/` when added.

## Repository Notes

- Build output, DerivedData, user settings, and system files are ignored.
- The macOS editor HTML is bundled at `macos/MarkdownStudioProMac/App/editor.html`.
- No Node, Electron, Tauri, or Rust runtime is required for the current macOS app.

