import SwiftUI
import WebKit

struct MacEditorShellView: View {
    let webView: WKWebView
    let newDocument: () -> Void
    let openDocument: () -> Void
    let saveDocument: () -> Void
    let copyMarkdown: () -> Void
    let insertTable: () -> Void
    let insertCodeBlock: () -> Void
    let toggleLightAppearance: () -> Void
    let toggleDarkAppearance: () -> Void

    var body: some View {
        EditorWebViewContainer(webView: webView)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color(NSColor.windowBackgroundColor))
    }
}

struct EditorWebViewContainer: NSViewRepresentable {
    let webView: WKWebView

    func makeNSView(context: Context) -> WKWebView {
        webView
    }

    func updateNSView(_ nsView: WKWebView, context: Context) {
    }
}
