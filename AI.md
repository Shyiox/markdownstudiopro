# AI.md

## Project workflow

- Check this file before each larger project step.
- Check Git status before patching when a Git checkout is available.
- Read relevant source files before changing them.
- Deliver changes as a structure ZIP with complete replacement files.
- Update this file when a stage is completed.

## Completed stages

### 2026-08-24 - Windows Editorial Pro shell redesign, stage 1

- Redesigned only the Windows WinUI shell; macOS files were left untouched.
- Replaced the visible WinUI CommandBar with a custom Editorial Pro title bar and compact icon-first command surface.
- Preserved the existing file, formatting, insertion, table, search, export, focus, theme, settings, dirty-state, auto-save, and WebView2 command handlers.
- Added native-compatible custom title-bar handling with system caption buttons and a fallback when title-bar customization is unavailable.
- Refined the Windows-only editor frame, paper surface, heading rule, focus state, and status bar for the approved warm editorial direction.
- Kept the existing application icon unchanged; icon/logo redesign is deferred to a separate final stage.
- Static contract checks and XAML XML parsing pass in the patch environment. A Windows target-machine build and visual smoke test are still required because the patch environment does not provide the .NET/WinUI toolchain.


### 2026-08-24 - Windows QoL/UX pass, stage 1

- Updated only the Windows WinUI/WebView2 editor path; macOS files were not touched.
- Unified the visible save state across the native shell and WebView as `Gespeichert`, `Ungespeichert`, and transient `Speichert...`.
- Prevented Save/Save As from marking a document clean before the native write actually succeeds; cancelling Save As now preserves the dirty state.
- Preserved edits made while a save is in progress so a completed write cannot incorrectly clear newer unsaved changes.
- Extended the status bar with selected word/character counts when text is selected.
- Replaced visible status separators in the touched status strings with `|`.
- Stage-specific source/contract checks passed in the patch environment, and the Windows smoke test was reported without abnormalities before continuing.

### 2026-08-24 - Windows QoL/UX pass, stage 2

- Updated only the Windows WinUI settings flow; macOS files were not touched.
- Added live preview for editor width, font size, and line height alongside the existing live theme preview.
- Kept preview values transient until `Übernehmen`; cancelling restores theme, editor width, font size, and line height to the values present when the dialog opened.
- Reused the existing WebView settings bridge without changing editor mechanics or persisting unrelated settings during preview.
- Stage-specific contract checks passed in the patch environment, and continuation to the next stage was approved after the Windows smoke test.

### 2026-08-24 - Windows QoL/UX pass, stage 3

- Updated only the Windows WinUI session/settings flow; macOS files were not touched.
- Persisted window size and window position and restore the saved position only when a meaningful part of the window remains visible on an attached display; otherwise the existing centered fallback is used.
- Added the optional setting `Letztes Dokument beim Start öffnen`, disabled by default.
- When enabled, the last active document is reopened only as a startup fallback; an explicit launch path always takes precedence, including the case where that explicit path no longer exists.
- Missing remembered files fall back safely to a new document instead of blocking startup.
- Stage-specific contract checks passed in the patch environment, and the Windows smoke test was reported as passing before continuing.

### 2026-08-24 - Windows QoL/UX pass, stage 4

- Updated only the Windows WinUI file-drop path; macOS files were not touched.
- Added drag and drop for exactly one `.md`, `.markdown`, or `.txt` file and routed accepted files through the existing document-load path.
- Reused the existing unsaved-changes confirmation before replacing the current document; unsupported files and multi-file drops leave the current document untouched.
- Added a transient drop overlay over the existing editor surface for valid single-file drags, with no new toolbar buttons, sidebars, or editor mechanics.
- The overlay disappears immediately on drag leave or drop and is not shown as a positive state for unsupported/multiple files.
- Stage-specific contract/regression checks passed in the patch environment, and the Windows smoke test including the drop overlay was reported as passing before continuing.

### 2026-08-24 - Windows QoL/UX pass, final visual polish

