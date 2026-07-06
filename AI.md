# AI.md

## Project workflow

- Check this file before each larger project step.
- Check Git status before patching when a Git checkout is available.
- Read relevant source files before changing them.
- Deliver changes as a structure ZIP with complete replacement files.
- Update this file when a stage is completed.

## Completed stages

### 2026-06-12 — macOS SwiftUI shell, stage 1

- Inspected the uploaded macOS project ZIP.
- No existing `AI.md` was present in the uploaded project.
- Git status could not be checked because the uploaded ZIP is not a Git repository.
- Kept the existing HTML/JS editor and existing file/menu bridge logic.
- Added a first SwiftUI shell layer around the existing `WKWebView` via `MacEditorShellView`.
- Reused `EditorWindowController` for window lifecycle, native menu actions, file handling, export, appearance, and WebView messaging.
- Added a SwiftUI top toolbar for common actions: new, open, save, insert table, insert code block, copy markdown, and appearance.

## Notes for next stages

- Build and run locally in Xcode with `Cmd+B` and `Cmd+R`.
- If the first stage is stable, later stages can move more chrome into SwiftUI or migrate to a full SwiftUI `App` entrypoint.
- Keep `App/editor.html` named exactly `editor.html`; avoid timestamped copies such as `editor.html 23-57-53-935.html`.

### 2026-06-12 — Windows floating UI cleanup, stage 4

- Removed the visible left sidebar/rail from the editor UI and consolidated actions into the bottom floating bar and centered modal.
- Reworked the floating bar into grouped buttons for Menu, Datei, Format, Einfügen, Export, and Theme.
- Rebuilt the old card-like action sheet into a centered modern modal with grouped list actions.
- Restored centered document title styling while keeping the divider line below the heading.
- Forced the side tools panel to stay closed by default and routed theme actions through the bottom controls and menu.

### 2026-06-12 — Windows modal/list cleanup, stage 5

- Replaced the card-like modal action buttons with flatter list rows and lighter separators.
- Reduced the global menu to primary groups and kept detailed table actions only in the Insert panel.
- Made the bottom Theme control neutral instead of a dominant primary action.
- Added a reliable visual divider under the first document heading, even when the default markdown only contains a title.


### 2026-06-12 — Windows command-bar flyouts, stage 6

- Converted grouped bottom controls from central modal behavior into compact bottom flyouts for Datei, Format, Einfügen, Export, and Theme.
- Reduced the global Menü view to quick actions plus small document stats.
- Kept the centered document title and softened the visual divider under the title.
- Moved Theme from instant toggle to a small theme picker flyout.

### 2026-06-12 — Windows WinUI 3 shell, stage 1

- Added a parallel `MarkdownStudioPro.WinUI` project as the Windows equivalent of the macOS SwiftUI shell direction.
- Kept the existing WinForms/WebView2 project intact; this is a future track, not a destructive replacement.
- Reused the current WebView2 `App/editor.html` editor core inside the WinUI shell.
- Added native WinUI `CommandBar`, file open/save/save-as routes, Markdown copy, source view, HTML export routing, and basic theme toggle.
- Added `build_winui3.bat`, `start_winui3.bat`, `README_WINUI3.md`, and `PATCH_NOTES.md`.
- Next intended stage: build locally on Windows, then jointly polish Fluent/Mica/command-surface look and parity features.

### 2026-06-12 — Windows WinUI 3 shell, startup hotfix

- Fixed a duplicate `ConfirmDiscardIfNeededAsync` declaration in `MainWindow.xaml.cs` that could break build/start.
- Added guarded WebView2 startup initialization and local startup/unhandled-exception log files.
- Updated WinUI build/start batch files so the console window stays open after errors instead of closing immediately.

### 2026-06-12 -- Windows WinUI 3 shell, Program entry hotfix

- Investigated reported WinUI build failure CS0101/CS0111 from generated App.g.i.cs.
- Identified stale hand-written Program.cs as the duplicate entry point; WinUI generates Program/Main from App.xaml.
- Updated the WinUI project to exclude Program.cs from compilation and changed scripts to delete the stale file before build/start.
- Updated Microsoft.WindowsAppSDK package version to the concrete version resolved by the user's restore log to remove the NU1603 version warning.

### 2026-06-12 — Windows WinUI 3 Shell, width/height hotfix

