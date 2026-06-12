import Cocoa
import SwiftUI
import WebKit

final class EditorWindowController: NSWindowController, WKScriptMessageHandler, WKNavigationDelegate, NSToolbarDelegate {
    private let webView: WKWebView
    private var currentFileURL: URL?
    private var lastKnownMarkdown: String = ""
    private var lastSavedMarkdown: String = ""
    private var isClosingAfterConfirmation = false
    private var editorReady = false
    private var pendingMarkdown: String?
    private var appearanceMode: String = "system"

    private enum ToolbarIdentifier {
        static let main = NSToolbar.Identifier("MarkdownStudioProMainToolbar")
        static let newDocument = NSToolbarItem.Identifier("MarkdownStudioProToolbarNewDocument")
        static let openDocument = NSToolbarItem.Identifier("MarkdownStudioProToolbarOpenDocument")
        static let saveDocument = NSToolbarItem.Identifier("MarkdownStudioProToolbarSaveDocument")
        static let insert = NSToolbarItem.Identifier("MarkdownStudioProToolbarInsert")
        static let copyMarkdown = NSToolbarItem.Identifier("MarkdownStudioProToolbarCopyMarkdown")
        static let appearance = NSToolbarItem.Identifier("MarkdownStudioProToolbarAppearance")
    }