- Kept the confirmed Stage 4 Windows behavior as the functional baseline; macOS files were not touched.
- Fully rolled back the attempted keyboard/accessibility Stage 5 changes after they interfered with the existing keyboard setup. Existing keyboard, Smart-Tab execution, shortcut, and editor keydown behavior must not be changed unless explicitly requested.
- Refined the `Suchen und Ersetzen` backdrop so the editor is softly blurred without the previous heavy dark overlay.
- Restyled the Smart-Tab command help as a calmer compact single-column command list without changing Smart-Tab behavior.
- Added a print-specific light paper palette so document content and preview remain white with dark text independently of the app Light/Dark theme; the surrounding Windows print UI remains system-controlled.
- Improved print pagination with heading keep-with-next behavior, widow/orphan control, and break avoidance for code blocks, quotes, tables, table rows, and similar grouped content while preserving explicit manual page breaks.
- The final visual/print polish changed only `windows/MarkdownStudioPro.WinUI/App/editor.html`; `MainWindow.xaml`, `MainWindow.xaml.cs`, and macOS were left unchanged for this polish step.
- The Windows visual smoke test for the final polish was reported as looking good. The requested Windows QoL/UX pass is considered complete at this checkpoint.

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

### 2026-09-10 - Windows V2 migration, phase 1 regression hardening

- Updated only the Windows WinUI/WebView2 implementation; macOS files were left untouched.
- Repaired the confirmed Smart-Tab command defects without changing the established keyboard assignments or general editor keydown behavior.
- Preserved table snippet identity through Smart Tab so table-specific follow-up behavior remains available after expansion.
- Repaired the HTML export command path so `Ctrl+E` and native export routing reach the existing export dialog instead of recursing through the command dispatcher.
- Prevented transient code-block language labels from being serialized into saved Markdown.
- Repaired task-list insertion from the command palette and preserved the active table context when deleting columns.
- Refreshed dirty state from the live editor immediately before destructive New/Open/Recent/Drop flows so unsaved changes are not missed by stale native state.
- Preserved explicit launch-path precedence and the existing keyboard/shortcut contract.
- Added/ran focused regression checks for the repaired editor contracts; all 9 checks passed in the patch environment and JavaScript syntax validation passed.
- The resulting candidate was smoke-tested on Windows by the user and reported to work without observed problems. Phase 1 is complete.

### 2026-09-10 - Windows diagnostics foundation

- Added a Windows-only diagnostics foundation that initializes before WinUI XAML startup and writes immediate per-session startup markers under `%LOCALAPPDATA%\Markdown Studio Pro\Diagnostics`.
- Added coverage for WinUI unhandled exceptions, AppDomain unhandled exceptions, unobserved task exceptions, WebView2 runtime/version startup state, and guarded step-level startup logging without recording editor/script result contents.
- Added `collect_diagnostics.ps1` to package the scoped diagnostics logs and relevant Windows crash events without collecting `settings.json`, `recent.json`, or Markdown document contents.
- The diagnostics path was verified on the Windows target machine and successfully isolated the Phase 2 startup crash to the new `ThemeShadow` receiver path before later revealing a separate saved-window-position WinRT projection exception.

### 2026-09-10 - Windows V2 migration, phase 2 native paper and Acrylic boundary

