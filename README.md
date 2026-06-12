# Markdown Studio Pro

Markdown Studio Pro is an early Markdown editor project focused on clean writing, readable documents, and a calm native desktop experience.

The goal is to build a simple but polished editor for writing Markdown without visual clutter. The current repository contains the first macOS source prototype, built with Swift, SwiftUI/AppKit, and a WKWebView-based editor.

This is not a finished app release yet. It is a working development version for testing, iteration, and feedback.

## Current status

- Early macOS source prototype
- Native macOS app shell
- Source-first GitHub release
- No packaged `.dmg` installer yet
- Windows version planned separately

## Platforms

```text
main
macos/      native macOS app
windows/    planned Windows app
```

The repository is structured for platform-specific desktop apps. The macOS version is currently active. A Windows version is planned for a later stage.

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

## Repository structure

```text
.
├─ AI.md
├─ README.md
├─ .gitignore
└─ macos/
   ├─ README.md
   ├─ MarkdownStudioProMac.xcodeproj/
   └─ MarkdownStudioProMac/
```

## macOS notes

See [macos/README.md](macos/README.md) for macOS-specific setup notes.

## AI-assisted development

This project uses an `AI.md` file to document project workflow, technical decisions, completed stages, and next steps.

See [AI.md](AI.md) for project notes.

## Known limitations

- This is not a finished public app release.
- No `.dmg` installer is provided yet.
- Some UI details and workflows are still experimental.
- Windows support is planned but not included yet.

## Roadmap

Planned next steps:

- add screenshots to the GitHub page
- continue macOS editor polish
- improve unsaved-change handling around app termination
- improve export and packaging workflows
- prepare a separate Windows shell
- add packaged macOS beta builds later

## Support

If you want to support the project:

[PayPal: @Shyiox](https://paypal.me/Shyiox)

## License

This project is licensed under the MIT License.
