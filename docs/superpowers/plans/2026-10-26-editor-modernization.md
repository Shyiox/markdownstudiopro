# Markdown Studio Pro 1–5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checklist syntax.

**Goal:** Harden the existing Windows Markdown editor and progressively add Markdown features without regressing the existing document workflow.

**Architecture:** Native WinUI 3 retains local file ownership; the WebView2 page handles structured editing and rendering. Add small independently testable helpers and keep modifications scoped to the cloned source tree.

**Tech Stack:** C#/.NET, WinUI 3/WebView2, HTML/JavaScript, Node/browser harness, PowerShell.

**Spec:** `docs/superpowers/specs/2026-10-26-editor-modernization-design.md`

## Global Constraints
- main remains untouched; source ZIP only; no merge/push/release.
- Preserve the editor's established keyboard/Smart-Tab workflow and WinUI shell.
- No CDN dependencies at runtime.
- Every completed stage updates `AI.md` and has regression/build evidence.

## Review Focus
- Interrupted filesystem writes must not truncate an existing file; verify conflict handling and temp cleanup.
- HTML links, images, bridge messages and navigation must reject unsafe schemes/untrusted origins.
- Markdown escaped punctuation, nested lists, quoted blocks and complex link URLs must roundtrip acceptably.
- Preview documents with malformed Mermaid/LaTeX cannot crash the editor or destroy source text.
- Existing document caret/undo behavior, exports, and assets must survive every change.

### Task 1: Integrity and Security
**Files:** create `DocumentFileWriter.cs`, `WebViewSecurityPolicy.cs`, test harnesses; modify `MainWindow.xaml.cs`, `App/editor.html`.
- [ ] Add failing tests for overwrite conflicts, interrupted writes, URL policies, malicious Markdown link schemes.
- [ ] Implement guarded atomic replacement, on-disk hash tracking and rejected WebView2 navigation/origins.
- [ ] Verify new tests; run all existing harnesses and build; document verified stage in `AI.md`.

### Task 2: Standards-Compatible Markdown
**Files:** modify `App/editor.html`, `tests/roundtrip.test.mjs` or add focused harness.
- [ ] Add failing actual-browser roundtrips for escaped syntax, autolinks, reference links, setext headings, tilde fences, balanced URL parentheses and tables containing escaped pipes.
- [ ] Extend the existing parser and serializer conservatively without changing keyboard behavior.
- [ ] Verify browser suite, all other suites/build; document completed vs deferred constructs in `AI.md`.

### Task 3: Maintenance and Runtime
**Files:** extract cohesive helpers only if regression-safe; add `tests/performance.test.mjs`; modify `*.csproj` if .NET 10 is compatible.
- [ ] Baseline and regression-test large-document responsiveness.
- [ ] Split deterministic non-interaction code into an explicitly loaded module if it preserves behavior.
- [ ] Check .NET 10 SDK / actual Windows build; migrate only if verified.
- [ ] Run full regression, record results and `AI.md`.

### Task 4: Release Readiness
**Files:** create test-runner/release verification scripts, revise README/CHANGELOG/PATCH_NOTES as needed.
- [ ] Add a one-command test and build runner with nonzero exit codes.
- [ ] Verify the runner exercises the full suite and reports exact build output.
- [ ] Document version/signing/installer state without falsely claiming publication; update `AI.md`.

### Task 5: Extended Markdown
**Files:** local vendor assets and licenses, editor integration, tests.
- [ ] Add failing Markdown roundtrip and preview tests for footnotes, inline/block math, Mermaid, and fenced-code highlighting.
- [ ] Implement offline render/persist functions without importing untrusted network scripts.
- [ ] Run all suites/build and smoke selected representations; document any uncompleted feature honestly in `AI.md`.

### Delivery
- [ ] Prepare clean structure ZIP of only changed/new full files (not Git metadata, build outputs or caches).
- [ ] Verify ZIP member hashes versus tested staged source and identify source baseline.
- [ ] Keep the user's production checkout unmodified; include application and test evidence.