    init() {
        let configuration = WKWebViewConfiguration()
        let contentController = WKUserContentController()
        let shim = """
        (function(){
          if (!window.chrome) window.chrome = {};
          if (!window.chrome.webview) {
            const listeners = [];
            window.chrome.webview = {
              postMessage: function(message) {
                try { window.webkit.messageHandlers.markdownStudio.postMessage(message || {}); } catch (e) {}
              },
              addEventListener: function(type, handler) {
                if (type === 'message' && typeof handler === 'function') listeners.push(handler);
              },
              __dispatchMessage: function(data) {
                const event = { data: data };
                listeners.slice().forEach(function(handler){ try { handler(event); } catch(e) {} });
              }
            };
          }
          window.MarkdownStudioMac = {
            getMarkdown: function(){ return window.markdownStudio && window.markdownStudio.toMarkdown ? window.markdownStudio.toMarkdown() : ''; },
            setMarkdown: function(markdown){ if (window.markdownStudio && window.markdownStudio.setMarkdown) window.markdownStudio.setMarkdown(markdown || ''); }
          };
        })();
        """
        contentController.addUserScript(WKUserScript(source: shim, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        configuration.userContentController = contentController

        webView = WKWebView(frame: .zero, configuration: configuration)
        webView.setValue(false, forKey: "drawsBackground")
        webView.wantsLayer = true
        webView.layer?.backgroundColor = NSColor.clear.cgColor

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 860, height: 760),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Ohne Titel — Markdown Studio Pro"
        window.minSize = NSSize(width: 720, height: 560)
        window.titlebarAppearsTransparent = false
        window.titleVisibility = .visible
        window.toolbarStyle = .unified
        window.isMovableByWindowBackground = false
        window.backgroundColor = NSColor.windowBackgroundColor
        window.center()

        super.init(window: window)
        window.contentView = NSHostingView(rootView: MacEditorShellView(
            webView: webView,
            newDocument: { [weak self] in self?.newDocument(nil) },
            openDocument: { [weak self] in self?.openDocument(nil) },
            saveDocument: { [weak self] in self?.saveDocument(nil) },
            copyMarkdown: { [weak self] in self?.copyMarkdown(nil) },
            insertTable: { [weak self] in self?.insertTable(nil) },
            insertCodeBlock: { [weak self] in self?.insertCodeBlock(nil) },
            toggleLightAppearance: { [weak self] in self?.useLightAppearance(nil) },
            toggleDarkAppearance: { [weak self] in self?.useDarkAppearance(nil) }
        ))
        webView.navigationDelegate = self
        contentController.add(self, name: "markdownStudio")
        window.delegate = self
        configureToolbar()
        loadEditor()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func configureToolbar() {
        let toolbar = NSToolbar(identifier: ToolbarIdentifier.main)
        toolbar.delegate = self
        toolbar.displayMode = .iconOnly
        toolbar.allowsUserCustomization = true
        toolbar.autosavesConfiguration = true
        toolbar.sizeMode = .regular
        window?.toolbar = toolbar
    }

    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        [
            ToolbarIdentifier.newDocument,
            ToolbarIdentifier.openDocument,
            ToolbarIdentifier.saveDocument,
            ToolbarIdentifier.insert,
            .flexibleSpace,
            ToolbarIdentifier.copyMarkdown,
            ToolbarIdentifier.appearance
        ]
    }

    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        [
            ToolbarIdentifier.newDocument,
            ToolbarIdentifier.openDocument,
            ToolbarIdentifier.saveDocument,
            ToolbarIdentifier.insert,
            .flexibleSpace,
            ToolbarIdentifier.copyMarkdown,
            ToolbarIdentifier.appearance
        ]
    }

    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier itemIdentifier: NSToolbarItem.Identifier, willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
        switch itemIdentifier {
        case ToolbarIdentifier.newDocument:
            return toolbarButton(identifier: itemIdentifier, label: "Neu", symbolName: "doc.badge.plus", action: #selector(newDocument(_:)))
        case ToolbarIdentifier.openDocument:
            return toolbarButton(identifier: itemIdentifier, label: "Öffnen", symbolName: "folder", action: #selector(openDocument(_:)))
        case ToolbarIdentifier.saveDocument:
            return toolbarButton(identifier: itemIdentifier, label: "Speichern", symbolName: "square.and.arrow.down", action: #selector(saveDocument(_:)))
        case ToolbarIdentifier.insert:
            return insertToolbarMenuItem(identifier: itemIdentifier)
        case ToolbarIdentifier.copyMarkdown:
            return toolbarButton(identifier: itemIdentifier, label: "Markdown kopieren", symbolName: "doc.on.doc", action: #selector(copyMarkdown(_:)))
        case ToolbarIdentifier.appearance:
            return appearanceToolbarMenuItem(identifier: itemIdentifier)
        default:
            return nil
        }
    }

    private func toolbarButton(identifier: NSToolbarItem.Identifier, label: String, symbolName: String, action: Selector) -> NSToolbarItem {
        let item = NSToolbarItem(itemIdentifier: identifier)
        item.label = label
        item.paletteLabel = label
        item.toolTip = label
        item.image = NSImage(systemSymbolName: symbolName, accessibilityDescription: label)
        item.target = self
        item.action = action
        return item
    }

    private func insertToolbarMenuItem(identifier: NSToolbarItem.Identifier) -> NSToolbarItem {
        let item = NSMenuToolbarItem(itemIdentifier: identifier)
        item.label = "Einfügen"
        item.paletteLabel = "Einfügen"
        item.toolTip = "Einfügen"
        item.image = NSImage(systemSymbolName: "plus", accessibilityDescription: "Einfügen")

        let menu = NSMenu(title: "Einfügen")
        menu.addItem(NSMenuItem(title: "Tabelle…", action: #selector(insertTable(_:)), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Codeblock", action: #selector(insertCodeBlock(_:)), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Überschrift 1", action: #selector(formatHeading1(_:)), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Überschrift 2", action: #selector(formatHeading2(_:)), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Zitat", action: #selector(formatQuote(_:)), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Trennlinie", action: #selector(insertHorizontalRule(_:)), keyEquivalent: ""))
        menu.items.forEach { $0.target = self }
        item.menu = menu
        return item
    }

    private func appearanceToolbarMenuItem(identifier: NSToolbarItem.Identifier) -> NSToolbarItem {
        let item = NSMenuToolbarItem(itemIdentifier: identifier)
        item.label = "Darstellung"
        item.paletteLabel = "Darstellung"
        item.toolTip = "Darstellung"
        item.image = NSImage(systemSymbolName: "circle.lefthalf.filled", accessibilityDescription: "Darstellung")

        let menu = NSMenu(title: "Darstellung")
        menu.addItem(NSMenuItem(title: "System", action: #selector(useSystemAppearance(_:)), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Hell", action: #selector(useLightAppearance(_:)), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Dunkel", action: #selector(useDarkAppearance(_:)), keyEquivalent: ""))
        menu.items.forEach { $0.target = self }
        item.menu = menu
        return item
    }

    private func loadEditor() {
        guard let url = Bundle.main.url(forResource: "editor", withExtension: "html", subdirectory: "App") else {
            showAlert(title: "Editor nicht gefunden", message: "App/editor.html konnte nicht im App-Bundle gefunden werden.")
            return
        }
        webView.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent())
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any], let type = body["type"] as? String else { return }
        switch type {
        case "ready":
            editorReady = true
            injectMacPolish()
            if let pending = pendingMarkdown {
                setMarkdown(pending)
                pendingMarkdown = nil
            } else if lastKnownMarkdown.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                setMarkdown(createNewDocumentMarkdown())
            } else {
                refreshWindowTitle()
            }
        case "changed":
            if let markdown = body["markdown"] as? String {
                lastKnownMarkdown = markdown
                updateDocumentEditedState(using: markdown)
                updateWindowTitle(using: markdown)
            }
        case "new":
            newDocument(nil)
        case "open":
            openDocument(nil)
        case "save":
            if let markdown = body["markdown"] as? String {
                lastKnownMarkdown = markdown
                save(markdown: markdown, askForLocation: currentFileURL == nil)
            } else {
                saveDocument(nil)
            }
        case "saveAs":
            if let markdown = body["markdown"] as? String {
                lastKnownMarkdown = markdown
                save(markdown: markdown, askForLocation: true)
            } else {
                saveDocumentAs(nil)
            }
        case "copyMarkdown":
            if let markdown = body["markdown"] as? String {
                copyText(markdown)
                lastKnownMarkdown = markdown
                updateWindowTitle(using: markdown)
            }
        case "showSource":
            if let markdown = body["markdown"] as? String {
                showMarkdownSource(markdown)
                lastKnownMarkdown = markdown
                updateWindowTitle(using: markdown)
            } else {
                showSource(nil)
            }
        case "exportHtml":
            if let html = body["html"] as? String {
                export(html: html)
            } else {
                exportHTML(nil)
            }
        default:
            break
        }
    }

    private func injectMacPolish() {
        let script = """
        (function(){
          document.documentElement.style.colorScheme = 'light dark';
          document.body.classList.add('mac-wrapper');
          var style = document.getElementById('msp-macos-polish');
          if (!style) {
            style = document.createElement('style');
            style.id = 'msp-macos-polish';
            document.head.appendChild(style);
          }
          style.textContent = `
            html, body {
              min-height: 100vh;
              background: #f3efe8 !important;
              -webkit-font-smoothing: antialiased;
              text-rendering: optimizeLegibility;
            }
            body.mac-wrapper {
              background: #f3efe8 !important;
              color: #24211d !important;
              font-family: -apple-system, BlinkMacSystemFont, "SF Pro Text", "SF Pro Display", system-ui, sans-serif !important;
            }
            body.mac-wrapper .app-shell,
            body.mac-wrapper .main,
            body.mac-wrapper .editor-frame {
              background: transparent !important;
            }
            body.mac-wrapper .app-shell {
              display: block !important;
              min-height: 100vh !important;
            }
            body.mac-wrapper .editor-frame {
              min-height: 100vh;
              padding: 0 !important;
            }
            body.mac-wrapper .document {
              width: 100% !important;
              min-height: 100vh !important;
              margin: 0 auto !important;
              padding: 54px 68px 112px !important;
              border-radius: 0 !important;
              background: #fffaf2 !important;
              color: #24211d !important;
              border: 0 !important;
              box-shadow: none !important;
              line-height: 1.76 !important;
            }
            body.mac-wrapper .document:focus {
              border-color: transparent !important;
              box-shadow: none !important;
            }
            body.mac-wrapper .document .dochead {
              display: block !important;
              padding-bottom: 1rem !important;
              margin-bottom: 1.4rem !important;
              border-bottom-color: rgba(60, 60, 67, .12) !important;
            }
            body.mac-wrapper .document .dochead .label {
              display: none !important;
            }
            body.mac-wrapper .document .dochead h1,
            body.mac-wrapper .document .dochead .tags,
            body.mac-wrapper .document .dochead p {
              text-align: center !important;
            }
            body.mac-wrapper .document h1,
            body.mac-wrapper .document h2,
            body.mac-wrapper .document h3 {
              letter-spacing: -0.03em !important;
            }
            body.mac-wrapper .document a { color: #0a84ff !important; }
            body.mac-wrapper .document code:not(pre code) {
              background: rgba(204, 120, 92, .10) !important;
              color: #8a4a36 !important;
              border-color: rgba(204, 120, 92, .22) !important;
            }
            body.mac-wrapper .document pre {
              background: #101218 !important;
              color: #faf9f5 !important;
              border: 1px solid rgba(125,211,252,.18) !important;
              box-shadow: none !important;
            }
            body.mac-wrapper .document pre code {
              background: transparent !important;
              color: inherit !important;
              border: 0 !important;
            }
            body.mac-wrapper .document table {
              border-color: rgba(60, 60, 67, .12) !important;
            }
            body.mac-wrapper .document th {
              background: rgba(204, 120, 92, .10) !important;
              color: #342b24 !important;
            }
            body.mac-wrapper .document td {
              background: rgba(255,250,242,.88) !important;
            }
            @media (min-width: 980px) {
              html, body,
              body.mac-wrapper {
                background: linear-gradient(180deg, #f2eee7 0%, #eae4db 100%) !important;
              }
              body.mac-wrapper .editor-frame {
                padding: 28px 22px 44px !important;
              }
              body.mac-wrapper .document {
                width: min(820px, 100%) !important;
                min-height: calc(100vh - 72px) !important;
                border-radius: 16px !important;
                background: rgba(255,250,242,.96) !important;
                border: 1px solid rgba(129, 112, 91, .16) !important;
                box-shadow: 0 24px 80px rgba(56, 45, 34, .10), 0 1px 0 rgba(255,255,255,.72) inset !important;
              }
              body.mac-wrapper .document:focus {
                border-color: rgba(10, 132, 255, .24) !important;
                box-shadow: 0 28px 90px rgba(0, 0, 0, .12), 0 0 0 3px rgba(10, 132, 255, .12), 0 1px 0 rgba(255,255,255,.8) inset !important;
              }
            }
            @media (prefers-color-scheme: dark) {
              html:not(.mac-force-light),
              html:not(.mac-force-light) body {
                background: #151517 !important;
              }
              html:not(.mac-force-light) body.mac-wrapper {
                background: #242426 !important;
                color: #f4f4f2 !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .document {
                background: #242426 !important;
                color: #f4f4f2 !important;
                border-color: transparent !important;
                box-shadow: none !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .document:focus {
                border-color: transparent !important;
                box-shadow: none !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .document .dochead {
                border-bottom-color: rgba(255,255,255,.08) !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .document .dochead .label,
              html:not(.mac-force-light) body.mac-wrapper .document .dochead .tags {
                color: rgba(235, 235, 245, .68) !important;
              }
              html:not(.mac-force-light) body.mac-wrapper a { color: #0a84ff !important; }
              html:not(.mac-force-light) body.mac-wrapper code:not(pre code) {
                background: rgba(125, 211, 252, .14) !important;
                color: #bae6fd !important;
                border-color: rgba(125, 211, 252, .22) !important;
              }
              html:not(.mac-force-light) body.mac-wrapper pre {
                background: #0b0d12 !important;
                color: #f8fafc !important;
                border-color: rgba(125,211,252,.24) !important;
              }
              html:not(.mac-force-light) body.mac-wrapper th {
                background: rgba(125,211,252,.12) !important;
                color: #f8fafc !important;
              }
              html:not(.mac-force-light) body.mac-wrapper td {
                background: rgba(255,255,255,.035) !important;
              }
            }
            @media (prefers-color-scheme: dark) and (min-width: 980px) {
              html:not(.mac-force-light),
              html:not(.mac-force-light) body,
              html:not(.mac-force-light) body.mac-wrapper {
                background: linear-gradient(180deg, #19191c 0%, #101114 100%) !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .document {
                background: rgba(36,36,38,.96) !important;
                border-color: rgba(244,244,242,.10) !important;
                box-shadow: 0 24px 80px rgba(0,0,0,.36), 0 1px 0 rgba(255,255,255,.06) inset !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .document:focus {
                border-color: rgba(125,211,252,.30) !important;
                box-shadow: 0 28px 90px rgba(0,0,0,.44), 0 0 0 3px rgba(125,211,252,.16), 0 1px 0 rgba(255,255,255,.06) inset !important;
              }
            }
            html.mac-force-light,
            html.mac-force-light body,
            body.mac-wrapper.mac-force-light {
              background: #f3efe8 !important;
              color: #24211d !important;
            }
            body.mac-wrapper.mac-force-light .document {
              background: #fffaf2 !important;
              color: #24211d !important;
              border-color: transparent !important;
            }
            @media (min-width: 980px) {
              html.mac-force-light,
              html.mac-force-light body,
              body.mac-wrapper.mac-force-light {
                background: linear-gradient(180deg, #f2eee7 0%, #eae4db 100%) !important;
              }
              body.mac-wrapper.mac-force-light .document {
                background: rgba(255,250,242,.96) !important;
                border-color: rgba(129, 112, 91, .16) !important;
                box-shadow: 0 24px 80px rgba(56, 45, 34, .10), 0 1px 0 rgba(255,255,255,.72) inset !important;
              }
            }
            html.mac-force-dark,
            html.mac-force-dark body,
            body.mac-wrapper.mac-force-dark {
              background: #242426 !important;
              color: #f4f4f2 !important;
            }
            body.mac-wrapper.mac-force-dark .document {
              background: #242426 !important;
              color: #f4f4f2 !important;
              border-color: transparent !important;
            }
            @media (min-width: 980px) {
              html.mac-force-dark,
              html.mac-force-dark body,
              body.mac-wrapper.mac-force-dark {
                background: linear-gradient(180deg, #19191c 0%, #101114 100%) !important;
              }
              body.mac-wrapper.mac-force-dark .document {
                background: rgba(36,36,38,.96) !important;
                border-color: rgba(244,244,242,.10) !important;
                box-shadow: 0 24px 80px rgba(0,0,0,.36), 0 1px 0 rgba(255,255,255,.06) inset !important;
              }
            }
            body.mac-wrapper.mac-force-dark .document .dochead {
              border-bottom-color: rgba(255,255,255,.08) !important;
            }
            body.mac-wrapper.mac-force-dark .document .dochead .label,
            body.mac-wrapper.mac-force-dark .document .dochead .tags {
              color: rgba(235, 235, 245, .68) !important;
            }
            body.mac-wrapper.mac-force-dark a { color: #0a84ff !important; }
            body.mac-wrapper.mac-force-dark code:not(pre code) {
              background: rgba(125,211,252,.14) !important;
              color: #bae6fd !important;
              border-color: rgba(125,211,252,.22) !important;
            }
            body.mac-wrapper.mac-force-dark pre {
              background: #0b0d12 !important;
              color: #f8fafc !important;
              border-color: rgba(125,211,252,.24) !important;
            }
            body.mac-wrapper .table-backdrop {
              background: rgba(38, 33, 27, .18) !important;
              backdrop-filter: blur(18px) saturate(1.18) !important;
              -webkit-backdrop-filter: blur(18px) saturate(1.18) !important;
            }
            body.mac-wrapper .table-modal {
              width: min(360px, calc(100vw - 44px)) !important;
              padding: 22px !important;
              border-radius: 18px !important;
              background: rgba(255, 250, 242, .86) !important;
              color: #211d19 !important;
              border: 1px solid rgba(129, 112, 91, .18) !important;
              box-shadow: 0 26px 90px rgba(50, 40, 30, .22), 0 1px 0 rgba(255,255,255,.74) inset !important;
              backdrop-filter: blur(30px) saturate(1.22) !important;
              -webkit-backdrop-filter: blur(30px) saturate(1.22) !important;
            }
            body.mac-wrapper .table-modal h2,
            body.mac-wrapper .table-modal h3 {
              margin-top: 0 !important;
              color: #211d19 !important;
              letter-spacing: -0.025em !important;
            }
            body.mac-wrapper .table-modal p,
            body.mac-wrapper .table-field label {
              color: rgba(36, 33, 29, .66) !important;
            }
            body.mac-wrapper .table-field label {
              font-size: 12px !important;
              font-weight: 600 !important;
            }
            body.mac-wrapper .table-input {
              height: 34px !important;
              border-radius: 10px !important;
              background: rgba(255, 253, 248, .82) !important;
              color: #211d19 !important;
              border: 1px solid rgba(129, 112, 91, .22) !important;
              box-shadow: 0 1px 0 rgba(255,255,255,.78) inset !important;
            }
            body.mac-wrapper .table-input:focus {
              border-color: rgba(10, 132, 255, .55) !important;
              box-shadow: 0 0 0 3px rgba(10,132,255,.18), 0 1px 0 rgba(255,255,255,.78) inset !important;
              outline: none !important;
            }
            body.mac-wrapper .table-actions button,
            body.mac-wrapper .table-modal button {
              border-radius: 10px !important;
              font-weight: 600 !important;
            }
            body.mac-wrapper.mac-force-dark .table-backdrop {
              background: rgba(0, 0, 0, .34) !important;
              backdrop-filter: blur(20px) saturate(1.08) !important;
              -webkit-backdrop-filter: blur(20px) saturate(1.08) !important;
            }
            body.mac-wrapper.mac-force-dark .table-modal {
              background: rgba(36,36,38,.86) !important;
              color: #f4f4f2 !important;
              border-color: rgba(244,244,242,.14) !important;
              box-shadow: 0 30px 96px rgba(0,0,0,.52), 0 1px 0 rgba(255,255,255,.06) inset !important;
            }
            body.mac-wrapper.mac-force-dark .table-modal h2,
            body.mac-wrapper.mac-force-dark .table-modal h3 {
              color: #f4f4f2 !important;
            }
            body.mac-wrapper.mac-force-dark .table-input {
              background: rgba(21,21,23,.78) !important;
              color: #f4f4f2 !important;
              border-color: rgba(244,244,242,.18) !important;
              box-shadow: 0 1px 0 rgba(255,255,255,.05) inset !important;
            }
            body.mac-wrapper.mac-force-dark .table-modal p,
            body.mac-wrapper.mac-force-dark .table-field label {
              color: rgba(235,235,245,.68) !important;
            }
            @media (prefers-color-scheme: dark) {
              html:not(.mac-force-light) body.mac-wrapper .table-backdrop {
                background: rgba(0, 0, 0, .34) !important;
                backdrop-filter: blur(20px) saturate(1.08) !important;
                -webkit-backdrop-filter: blur(20px) saturate(1.08) !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .table-modal {
                background: rgba(36,36,38,.86) !important;
                color: #f4f4f2 !important;
                border-color: rgba(244,244,242,.14) !important;
                box-shadow: 0 30px 96px rgba(0,0,0,.52), 0 1px 0 rgba(255,255,255,.06) inset !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .table-modal h2,
              html:not(.mac-force-light) body.mac-wrapper .table-modal h3 {
                color: #f4f4f2 !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .table-input {
                background: rgba(21,21,23,.78) !important;
                color: #f4f4f2 !important;
                border-color: rgba(244,244,242,.18) !important;
                box-shadow: 0 1px 0 rgba(255,255,255,.05) inset !important;
              }
              html:not(.mac-force-light) body.mac-wrapper .table-modal p,
              html:not(.mac-force-light) body.mac-wrapper .table-field label {
                color: rgba(235,235,245,.68) !important;
              }
            }
            body.mac-wrapper .rail,
            body.mac-wrapper .tools,
            body.mac-wrapper .topbar,
            body.mac-wrapper .menu-backdrop,
            body.mac-wrapper .menu-sheet,
            body.mac-wrapper .statusbar,
            body.mac-wrapper .toast-stack {
              display: none !important;
            }
            body.mac-wrapper .main {
              display: block !important;
              min-height: 100vh !important;
            }
            @media (max-width: 980px) {
              body.tools-open .app-shell { grid-template-columns: minmax(0,1fr) !important; }
              body.mac-wrapper .editor-frame { padding: 14px !important; }
              body.mac-wrapper .document {
                width: 100% !important;
                min-height: calc(100vh - 28px) !important;
                padding: 40px 28px 92px !important;
              }
            }
          `;

          if (!window.__markdownStudioMacTitleBridgeInstalled) {
            window.__markdownStudioMacTitleBridgeInstalled = true;
            var sendMarkdownChanged = function(){
              clearTimeout(window.__markdownStudioMacTitleTimer);
              window.__markdownStudioMacTitleTimer = setTimeout(function(){
                var markdown = '';
                try {
                  if (window.markdownStudio && typeof window.markdownStudio.toMarkdown === 'function') {
                    markdown = window.markdownStudio.toMarkdown();
                  } else if (window.MarkdownStudioMac && typeof window.MarkdownStudioMac.getMarkdown === 'function') {
                    markdown = window.MarkdownStudioMac.getMarkdown();
                  }
                } catch (error) {}
                try {
                  window.webkit.messageHandlers.markdownStudio.postMessage({ type: 'changed', markdown: markdown });
                } catch (error) {}
              }, 180);
            };
            document.addEventListener('input', sendMarkdownChanged, true);
            document.addEventListener('keyup', sendMarkdownChanged, true);
            document.addEventListener('mouseup', sendMarkdownChanged, true);
            sendMarkdownChanged();
          }
        })();
        """
        webView.evaluateJavaScript(script) { [weak self] _, _ in
            self?.applyAppearanceModeToEditor()
        }
    }

    private func getMarkdown(_ completion: @escaping (String) -> Void) {
        let js = "(window.markdownStudio && window.markdownStudio.toMarkdown ? window.markdownStudio.toMarkdown() : (window.MarkdownStudioMac && window.MarkdownStudioMac.getMarkdown ? window.MarkdownStudioMac.getMarkdown() : ''))"
        webView.evaluateJavaScript(js) { result, _ in
            let markdown = result as? String ?? self.lastKnownMarkdown
            self.lastKnownMarkdown = markdown
            completion(markdown)
        }
    }

    private func getMarkdownNoSideEffect(_ completion: @escaping (String) -> Void) {
        let js = "(window.markdownStudio && window.markdownStudio.toMarkdown ? window.markdownStudio.toMarkdown() : '')"
        webView.evaluateJavaScript(js) { result, _ in
            completion(result as? String ?? self.lastKnownMarkdown)
        }
    }

    private func setMarkdown(_ markdown: String) {
        if !editorReady {
            pendingMarkdown = markdown
            return
        }
        let base64 = Data(markdown.utf8).base64EncodedString()
        let js = "window.markdownStudio && window.markdownStudio.setMarkdownBase64 ? window.markdownStudio.setMarkdownBase64('\(base64)', {source:'mac'}) : window.chrome.webview.__dispatchMessage({type:'setMarkdownBase64', base64:'\(base64)'});"
        webView.evaluateJavaScript(js) { [weak self] _, _ in
            self?.lastKnownMarkdown = markdown
            self?.lastSavedMarkdown = markdown
            self?.window?.isDocumentEdited = false
            self?.updateWindowTitle(using: markdown)
        }
    }

    @objc func newDocument(_ sender: Any?) {
        currentFileURL = nil
        window?.representedURL = nil
        setMarkdown(createNewDocumentMarkdown())
        window?.isDocumentEdited = false
    }

    @objc func openDocument(_ sender: Any?) {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.resolvesAliases = true
        panel.message = "Text- oder Markdown-Datei öffnen"
        guard let window = window else { return }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            self?.openFile(url)
        }
    }

    func openFile(_ url: URL) {
        do {
            let markdown = try readTextFile(url)
            currentFileURL = url
            window?.representedURL = url
            setMarkdown(markdown)
        } catch {
            showAlert(title: "Öffnen fehlgeschlagen", message: error.localizedDescription)
        }
    }

    private func readTextFile(_ url: URL) throws -> String {
        let resourceValues = try url.resourceValues(forKeys: [.isRegularFileKey])
        guard resourceValues.isRegularFile == true else {
            throw NSError(domain: "MarkdownStudioPro", code: 3, userInfo: [NSLocalizedDescriptionKey: "Es können nur Dateien geöffnet werden."])
        }

        let data = try Data(contentsOf: url)
        guard !looksLikeBinary(data) else {
            throw NSError(domain: "MarkdownStudioPro", code: 4, userInfo: [NSLocalizedDescriptionKey: "\(url.lastPathComponent) sieht nicht wie eine Textdatei aus."])
        }

        if data.isEmpty {
            return ""
        }

        let encodings: [String.Encoding] = [
            .utf8,
            .utf16,
            .utf16LittleEndian,
            .utf16BigEndian,
            .utf32,
            .utf32LittleEndian,
            .utf32BigEndian,
            .isoLatin1,
            .windowsCP1252,
            .macOSRoman
        ]

        for encoding in encodings {
            if let text = String(data: data, encoding: encoding) {
                return stripByteOrderMark(from: text)
            }
        }

        throw NSError(domain: "MarkdownStudioPro", code: 5, userInfo: [NSLocalizedDescriptionKey: "\(url.lastPathComponent) konnte nicht als Text gelesen werden."])
    }

    private func looksLikeBinary(_ data: Data) -> Bool {
        guard !data.isEmpty else { return false }
        let sample = data.prefix(4096)

        if sample.contains(0) {
            let bytes = Array(sample)
            let evenNulls = stride(from: 0, to: bytes.count, by: 2).filter { bytes[$0] == 0 }.count
            let oddNulls = stride(from: 1, to: bytes.count, by: 2).filter { bytes[$0] == 0 }.count
            let utf16LikeThreshold = max(2, bytes.count / 4)
            if evenNulls < utf16LikeThreshold && oddNulls < utf16LikeThreshold {
                return true
            }
        }

        let suspiciousControlBytes = sample.filter { byte in
            byte < 0x09 || (byte > 0x0D && byte < 0x20)
        }.count
        return Double(suspiciousControlBytes) / Double(sample.count) > 0.08
    }

    private func stripByteOrderMark(from text: String) -> String {
        if text.first == "\u{feff}" {
            return String(text.dropFirst())
        }
        return text
    }

    @objc func saveDocument(_ sender: Any?) {
        getMarkdown { markdown in
            self.save(markdown: markdown, askForLocation: self.currentFileURL == nil)
        }
    }

    @objc func saveDocumentAs(_ sender: Any?) {
        getMarkdown { markdown in
            self.save(markdown: markdown, askForLocation: true)
        }
    }

    private func save(markdown: String, askForLocation: Bool) {
        if !askForLocation, let url = currentFileURL {
            writeMarkdown(markdown, to: url)
            return
        }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.plainText]
        panel.nameFieldStringValue = suggestedFilename(for: markdown)
        panel.message = "Markdown-Datei speichern"
        guard let window = window else { return }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            self?.writeMarkdown(markdown, to: url)
        }
    }

    @discardableResult
    private func writeMarkdown(_ markdown: String, to url: URL) -> Bool {
        do {
            guard let data = markdown.data(using: .utf8) else {
                throw NSError(domain: "MarkdownStudioPro", code: 1, userInfo: [NSLocalizedDescriptionKey: "Markdown konnte nicht als UTF-8 kodiert werden."])
            }
            try data.write(to: url, options: [.atomic])
            currentFileURL = url
            lastKnownMarkdown = markdown
            lastSavedMarkdown = markdown
            window?.representedURL = url
            window?.isDocumentEdited = false
            updateWindowTitle(using: markdown)
            return true
        } catch {
            showAlert(title: "Speichern fehlgeschlagen", message: error.localizedDescription)
            return false
        }
    }

    @objc func exportHTML(_ sender: Any?) {
        webView.evaluateJavaScript("document.documentElement.outerHTML") { result, _ in
            if let html = result as? String { self.export(html: html) }
        }
    }

    private func export(html: String) {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.html]
        let base = currentFileURL?.deletingPathExtension().lastPathComponent ?? titleFromMarkdown(lastKnownMarkdown) ?? "Dokument"
        panel.nameFieldStringValue = sanitizedFilenameStem(base) + ".html"
        panel.message = "HTML exportieren"
        guard let window = window else { return }
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let url = panel.url else { return }
            do {
                guard let data = html.data(using: .utf8) else {
                    throw NSError(domain: "MarkdownStudioPro", code: 2, userInfo: [NSLocalizedDescriptionKey: "HTML konnte nicht als UTF-8 kodiert werden."])
                }
                try data.write(to: url, options: [.atomic])
            } catch {
                self.showAlert(title: "Export fehlgeschlagen", message: error.localizedDescription)
            }
        }
    }

    @objc func copyMarkdown(_ sender: Any?) {
        getMarkdown { markdown in self.copyText(markdown) }
    }

    @objc func showSource(_ sender: Any?) {
        getMarkdown { markdown in self.showMarkdownSource(markdown) }
    }

    private func showMarkdownSource(_ markdown: String) {
        let alert = NSAlert()
        alert.messageText = "Markdown-Quelle"
        alert.informativeText = markdown.isEmpty ? "Keine Quelle vorhanden." : markdown
        alert.alertStyle = .informational
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }

    @objc func formatBold(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('bold'); else document.execCommand('bold', false, null);")
    }

    @objc func formatItalic(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('italic'); else document.execCommand('italic', false, null);")
    }

    @objc func formatStrikethrough(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('strike'); else document.execCommand('strikeThrough', false, null);")
    }

    @objc func insertInlineCode(_ sender: Any?) {
        runEditorJavaScript("""
        (function(){
          if (typeof startInlineCodeMode === 'function') {
            startInlineCodeMode();
            return;
          }

          var selection = window.getSelection();
          if (!selection || selection.rangeCount === 0) {
            document.execCommand('insertText', false, 'INL');
            return;
          }

          var range = selection.getRangeAt(0);
          var documentNode = document.querySelector('.document[contenteditable="true"], .document, [contenteditable="true"]');
          if (!documentNode || !documentNode.contains(range.commonAncestorContainer)) {
            document.execCommand('insertText', false, 'INL');
            return;
          }

          var code = document.createElement('code');
          code.className = 'inline-code';
          code.setAttribute('data-inline-code', 'true');

          if (range.collapsed) {
            code.textContent = '';
            range.insertNode(code);
            var inside = document.createRange();
            inside.selectNodeContents(code);
            inside.collapse(true);
            selection.removeAllRanges();
            selection.addRange(inside);
          } else {
            code.appendChild(range.extractContents());
            range.insertNode(code);
            var after = document.createRange();
            after.setStartAfter(code);
            after.collapse(true);
            selection.removeAllRanges();
            selection.addRange(after);
          }

          documentNode.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'formatSetBlockTextDirection' }));
        })();
        """)
    }

    @objc func insertCodeBlock(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('code'); else document.execCommand('insertText', false, 'CODE');")
    }

    @objc func formatHeading1(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('h1'); else document.execCommand('formatBlock', false, 'h1');")
    }

    @objc func formatHeading2(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('h2'); else document.execCommand('formatBlock', false, 'h2');")
    }

    @objc func formatQuote(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('quote'); else document.execCommand('formatBlock', false, 'blockquote');")
    }

    @objc func insertLink(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('link'); else { var url = prompt('Link URL'); if (url) document.execCommand('createLink', false, url); }")
    }

    @objc func insertTable(_ sender: Any?) {
        runEditorJavaScript("if (typeof insertTablePrompt === 'function') insertTablePrompt(); else if (typeof runFormat === 'function') runFormat('table');")
    }

    @objc func insertHorizontalRule(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('hr'); else document.execCommand('insertHorizontalRule', false, null);")
    }

    @objc func insertTaskList(_ sender: Any?) {
        runEditorJavaScript("if (typeof runFormat === 'function') runFormat('task'); else document.execCommand('insertText', false, '- [ ] Aufgabe');")
    }

    @objc func useSystemAppearance(_ sender: Any?) {
        setAppearanceMode("system")
    }

    @objc func useLightAppearance(_ sender: Any?) {
        setAppearanceMode("light")
    }

    @objc func useDarkAppearance(_ sender: Any?) {
        setAppearanceMode("dark")
    }

    private func setAppearanceMode(_ mode: String) {
        appearanceMode = mode
        switch mode {
        case "light":
            window?.appearance = NSAppearance(named: .aqua)
            NSApp.appearance = NSAppearance(named: .aqua)
        case "dark":
            window?.appearance = NSAppearance(named: .darkAqua)
            NSApp.appearance = NSAppearance(named: .darkAqua)
        default:
            window?.appearance = nil
            NSApp.appearance = nil
        }
        applyAppearanceModeToEditor()
    }

    private func applyAppearanceModeToEditor() {
        let escapedMode = appearanceMode
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "'", with: "\\'")
        let script = """
        (function(){
          var mode = '\(escapedMode)';
          document.documentElement.classList.remove('mac-force-light', 'mac-force-dark');
          document.body.classList.remove('mac-force-light', 'mac-force-dark');
          if (mode === 'light') {
            document.documentElement.classList.add('mac-force-light');
            document.body.classList.add('mac-force-light');
          } else if (mode === 'dark') {
            document.documentElement.classList.add('mac-force-dark');
            document.body.classList.add('mac-force-dark');
          }
        })();
        """
        webView.evaluateJavaScript(script, completionHandler: nil)
    }

    private func runEditorJavaScript(_ body: String) {
        let script = """
        (function(){
          try { \(body) } catch (error) { console.error(error); }
        })();
        """
        webView.evaluateJavaScript(script) { [weak self] _, _ in
            self?.refreshWindowTitle()
        }
    }

    private func copyText(_ text: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }

    private func createNewDocumentMarkdown() -> String {
        let formatter = DateFormatter()
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd"
        let created = formatter.string(from: Date())

        return [
            "---",
            "title: \"Neues Dokument\"",
            "author: \"Markdown Studio\"",
            "created: \"\(created)\"",
            "---",
            "",
            "# Neues Dokument",
            "",
            ""
        ].joined(separator: "\n")
    }

    private func refreshWindowTitle() {
        getMarkdownNoSideEffect { markdown in
            self.lastKnownMarkdown = markdown
            self.updateWindowTitle(using: markdown)
        }
    }

    private func updateWindowTitle(using markdown: String) {
        let display: String
        if let url = currentFileURL {
            display = url.lastPathComponent
        } else if let title = titleFromMarkdown(markdown), !title.isEmpty {
            display = title
        } else {
            display = "Ohne Titel"
        }
        window?.title = "\(display) — Markdown Studio Pro"
    }

    private func suggestedFilename(for markdown: String) -> String {
        let stem = titleFromMarkdown(markdown) ?? "Dokument"
        return sanitizedFilenameStem(stem) + ".md"
    }

    private func getCurrentMarkdownForClose(_ completion: @escaping (String) -> Void) {
        let js = "(window.markdownStudio && window.markdownStudio.toMarkdown ? window.markdownStudio.toMarkdown() : (window.MarkdownStudioMac && window.MarkdownStudioMac.getMarkdown ? window.MarkdownStudioMac.getMarkdown() : ''))"
        webView.evaluateJavaScript(js) { [weak self] result, _ in
            let markdown = result as? String ?? self?.lastKnownMarkdown ?? ""
            self?.lastKnownMarkdown = markdown
            completion(markdown)
        }
    }

    private func confirmClose(markdown: String) -> Bool {
        guard markdown != lastSavedMarkdown else { return true }
        let alert = NSAlert()
        alert.messageText = "Änderungen sichern?"
        alert.informativeText = "Dieses Dokument enthält ungespeicherte Änderungen. Möchtest du sie vor dem Schließen speichern?"
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Speichern")
        alert.addButton(withTitle: "Abbrechen")
        alert.addButton(withTitle: "Nicht speichern")

        let response = alert.runModal()
        switch response {
        case .alertFirstButtonReturn:
            return saveSynchronouslyBeforeClosing(markdown: markdown)
        case .alertSecondButtonReturn:
            return false
        default:
            return true
        }
    }

    private func confirmCloseIfNeeded(markdown: String) {
        if confirmClose(markdown: markdown) {
            isClosingAfterConfirmation = true
            window?.performClose(nil)
        } else {
            isClosingAfterConfirmation = false
        }
    }

    private var hasUnsavedChanges: Bool {
        window?.isDocumentEdited == true || lastKnownMarkdown != lastSavedMarkdown
    }
    private func updateDocumentEditedState(using markdown: String) {
        window?.isDocumentEdited = markdown != lastSavedMarkdown
    }


    private func saveSynchronouslyBeforeClosing(markdown: String) -> Bool {
        if let url = currentFileURL {
            return writeMarkdown(markdown, to: url)
        }

        let panel = NSSavePanel()
        panel.allowedContentTypes = [.plainText]
        panel.nameFieldStringValue = suggestedFilename(for: markdown)
        panel.message = "Markdown-Datei vor dem Schließen speichern"

        guard panel.runModal() == .OK, let url = panel.url else {
            return false
        }

        return writeMarkdown(markdown, to: url)
    }

    func confirmCloseFromApplicationQuit(completion: @escaping (Bool) -> Void) {
        getCurrentMarkdownForClose { [weak self] markdown in
            guard let self else {
                completion(true)
                return
            }
            completion(self.confirmClose(markdown: markdown))
        }
    }

    private func showAlert(title: String, message: String) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.alertStyle = .warning
        if let window = window {
            alert.beginSheetModal(for: window)
        } else {
            alert.runModal()
        }
    }
}

