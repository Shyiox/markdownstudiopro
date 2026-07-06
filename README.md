# Markdown Studio Pro

Markdown Studio Pro is a native desktop Markdown editor focused on clean writing, readable documents, and a calm editor surface.

The macOS app is the lead version. Windows follows the same product direction and keeps feature parity where it makes sense for WinUI and WebView2.

## Current Status

- macOS is the lead platform and reference for product decisions.
- Windows is a WinUI 3 app with a native shell and WebView2 editor.
- The Windows app lives under `windows/MarkdownStudioPro.WinUI/`.
- Builds are source-based; no public installer package is included yet.

## Repository Structure

```text
.
├─ AI.md
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
│     ├─ App/
│     │  └─ editor.html
│     └─ Assets/
└─ macos/
   ├─ README.md
   ├─ MarkdownStudioProMac.xcodeproj/
   └─ MarkdownStudioProMac/
```

## Windows App

The Windows version currently includes:

- modern WinUI topbar with document status and native commands
- WebView2 Markdown editor
- open, save, save as, recent files, and export workflows
- self-contained Windows App SDK configuration
- dirty-state prompt on window close
- theme settings with improved editor contrast
- clean About dialog with contact and PayPal support link
- updated Markdown Studio Pro icon assets

Build:

```powershell
dotnet build .\windows\MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj
```

Run during development:

```powershell
dotnet run --project .\windows\MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj
```

Helper scripts are available in `windows/`:

- `windows/build_winui3.bat`
- `windows/start_winui3.bat`
- `windows/diagnose_winui3.bat`
- `windows/clean_project_artifacts.bat`

## macOS App

The macOS app remains the lead version and product reference.

Build with Xcode:

1. Open `macos/MarkdownStudioProMac.xcodeproj`.
2. Select the `MarkdownStudioProMac` scheme.
3. Build with `Cmd+B`.
4. Run with `Cmd+R`.

Command line:

```sh
xcodebuild -project macos/MarkdownStudioProMac.xcodeproj -scheme MarkdownStudioProMac -configuration Debug build
```

## Development Notes

`AI.md` documents the project direction, completed work, parity goals, and open follow-up items.

Windows should stay close to the macOS experience, but not by copying macOS UI literally. Native platform behavior should remain intact where it improves the Windows experience.

## Support

[PayPal: @Shyiox](https://paypal.me/Shyiox)

## License

This project is licensed under the MIT License.