- Updated only the Windows WinUI/WebView2 implementation; macOS files were left untouched.
- Introduced a fixed native floating paper boundary and moved the WebView2 editor plus transient loading/drop overlays inside that paper surface.
- Kept document scrolling inside the paper viewport while the native paper boundary itself remains stationary.
- Replaced the previous shell backdrop path with guarded Desktop Acrylic, including activation handling, disposal, and an opaque fallback when the system backdrop is unavailable.
- Kept the existing editor-width preference connected to the native paper width while preserving the existing command surface, typography defaults, editing mechanics, keyboard behavior, and Phase 1 regression fixes.
- Removed the Phase 2 `ThemeShadow` receiver implementation after Windows diagnostics proved that path caused the native `0xC000027B` startup termination on the target machine. Shadow styling remains deferred to a safer Composition-based implementation.
- Repaired saved window-position validation by replacing `DisplayArea.FindAll()` enumeration, which failed on the target machine with WinRT `0x80004002`, with direct `DisplayArea.GetFromRect(..., DisplayAreaFallback.None)` resolution while preserving the existing minimum-visible-area guard.
- Cumulative Phase 2/diagnostics contract checks, XAML parsing, and editor JavaScript syntax validation pass in the patch environment.
- The final Phase 2 candidate was built successfully and smoke-tested on Windows. The resulting startup trace shows `MainWindow.ConfigureWindow`, Desktop Acrylic configuration, WebView2 initialization, and editor startup all completing without the previous fallback exception or startup crash. Two duplicate CS8603 nullable warnings remain at the WebView2 script-result return site; they are non-fatal and intentionally left unchanged in the exact smoke-tested Phase 2 code.

### 2026-09-10 - Windows V2 migration, phase 3 light resting composition

- Updated only the Windows WinUI/WebView2 presentation; macOS files were left untouched.
- Removed the previous stacked native title/toolbar rows from the resting composition and replaced them with the centered floating command band used by the Windows V2 reference direction.
- Calibrated the canonical 100% reference geometry around a 686 x 58 command band at a 57 px top offset and an 848 px floating paper beginning at 152 px, with the paper remaining fixed while document content scrolls internally.
- Kept native Windows caption buttons and window dragging intact while routing the established file, formatting, insertion, export, settings, focus, recent-file, and auxiliary actions through the compact band and its overflow menu.
- Added visible undo/redo commands to the band while preserving the existing WebView2/contenteditable editing model and established keyboard behavior.
- Reworked the editor presentation toward the approved light editorial reference, including the 648 px document measure, serif document typography, centered first-heading treatment, restrained status presentation, and the subtle overlay scrollbar inside the paper viewport.
- Removed the experimental ambient prose from the resting shell after visual review; the outside Acrylic surface now stays intentionally quiet when the editor is ready.
- Added a startup-only document skeleton inside the paper. It uses low-opacity, slow fade animation, appears only during genuine editor initialization, and is stopped/hidden once editor startup completes; normal New/Open/Recent document operations do not restart it.
- Further calibrated the Light Desktop Acrylic and translucent command-band material so the real desktop remains perceptible behind the fixed opaque paper without using the reference image as a bundled wallpaper.
- Phase 3 visual/contract checks, XAML parsing, handler checks, and editor JavaScript syntax validation pass in the patch environment.
- The final Phase 3 candidate was built and visually smoke-tested on Windows by the user in Light mode, including the startup skeleton and resting composition. The supplied startup trace completes `ConfigureWindow`, Desktop Acrylic setup, WebView2 initialization, and `Editor startup completed` without exceptions or fallback errors. Phase 3 is complete.

### 2026-09-12 - Windows V2 Etappe 1 Markdown data integrity

- Repaired BUG-003 through BUG-007 in the WinUI WebView2 Markdown roundtrip: empty table cells and rows, literal inline code, formatted task items, fenced-code blank lines, ordered-list starts, nested lists, multi-paragraph blockquotes, hardbreaks, unknown frontmatter fields, and table alignment now survive save and reload.
- Added a dependency-free Edge/Chrome roundtrip suite that loads the real current `App/editor.html` and covers 14 focused regressions plus a baseline Markdown sanity case; all 15 tests pass across two serialization cycles.
- Modified `windows/MarkdownStudioPro.WinUI/App/editor.html` and created `windows/MarkdownStudioPro.WinUI/tests/roundtrip.test.mjs`; this `AI.md` entry records the verified result.
- Editor and harness JavaScript syntax checks pass. The Windows Debug `win-x64` build completes with 0 errors and the pre-existing CS8603 nullable warning.
- UI design, keyboard behavior, native shell code, and all other audit groups were left unchanged in Etappe 1.