extension EditorWindowController: NSWindowDelegate {
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        guard hasUnsavedChanges else { return true }
        return confirmClose(markdown: lastKnownMarkdown)
    }
}

private func titleFromMarkdown(_ markdown: String) -> String? {
    let lines = markdown.components(separatedBy: .newlines)

    if let frontmatterTitle = titleFromFrontmatter(lines) {
        return frontmatterTitle
    }

    for rawLine in lines {
        let line = rawLine.trimmingCharacters(in: .whitespaces)
        if line.isEmpty { continue }
        guard line.hasPrefix("#") else { continue }

        let hashCount = line.prefix { $0 == "#" }.count
        guard hashCount >= 1, hashCount <= 6 else { continue }

        let afterHashes = line.dropFirst(hashCount)
        guard afterHashes.first == " " || afterHashes.first == "\t" else { continue }

        let rawTitle = String(afterHashes).trimmingCharacters(in: .whitespacesAndNewlines)
        let cleanedTitle = cleanMarkdownTitle(rawTitle)
        return cleanedTitle.isEmpty ? nil : cleanedTitle
    }

    return nil
}

private func titleFromFrontmatter(_ lines: [String]) -> String? {
    guard let first = lines.first?.trimmingCharacters(in: .whitespacesAndNewlines), first == "---" else {
        return nil
    }

    for rawLine in lines.dropFirst() {
        let line = rawLine.trimmingCharacters(in: .whitespacesAndNewlines)
        if line == "---" { break }
        if line.lowercased().hasPrefix("title:") {
            let rawTitle = String(line.dropFirst("title:".count)).trimmingCharacters(in: .whitespacesAndNewlines)
            let unquotedTitle = rawTitle.trimmingCharacters(in: CharacterSet(charactersIn: "\"'"))
            let cleanedTitle = cleanMarkdownTitle(unquotedTitle)
            return cleanedTitle.isEmpty ? nil : cleanedTitle
        }
    }

    return nil
}

