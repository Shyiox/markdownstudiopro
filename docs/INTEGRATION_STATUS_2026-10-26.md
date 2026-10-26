# Private GitHub integration status — 2026-10-26

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

## Scope and release boundaries

For current **personal use**, extended GUI, accessibility, Smart-Tab, export and print acceptance are deferred according to owner decision. Confirmed data-loss defects are never considered low priority. Before publishing the source branch, the bundled `fastdom@1.0.12` MIT notice was completed from the upstream package's license information and the existing `khroma@2.1.0` MIT license was checked against the upstream repository (whose npm metadata does not declare a license field). Full packaged-public-release review remains separate. This integration is not an installed app or a public product release.

GitHub issue #8 tracks the deferred work. No installed application, personal Markdown documents, the existing local checkout's `main`, or the existing Revision 2 ZIP are changed by this isolated integration.