### 2026-09-12 - Windows V2 Etappe 2 Save and session safety

- Repaired BUG-014 by preserving explicit launch intent separately from path validity. An existing explicit file wins over session restore; a missing explicit path opens a new document instead of silently restoring the last file.
- Repaired BUG-015 with a small `DocumentSessionCoordinator`: document generations invalidate completions from replaced documents, content revisions identify the saved snapshot, monotonically increasing operation IDs reject older completions after a newer one wins, and an active-operation set keeps the saving state true until every save ends. Manual saves and Auto-Save use the same path.
- Repaired BUG-016 by keeping the Save As snapshot separate from live editor content. A successful write advances the file path and saved baseline for that snapshot without replacing newer editor content; newer content remains dirty. Picker cancellation removes only its save operation.
- Serialized physical file writes through a narrow `SemaphoreSlim` gate while retaining operation checks before each write, preventing an older delayed picker/save from writing after a newer save has already completed.
- Hardened the related load-transfer path: native path, baseline, and dirty state are committed only after the WebView accepts the loaded Markdown; failures restore the previous native document state. Starting a load or new document also invalidates outstanding save completions.
- Added the dependency-free persistent native harness under `windows/MarkdownStudioPro.WinUI/tests/SaveSessionRegression/`, linked directly to the production coordinator. The documented red run failed the missing-launch-intent, stale-save, replaced-document, and newer-edit Save As cases; the final run passes 9/9.
- Updated the WinUI project compile items so the nested harness and its generated `obj` sources remain isolated from the application build.
- Verification passed: Etappe 2 harness 9/9, Etappe 1 real-`editor.html` roundtrip 15/15, inline editor JavaScript syntax 1/1, Windows Debug `win-x64` build with 0 errors and the pre-existing CS8603 warning, and GUI smoke coverage for normal save, Save As, Save As cancellation, new/open/reopen, session restore, explicit existing launch, and explicit missing launch.
- The GUI run also reproduced an unrelated existing WebView2 `GpuProcessExited`/nested-ContentDialog shutdown while the Settings dialog was open. It was diagnosed and left unchanged because it is outside Etappe 2. No other audit group, UI design, Markdown parser/serializer, macOS code, commit, push, or PR was touched for this stage.

### 2026-09-12 - Windows V2 Etappe 3 editor interaction and history

- Repaired BUG-001 by keeping command-palette rows connected during hover. Hover now updates only the selected row classes and ARIA state, so a normal pointer sequence reaches the same target and executes the selected command exactly once.
- Repaired BUG-002 by routing keyboard selection through the same targeted selection updater and scrolling only the active row into view with `scrollIntoView({ block: 'nearest' })`; filtering, hover handoff, ArrowUp/ArrowDown, Enter, Escape, and the no-results state retain their existing routing.
- Repaired BUG-008 with one central native editing transaction for app-owned programmatic DOM mutations. The mutation is staged, then committed as a single browser `insertHTML` transaction with editor selection restoration, so existing browser Undo/Redo handles Bold, Italic, headings, Smart-Tab insertion, and table row/column changes without a parallel custom history stack.
- Repaired BUG-010 by validating the complete trimmed dimension string as decimal digits before numeric conversion. Columns remain limited to 1-12 and rows to 1-50; fractional, exponential, empty, alphabetic, negative, zero, and oversized values are rejected with the existing validation message.
- Added `windows/MarkdownStudioPro.WinUI/tests/interaction.test.mjs`, a dependency-free Edge/Chrome harness against the real current `App/editor.html`. Its documented red run exposed the pointer disconnect, invisible keyboard selection, missing programmatic Undo/Redo, and decimal truncation; the final suite passes 18/18.
- Modified only `windows/MarkdownStudioPro.WinUI/App/editor.html` and `AI.md`, and created the interaction harness. No macOS files, global shortcut ordering, broad keyboard handling, Smart-Tab command matching, external dependencies, commit, push, or PR were added.
- Verification passed: Etappe 3 interaction suite 18/18, Etappe 1 roundtrip 15/15, Etappe 2 SaveSessionRegression 9/9, editor JavaScript syntax 1/1, and Windows Debug `win-x64` build with 0 errors plus the pre-existing CS8603 warning.
- Native GUI input automation was unavailable in the session, so Ctrl+K mouse/keyboard and editor Undo/Redo gestures were not claimed as interactively tested. The built EXE was started successfully; its window responded and diagnostics recorded successful WebView navigation and `Editor startup completed`. The already parked delayed-close behavior recurred and was left unchanged for Etappe 6.