private func cleanMarkdownTitle(_ value: String) -> String {
    var title = value
    title = title.replacingOccurrences(of: #"`([^`]*)`"#, with: "$1", options: .regularExpression)
    title = title.replacingOccurrences(of: #"\*\*([^*]*)\*\*"#, with: "$1", options: .regularExpression)
    title = title.replacingOccurrences(of: #"__([^_]*)__"#, with: "$1", options: .regularExpression)
    title = title.replacingOccurrences(of: #"\*([^*]*)\*"#, with: "$1", options: .regularExpression)
    title = title.replacingOccurrences(of: #"_([^_]*)_"#, with: "$1", options: .regularExpression)
    title = title.replacingOccurrences(of: #"~~([^~]*)~~"#, with: "$1", options: .regularExpression)
    title = title.replacingOccurrences(of: #"\[([^\]]+)\]\([^\)]+\)"#, with: "$1", options: .regularExpression)
    title = title.replacingOccurrences(of: #"<[^>]+>"#, with: "", options: .regularExpression)
    title = title.replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression)
    return title.trimmingCharacters(in: .whitespacesAndNewlines)
}

private func sanitizedFilenameStem(_ value: String) -> String {
    let invalidScalars = CharacterSet(charactersIn: "/:").union(.controlCharacters)
    let scalars = value.unicodeScalars.map { invalidScalars.contains($0) ? "-" : String($0) }.joined()
    let collapsed = scalars.replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression)
    let trimmed = collapsed.trimmingCharacters(in: CharacterSet.whitespacesAndNewlines.union(CharacterSet(charactersIn: ".")))
    return trimmed.isEmpty ? "Dokument" : trimmed
}
