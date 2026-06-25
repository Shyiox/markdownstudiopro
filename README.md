# Markdown Studio Pro

Markdown Studio Pro is an early Markdown editor project focused on clean writing, readable documents, and a calm native desktop experience.

The goal is to build a simple but polished editor for writing Markdown without visual clutter. The current repository contains native desktop shells for macOS and Windows, both wrapping the Markdown Studio editor experience in platform-specific UI.

This is not a finished app release yet. It is a working development version for testing, iteration, and feedback.

## Current status

- Early macOS source prototype
- Windows WinUI 3 source version
- Native macOS and Windows app shells
- Source-first GitHub release
- No packaged `.dmg` installer yet
- No packaged Windows installer yet

## Platforms

```text
main
macos/      native macOS app
windows/    native Windows WinUI 3 app
```

The repository is structured for platform-specific desktop apps. The macOS and Windows versions can evolve independently while sharing editor ideas and workflow decisions.

## macOS prototype

The macOS app wraps the Markdown editor in a native desktop shell with:

- native macOS menu bar and toolbar
- document open, save, and save as
- support for Markdown and common text-based files
- light, dark, and system appearance modes
- table insertion modal
- code block support
- HTML export
- bundled editor HTML at `macos/MarkdownStudioProMac/App/editor.html`

## Windows WinUI version

The Windows app is under `windows/` and uses WinUI 3 with WebView2:

- native WinUI command bar
- native file open, save, save as, recent files, and export workflows
- WebView2 editor startup diagnostics
- cleaned single Windows app surface without legacy WinForms duplication
- bundled editor HTML at `windows/MarkdownStudioPro.WinUI/App/editor.html`

## Build on macOS

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

## Build on Windows

Requirements:

- Windows
- .NET SDK 8+
- Windows App SDK / WinUI workload
- WebView2 Runtime

Steps:

1. Open `windows/MarkdownStudioPro.WinUI/MarkdownStudioPro.WinUI.csproj` in Visual Studio, or use the scripts in `windows/`.
2. Build with:

```bat
cd windows
build_winui3.bat
```

3. Start with:

```bat
start_winui3.bat
```

## Repository structure

```text
.
├─ AI.md
├─ README.md
├─ .gitignore
├─ macos/
   ├─ README.md
   ├─ MarkdownStudioProMac.xcodeproj/
   └─ MarkdownStudioProMac/
└─ windows/
   ├─ README_WINUI3.md
   ├─ MarkdownStudioPro.WinUI/
   └─ build/start/diagnostic scripts
```

## macOS notes

See [macos/README.md](macos/README.md) for macOS-specific setup notes.
See [windows/README_WINUI3.md](windows/README_WINUI3.md) for Windows-specific setup notes.

## AI-assisted development

This project uses an `AI.md` file to document project workflow, technical decisions, completed stages, and next steps.

See [AI.md](AI.md) for project notes.

## Known limitations

- This is not a finished public app release.
- No `.dmg` installer is provided yet.
- Some UI details and workflows are still experimental.
- Windows support is source-based and not packaged as an installer yet.

## Roadmap

Planned next steps:

- add screenshots to the GitHub page
- continue macOS editor polish
- improve unsaved-change handling around app termination
- improve export and packaging workflows
- continue Windows WinUI polish and packaging
- add packaged macOS beta builds later

## Support

If you want to support the project:

[PayPal: @Shyiox](https://paypal.me/Shyiox)

## License

This project is licensed under the MIT License.
