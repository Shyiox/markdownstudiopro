<p align="center">
  <img src="windows/MarkdownStudioPro.WinUI/Assets/markdown_studio_icon.png" width="96" alt="Markdown Studio Pro icon">
</p>

<h1 align="center">Markdown Studio Pro</h1>

<p align="center">
  A calm desktop Markdown editor built around focused writing, readable documents, and native platform behavior.
</p>

<p align="center">
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?logo=windows11&logoColor=white">
  <img alt="WinUI 3" src="https://img.shields.io/badge/WinUI-3-5C2D91?logo=windows&logoColor=white">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white">
  <img alt="WebView2" src="https://img.shields.io/badge/WebView2-editor-0F6CBD?logo=microsoftedge&logoColor=white">
  <img alt="MIT License" src="https://img.shields.io/badge/license-MIT-blue.svg">
</p>

---

Markdown Studio Pro combines a native desktop shell with a writing-first Markdown editor. The Windows version uses **WinUI 3** for the application chrome and **WebView2** for the document editor, keeping file handling and platform behavior native while preserving a flexible editing surface.

> **Current distribution:** source build. A public installer/package is not published yet.

## Windows highlights

- Native WinUI 3 shell with an Editorial Pro-style command surface
- Light and Dark themes with live settings preview
- Markdown, text, HTML export and print/PDF workflow
- White-paper print output even when the app is in Dark mode
- Improved print pagination for headings, tables, quotes and code blocks
- Clear save states: **Saved**, **Unsaved** and **Saving...**
- Auto-save support for already saved documents
- Open/recent-file workflows and optional last-document restore
- Window size and position persistence
- Drag & drop for `.md`, `.markdown` and `.txt` with a native drop surface
- Find & replace, source editing, command palette and Smart-Tab commands
- Focus mode, spellcheck, word goal and reading statistics
- Selection statistics for selected words and characters
- Configurable editor width, font size and line height with live preview

The existing keyboard, shortcut, Smart-Tab execution and editor key handling are intentionally preserved as part of the confirmed Windows baseline.

## Platform status

| Platform | Stack | Status |
| --- | --- | --- |
| Windows | WinUI 3 + WebView2 + .NET 8 | Active, current Editorial Pro/QoL track |
| macOS | SwiftUI + WKWebView | Separate source track; not changed by the Windows polish work |

## Build the Windows app

### Requirements

- Windows 10 version 2004 / build 19041 or newer
- x64
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime
- Visual Studio with Windows App SDK / WinUI tooling, if you prefer IDE builds

### Helper scripts

From the `windows` directory:

```bat
build_winui3.bat
start_winui3.bat
```

The build script restores and builds the self-contained `win-x64` app and checks that the executable, `editor.html`, and `WebView2Loader.dll` are present in the output.

### Direct build

```powershell
dotnet build .\windows\MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj -c Debug -r win-x64
```

More details are in [`windows/README_WINUI3.md`](windows/README_WINUI3.md).

## Changelog

User-facing release notes are tracked in [`CHANGELOG.md`](CHANGELOG.md). Detailed Windows implementation notes live in [`windows/PATCH_NOTES.md`](windows/PATCH_NOTES.md).

## Repository layout

```text
.
├─ AI.md
├─ CHANGELOG.md
├─ LICENSE
├─ README.md
├─ windows/
│  ├─ README_WINUI3.md
│  ├─ PATCH_NOTES.md
│  ├─ build_winui3.bat
│  ├─ start_winui3.bat
│  └─ MarkdownStudioPro.WinUI/
│     ├─ MarkdownStudioPro.WinUI.csproj
│     ├─ MainWindow.xaml
│     ├─ MainWindow.xaml.cs
│     ├─ App/editor.html
│     └─ Assets/
└─ macos/
   ├─ README.md
   ├─ MarkdownStudioProMac.xcodeproj/
   └─ MarkdownStudioProMac/
```

## Development notes

[`AI.md`](AI.md) records project decisions, completed stages, compatibility constraints, and the confirmed baseline for future work.

Windows should follow the product direction without replacing native Windows behavior with macOS-specific interaction patterns. Changes to established keyboard or editor key handling should only be made deliberately and tested separately.

## Support

If Markdown Studio Pro is useful to you, you can support the project via [PayPal @Shyiox](https://paypal.me/Shyiox).

## License

Markdown Studio Pro is released under the [MIT License](LICENSE).