### 2026-09-12 - Windows V2 Etappe 4 Find and Replace state consistency

- Repaired BUG-009 in the existing WinUI WebView2 Find/Replace flow without redesigning the dialog or changing global keyboard routing.
- Confirmed both root causes: `doSearch()` selected the first match without updating `findState.currentIndex`, while Replace and Replace All could act on stale `findState` values instead of the currently visible query and search options.
- Added a central control-to-state synchronization step before Next, Replace, and Replace All. Search recomputation now resets the old index, successful selection sets the matching index and visible position, and no-match or Replace-All completion clears stale selection state.
- Removed the delayed initial re-search on dialog reopen so visible query, stored query, match list, current index, selection, and status are consistent immediately.
- Extended the dependency-free real-`editor.html` interaction harness with persistent regressions for Enter then Replace, query-change Replace, query-change Replace All, fresh-dialog Replace All without Enter, no-match safety, Next/wrap state, query reset, match-count reduction, and dialog reopen. The documented red run failed 8 BUG-009 scenarios; the final suite passes 30/30.
- Verification passed: Etappe 4/3 interaction suite 30/30, Etappe 1 roundtrip 15/15, Etappe 2 SaveSessionRegression 9/9, editor JavaScript syntax 1/1, and Windows Debug `win-x64` build with 0 errors plus the pre-existing CS8603 warning.
- Native GUI testing is `BLOCKED - native Computer Use unavailable`: the capability inventory returned `apps: []` and exposed only browser surfaces. No native GUI PASS result is claimed and the EXE was not launched through a browser substitute.
- Modified only the BUG-009 region of `windows/MarkdownStudioPro.WinUI/App/editor.html`, its existing interaction test harness, and this project note. No other audit group, macOS file, external dependency, commit, push, PR, or Etappe 5 work was included.

### 2026-09-12 - Windows V2 Etappe 5 export, print, and theme bridge

- Verified BUG-011 against the current Working Tree before patching. The previously described Dark-print failure is already prevented by the existing parsed `@media print` layer, which forces a light color scheme, white page/document backgrounds, dark content/headings/table text, and readable link/code/table colors with print-priority declarations. No redundant print production change was made.
- Repaired BUG-012 at the source: the export dialog's `Markdown (.md)` action and its fallback now route to the existing `exportMarkdownFile` bridge command with the current `toMarkdown()` payload. The separate `copyMarkdown` action and HTML/PDF mappings remain unchanged.
- Repaired BUG-013 by routing toggle and explicit Command Palette Light/Dark choices through one `requestThemeChange` path. It updates the WebView immediately without writing a competing localStorage theme, then posts `updateSetting(theme)` to the existing native handler, which normalizes the canonical setting, updates the WinUI shell/menu state, and persists through `SaveSettings()` for startup reuse.
- Added `windows/MarkdownStudioPro.WinUI/tests/stage5.test.mjs`, a dependency-free Edge/Chrome harness against the real current `App/editor.html`. It covers parsed print rules in Dark, Light, and Dark-Light-Dark states; Markdown/Copy/HTML/PDF mappings and live payloads; explicit palette Light/Dark bridge messages; and Native Settings-to-Web updates without bridge echo or Web-only persistence.
- The documented red run passed all three print cases and failed exactly the Markdown file-export mapping plus the two explicit palette-theme bridge cases (6/9). The final Etappe-5 suite passes 9/9.
- Verification passed: Etappe 5 suite 9/9, Etappe 1 roundtrip 15/15, Etappe 2 SaveSessionRegression 9/9, Etappe 3/4 interaction 30/30, editor JavaScript syntax 1/1, Windows Debug `win-x64` build with 0 errors plus the pre-existing CS8603 warning, and `git diff --check`.
- Native GUI testing is `BLOCKED - native Computer Use unavailable`: the capability inventory returned `apps: []` and exposed only browser surfaces. No native export, print-preview, theme-sync, or persistence PASS result is claimed, and no browser substitute was presented as a native GUI test.
- Modified only the relevant BUG-012/013 paths in `windows/MarkdownStudioPro.WinUI/App/editor.html`, added the focused Etappe-5 harness, and updated this project note. No macOS file, external dependency, Etappe 6 item, commit, push, or PR was included.

