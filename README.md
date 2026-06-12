# Markdown Studio Pro

Markdown Studio Pro is an early Markdown editor project focused on clean writing, readable documents, and a calm native desktop experience.

The current repository contains the first macOS source prototype. It is not a finished public release yet, but a working development version for testing, iteration, and feedback.

## Current status

- Early macOS source prototype
- Built with Swift/AppKit and WKWebView
- Can be built locally with Xcode
- No packaged DMG installer yet
- Windows version planned separately

## Build on macOS

Requirements:

- macOS
- Xcode

Open the Xcode project:

```bash
open macos/MarkdownStudioProMac.xcodeproj
```

Then build and run from Xcode.

## Repository structure

```text
.
├─ AI.md
├─ README.md
├─ macos/
│  ├─ README.md
│  ├─ MarkdownStudioProMac.xcodeproj/
│  └─ MarkdownStudioProMac/
└─ .gitignore
```

## macOS notes

See [macos/README.md](macos/README.md) for macOS-specific setup notes.

## AI-assisted development

This project uses an `AI.md` file to document project workflow, technical decisions, completed stages, and next steps.

## Support

If you want to support the project:

[PayPal: @Shyiox](https://paypal.me/Shyiox)

## License

This project is licensed under the MIT License.
