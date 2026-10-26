# Private GitHub integration status — 2026-10-26

## Canonical local state after consolidation (2026-10-26)

- The private Windows portable `.NET 10` application with the approved A1 x A3 logo is the single intended everyday Windows app, located under `%LOCALAPPDATA%\Programs\Markdown Studio Pro` and reachable through Desktop and Start Menu shortcuts. Native launch, WebView2/editor readiness and normal clean close were confirmed earlier.
- The previously outdated **local** `main` was advanced by a clean Git fast-forward to the feature source. The five editable branding SVGs now have enforced LF working-tree line endings through `.gitattributes`, fixing exact asset-hash checks under Windows `core.autocrlf=true`. Verified on the local project after integration: 147 editor regressions, five portable package checks, four branding tests and successful Windows Debug build.
- The tested source remains on GitHub's separate **Draft PR #9**; GitHub public `main` has not been merged, tagged or released. Historic snapshots below describe earlier states, not the current local status.
- Generated old development builds, old delivery ZIPs and one redundant integration clone were inventoried for **explicit deletion approval**, including an old `.NET 8` WebView2 profile that may hold personal state. Never equate generated output with user documents or blindly wipe profiles, backups or unowned worktrees.

## Provenance

- GitHub public `main` at `13744540511f7d4f403149eceddaa149270a6ea8` (README v0.1.0 release link).
- Local Windows image assets baseline at `8af764744856868797036c70b3f80ab01b94a543` (descends from `d43aa88`).
- Modernization Revision 2 ZIP at SHA-256 `e6034ff0b2f64d3d529e0e4ac8b535d4858f84891deb8f048fe6c9ae7beea8bb`. Exactly 29 archive members were validated against the included SHA256SUMS manifest and applied to a dedicated integration clone.
- The two independent AI.md history additions and the GitHub README v0.1.0 release link were retained. No macOS implementation changes.

## Tests on the combined private integration source

- Markdown roundtrip: 39/39; editor interaction: 39/39; print/theme/export contract checks: 14/14 + 9/9.
- Native .NET regression suites: SaveSession 9/9, NativeRobustness 13/13, Asset 10/10, DocumentFileWriter 5/5, WebViewSecurity 9/9.
- Total: **147/147 automated regression checks passed**.
- Offline KaTeX/Mermaid extensions passed. Headless performance check: 1,200 paragraphs / 81,702 characters.
- Windows `win-x64` .NET 10 Debug build succeeded with zero errors; pre-existing nullable warning CS8603 remains.
- In earlier isolated native GUI QA, core editing, undo/redo, save, Find & Replace, close confirmation and reopen passed. This combined integration source was **not** separately subjected to the extended interactive GUI acceptance.

## Cross-platform logo update (A1 x A3)

- Replaced the two existing Windows application icon resources and all seven macOS AppIcon PNG sizes using the user-approved geometric A1 x A3 set. The five SVG source masters live in `branding/`; there are no retained old-logo duplicate files in the current candidate tree.
- Confirmed Windows ICO/PNG references in the .NET project, native AppWindow and About dialog. The icon was displayed in a real Windows test application with isolated user profile and an opened temporary test document; macOS icon assets were validated structurally but not compiled on macOS.
- New 4-case standalone branding test validates exact approved SHA-256 assets, PNG/ICO dimensions and platform references. The original source ZIP is archived once in the project-specific design Vault (outside the public repository), not kept in Downloads.
- Historical Git commits, earlier delivery ZIPs and the user's currently installed application are intentionally not rewritten or deleted. No app installation or public release has been performed.

## Installed Windows portable candidate and packaging correction

- Following approval of the A1 x A3 icon, the complete self-contained `.NET 10 win-x64` Windows release output was placed under `%LOCALAPPDATA%\Programs\Markdown Studio Pro`, with user Desktop and Start Menu shortcuts. The folder is portable: there is no MSI/MSIX, signing, updater or public release.
- An initial `dotnet publish -o` payload omitted `App.xbf`, `MainWindow.xbf` and `Markdown Studio Pro.pri` (504 files total). The missing resources produced a `MainWindow.InitializeComponent` XAML parse failure and a startup crash. The three files were obtained from the hash-identical Release build with generated resources; all **507 build files** now match the installed package SHA-256-for-SHA-256. The installed program starts with WebView2 and the editor ready. Extra WebView2 cache files generated during runtime are not part of the 507 packaged files.
- Future portable builds use `windows/build_portable_winui3.ps1`, which compiles the Release output and validates the XBF/PRI, executable, editor, loader and icon resources. `windows/portable_package.tests.ps1` checks five resource scenarios and is included in `windows/test_all.ps1`.
- The old .NET 8 project output remains as an intentional fallback until a separately approved cleanup. Settings and recent-file data hashes were unchanged by the smoke launch. The owned isolated QA clone and profile were removed; GitHub `main` and local checkout `main` remain untouched.

## Scope and release boundaries

For current **personal use**, extended GUI, accessibility, Smart-Tab, export and print acceptance are deferred according to owner decision. Confirmed data-loss defects are never considered low priority. Before publishing the source branch, the bundled `fastdom@1.0.12` MIT notice was completed from the upstream package's license information and the existing `khroma@2.1.0` MIT license was checked against the upstream repository (whose npm metadata does not declare a license field). Full packaged-public-release review remains separate. This integration is not an installed app or a public product release.

GitHub issue #8 tracks the deferred work. No installed application, personal Markdown documents, the existing local checkout's `main`, or the existing Revision 2 ZIP are changed by this isolated integration.