### 2026-09-12 - Windows V2 Etappe 6 native robustness

- Repaired BUG-017 at the verified control-flow break: `ShowSettingsAsync()` awaited the latest Preview task before its Cancel branch, so a Preview exception skipped rollback. Settings completion now preserves the dialog-open appearance snapshot, rolls native paper and WebView state back after Cancel or any Preview failure, and commits only after an accepted Preview completes successfully.
- Repaired BUG-018 with a central safe awaited UI boundary. Every relevant native `async void` event routes awaited work through it; operation context and original exceptions reach Diagnostics, error-reporting failures are contained and diagnosed, and the former fire-and-forget WebView startup task is observed through the same boundary.
- Serialized every native `ContentDialog.ShowAsync()` through one small async queue. A WebView/native error raised while Settings or another dialog is active waits for that modal operation to finish, so concurrent `ShowAsync()` calls cannot collide; distinct errors remain separately diagnosed and are presented serially.
- Hardened the directly related clean WebView transfer boundary by adopting the editor's canonical Markdown as both current and saved baseline after a successful clean transfer. This removed the false dirty state that made an untouched new document enter the save prompt during the pre-fix shutdown smoke.
- Added idempotent Window cleanup that cancels pending startup work, stops timers, detaches Window/XamlRoot/AppWindow/WebView events, closes WebView2, releases Acrylic and transient surfaces, logs cleanup stages, and continues cleanup if one resource release fails.
- The pre-fix process smoke exceeded 10 seconds because the unexpected dirty state left a save prompt active; it did not prove a post-`Closed` process leak. After the fixes, three separate five-cycle fresh-build runs exited normally, with the final run completing each close in 139-156 ms. Diagnostics show `MainWindow.ShutdownCleanup` completing on every cycle. User settings/recent data were backed up and restored byte-for-byte/through the persistent harness.
- Added the dependency-free persistent suite under `windows/MarkdownStudioPro.WinUI/tests/NativeRobustnessRegression/`; its documented red runs failed first on missing robustness primitives and the clean-transfer policy, and the final suite passes 13/13.
- Full verification passed: Etappe 6 13/13, Etappe 1 15/15, Etappe 2 9/9, Etappe 3/4 30/30, Etappe 5 13/13, inline JavaScript syntax 1/1, Windows Debug `win-x64` build with 0 errors, `git diff --check`, and repeated shutdown 5/5. The existing CS8603 warning at the WebView script-result return remains unchanged.
- Native GUI testing is `BLOCKED - native Computer Use unavailable`: the capability inventory returned `apps: []`. Settings Cancel/Save/repeated cycles and native dialog-error reentrancy are therefore not claimed as GUI PASS; the safe GPU/error condition was not manually reproduced.
- Etappe 7 remains unchanged: `Drucken...`/`Ctrl+P`, Find/Replace Previous/Next button group, and manual Light/Dark Print Preview after print access. Final adversarial audit remains Etappe 8. No Etappe 7/8 work, macOS change, external dependency, commit, push, or PR was included.

### 2026-09-13 - Windows V2 Etappe 7 feature and UX completion