- Fixed WinUI build errors caused by WPF/WinForms-style `Width` and `Height` assignments on `MainWindow`.
- Replaced direct window size properties with `AppWindow.Resize(...)` via `WindowNative` and `Win32Interop`.
- Kept the change defensive so startup continues if window sizing is unavailable.

### 2026-06-12 — Windows WinUI 3 Shell, WebView loading hotfix

- Investigated the reported WinUI shell state where the native window starts but the editor area stays blank/dark.
- Added WebView2 navigation and process-failure diagnostics instead of relying only on the editor's `ready` web message.
- Added a visible loading overlay in the WinUI shell so editor-loading failures are surfaced in the window instead of silently showing an empty panel.
- Changed editor navigation to use an explicit file URI via `CoreWebView2.Navigate(new Uri(editorPath).AbsoluteUri)`.
- Added a guarded startup completion path from `NavigationCompleted` so the initial document loads even if the HTML bridge ready message is missed.

### 2026-06-12 — WinUI 3 icon parse hotfix

- Fixed WinUI startup XAML parse crash caused by invalid SymbolIcon string values in secondary AppBarButton commands.
- Removed risky secondary command Icon string attributes for Stage 1 so the shell can start without Symbol enum parse failures.
- Kept primary command icons unchanged because the crash log points at the secondary `Code` icon path.

### 2026-06-12 — WinUI 3 WebView2 initialization timeout hotfix

- Investigated the reported state where the WinUI shell stays indefinitely on `WebView2 wird vorbereitet …`.
- Moved editor startup from the `Window` constructor to the loaded visual tree so WebView2 initializes after the WinUI surface exists.
- Added a dedicated WebView2 user data folder under LocalAppData to avoid profile/path issues.
- Added explicit timeouts for WebView2 environment creation and WebView2 initialization so hangs now turn into visible errors and startup logs.

### 2026-06-12 — Windows WinUI 3 WebView2 API hotfix

- Fixed a WinUI/WebView2 build error by switching CoreWebView2Environment.CreateAsync from the unsupported two-argument overload to the compatible three-argument overload with explicit null options.
- Kept the WebView2 timeout/user-data-folder startup handling from the previous hotfix.

### 2026-06-12 - WinUI 3 WebView2 default environment hotfix

- Fixed target-machine build error where `CoreWebView2Environment.CreateAsync(...)` exposed neither the two-argument nor the three-argument overload in the installed WebView2 package combination.
- Removed custom WebView2 environment creation and switched to the compatible `EditorWebView.EnsureCoreWebView2Async()` default initialization path while keeping timeout/error reporting.

### 2026-06-12 - WinUI 3 WebView2 compile hotfix

- Fixed the WinUI WebView2 initialization compile error where `EnsureCoreWebView2Async()` returns a WinRT `IAsyncAction` rather than a `Task` in the target package combination.
- Removed the incompatible generic Task timeout wrapper from the WebView2 initialization call and awaited `EnsureCoreWebView2Async()` directly.
- Kept the visible startup error/logging flow for runtime diagnostics after compilation succeeds.

### 2026-06-12 - WinUI 3 WebView2 native loader/runtime hotfix

- Investigated runtime startup error `Das angegebene Modul wurde nicht gefunden` after WebView2 initialization reached runtime.
- Added explicit `Microsoft.Web.WebView2` package reference so `WebView2Loader.dll` is copied for the unpackaged WinUI shell.
- Removed stale timeout helper code no longer used after reverting to direct WinRT WebView2 initialization.
- Expanded the visible startup error text with WebView2-specific guidance, editor path, output path, exception type, and inner exception details.
- Added a non-blocking WebView2 Runtime hint to build/start scripts.

### 2026-06-25 - Windows WinUI cleanup

- Focused the Windows workspace on `MarkdownStudioPro.WinUI` only.
- Removed the legacy WinForms/WebView2 Windows project and launcher script from the active workspace.
- Made the native WinUI CommandBar the single visible command/menu surface.
- Added native WinUI table row/column commands.
- Removed the duplicate HTML rail, tools sidebar, hidden topbar, web menu sheet, web recent-files list, and related JavaScript.
- Removed non-functional Auto-Save and word-goal UI/settings paths from the WinUI editor.
- Kept WebView2 startup diagnostics, command palette, tab help, find/replace, export dialog, toast notifications, and status bar.
