# Markdown Studio Pro: Windows Editor Modernization (26 October 2026)

## Intent
Keep the established native WinUI 3 + WebView2 writing experience and keyboard/Smart-Tab contracts, but make the editor more reliable, secure, compatible with Markdown and suitable for verifiable releases. User explicitly approved stages 1–5 in chat and requested a **structure ZIP only**, not direct integration.

## Goals
1. Preserve documents under interrupted writes and detect external file changes before overwriting.
2. Restrict browser navigation, pop-ups and host-bridge messages, and reject dangerous Markdown URL schemes.
3. Extend CommonMark/GFM behavior incrementally with browser-level roundtrip tests; no unproven claim of full CommonMark compliance.
4. Improve modularity, performance checks, test entrypoints, release documentation and target the supported .NET LTS where verified.
5. Add footnotes, inline/block math, Mermaid live preview, and code syntax highlighting only through locally bundled, license-audited assets; preserve editable canonical source.

## Constraints and anti-goals
- Source baseline: `8af764744856868797036c70b3f80ab01b94a543` (main); Windows track only, macOS untouched.
- No direct change, checkout or clean to main; all production code only in staging. Deliver full replacement paths in a reviewable structure ZIP.
- No pushes, installs, releases, elevation, signing claims, or deletion of existing code/backups.
- No unforeseen keyboard shortcuts, changes to Smart-Tab or established input handling.
- The editor works offline, including all preview and export functionality.
- A stage may only be called completed when its tests/build and QA evidence support it.

## Approach
**Chosen:** bounded, tested improvements to existing WinUI 3 and WebView2 core, preserving the native shell.
Alternatives not chosen: full rebuild with Tiptap/ProseMirror (high regression/migration risk) or online/CDN previews (breaks offline behavior).

## Architecture
- `DocumentFileWriter`: isolated managed-file writes and conflict checks.
- `WebViewSecurityPolicy`: trust only own local editor origin; strict host bridge.
- `App/editor.html`: existing editor remains canonical until incremental parser/renderer modules can replace narrower responsibilities.
- Browser tests and .NET harness verify behavior before and after every change.
- Local preview dependencies must be vendored under `App/vendor` with licenses, with no runtime network requirement.
- Documentation and QA scripts provide operator-readable release evidence.

## Validation
Existing suites (roundtrip, interaction, stage5, stage7, AssetRegression, SaveSessionRegression, NativeRobustnessRegression), WebView security test, standards fixtures, Windows build and smoke where permitted. Preserve outputs and mark GUI/signing not run rather than claiming completion.