- Made the existing print path directly reachable from the native main menu through `Drucken…` and from an app-owned `Ctrl+P` keyboard accelerator. Both entry points converge on the existing `PrintPdfAsync()` command, which continues through the established `printPdf` editor command to `window.print()` and reuses the Etappe-5 print CSS.
- Kept `AreBrowserAcceleratorKeysEnabled = false`; `Ctrl+K` and the established editor keyboard ordering remain unchanged. The native accelerator owns only `Ctrl+P`.
- Replaced the single visible `Weitersuchen` control with a compact Previous/Next group labelled `Vorheriger Treffer` and `Nächster Treffer`. Both directions reuse the BUG-009 query/options/match/index state, wrap correctly, preserve the exact current replacement target, and remain safe for zero or one match.
- Added the focused `windows/MarkdownStudioPro.WinUI/tests/stage7.test.mjs` contract suite and extended the real-editor interaction suite. The documented red run failed on the missing menu route, missing native accelerator, and missing Previous control; the final runs pass 5/5 and 37/37.
- Full automated verification passed: Etappe 7 5/5, roundtrip 15/15, SaveSession 9/9, interaction 37/37, Etappe 5 13/13, NativeRobustness 13/13, inline JavaScript syntax 1/1, Windows Debug `win-x64` build with 0 errors, and `git diff --check`. The existing CS8603 warning at the WebView script-result return remains unchanged.
- Native GUI verification is pending the explicit Etappe-7 GUI gate. Menu print, `Ctrl+P`, `Ctrl+K`, Light/Dark Print Preview, and Find/Replace pointer/keyboard behavior are not yet claimed as GUI PASS.
- Frontmatter/metadata UX remains a separate feature. A possibly legacy/dead earlier settings-theme-preview rollback path remains parked for the final adversarial audit in Etappe 8. No Etappe-8 work, macOS change, external dependency, commit, push, or PR was included.

### 2026-09-13 - Windows V2 final release audit

- Completed the final automated adversarial audit on the current Windows-only working tree; macOS files were not changed.
- Confirmed and corrected one stale test expectation only: the Etappe-5 export-dialog PDF assertion still expected the intentionally removed `window.print()` path. It now verifies the canonical `printPdf` native-host bridge. No production code changed, so no user-manual retest was introduced.
- Fresh final automated evidence: roundtrip 15/15, SaveSession 9/9, interaction 37/37, Etappe 5 13/13, NativeRobustness 13/13, Etappe 7 9/9, editor JavaScript syntax pass, Debug win-x64 build with 0 errors, and `git diff --check` pass (only known line-ending notices).
- The ten-cycle shutdown smoke passed 10/10 with each close completing in 102-141 ms; the harness backed up and restored its scoped AppData settings/recent state and no test process remained.
- Static review found the native print route canonical (`ShowPrintUI`), WebView browser accelerators disabled, download-flyout suppression non-cancelling and cleaned up, dialog queueing and UI error boundaries in place, and no productive `window.print()` legacy route. The existing non-fatal CS8603 warning at the WebView script-result return site remains unchanged.
- Native GUI evidence is `USER-MANUAL PASS` for editor/history/Smart Tab, tables/find, palette/shortcuts, theme/persistence, print/PDF/download, file/session, and dialogs/shutdown. Codex native Computer Use remains unavailable and was intentionally not retried in the final audit.
- Release assessment: GO -- no release-blocking issues found. Deferred: Document Information / Metadata UX for frontmatter. No commit, push, or PR was created.

### 2026-09-14 - v0.1.0 release follow-up

- Updated the README distribution notice to link the published Windows x64 portable ZIP directly and also link the v0.1.0 release notes.
- Prepared a closing comment for Issue #3 documenting the implemented WinUI 3, WebView2, .NET 8, shared-editor, and native file-handling decisions.
- Submitted the documentation change through a dedicated review branch and pull request. Issue #3 remains open until the pull request is reviewed and merged.
