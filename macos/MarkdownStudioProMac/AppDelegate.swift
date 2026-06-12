import Cocoa

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var editorWindowController: EditorWindowController?
    private var isHandlingApplicationQuit = false
    private var pendingOpenFileURLs: [URL] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        ProcessInfo.processInfo.disableSuddenTermination()
        let controller = EditorWindowController()
        editorWindowController = controller
        buildMainMenu(target: controller)
        controller.showWindow(nil)
        NSApp.activate(ignoringOtherApps: true)
        openPendingFilesIfNeeded()
    }

    func application(_ sender: NSApplication, openFiles filenames: [String]) {
        let urls = filenames.map { URL(fileURLWithPath: $0) }
        if let controller = editorWindowController {
            urls.forEach { controller.openFile($0) }
        } else {
            pendingOpenFileURLs.append(contentsOf: urls)
        }
        sender.reply(toOpenOrPrint: .success)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard let controller = editorWindowController else {
            return .terminateNow
        }

        guard !isHandlingApplicationQuit else {
            return .terminateLater
        }

        isHandlingApplicationQuit = true
        controller.confirmCloseFromApplicationQuit { [weak self, weak sender] shouldTerminate in
            self?.isHandlingApplicationQuit = false
            sender?.reply(toApplicationShouldTerminate: shouldTerminate)
        }
        return .terminateLater
    }

    private func openPendingFilesIfNeeded() {
        guard let controller = editorWindowController, !pendingOpenFileURLs.isEmpty else { return }
        let urls = pendingOpenFileURLs
        pendingOpenFileURLs.removeAll()
        urls.forEach { controller.openFile($0) }
    }

    private func menuItem(_ title: String, action: Selector?, key: String = "", modifiers: NSEvent.ModifierFlags = [.command], target: AnyObject?) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.keyEquivalentModifierMask = modifiers
        item.target = target
        return item
    }

    private func buildMainMenu(target: EditorWindowController) {
        let mainMenu = NSMenu()

        let appItem = NSMenuItem()
        mainMenu.addItem(appItem)
        let appMenu = NSMenu(title: "Markdown Studio Pro")
        appItem.submenu = appMenu
        appMenu.addItem(menuItem("Über Markdown Studio Pro", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), modifiers: [], target: NSApp))
        appMenu.addItem(NSMenuItem.separator())
        let appearanceItem = NSMenuItem(title: "Darstellung", action: nil, keyEquivalent: "")
        let appearanceMenu = NSMenu(title: "Darstellung")
        appearanceItem.submenu = appearanceMenu
        appearanceMenu.addItem(menuItem("System", action: #selector(EditorWindowController.useSystemAppearance(_:)), modifiers: [], target: target))
        appearanceMenu.addItem(menuItem("Hell", action: #selector(EditorWindowController.useLightAppearance(_:)), modifiers: [], target: target))
        appearanceMenu.addItem(menuItem("Dunkel", action: #selector(EditorWindowController.useDarkAppearance(_:)), modifiers: [], target: target))
        appMenu.addItem(appearanceItem)
        appMenu.addItem(NSMenuItem.separator())
        appMenu.addItem(menuItem("Markdown Studio Pro beenden", action: #selector(NSApplication.terminate(_:)), key: "q", target: nil))

        let fileItem = NSMenuItem()
        mainMenu.addItem(fileItem)
        let fileMenu = NSMenu(title: "Ablage")
        fileItem.submenu = fileMenu
        fileMenu.addItem(menuItem("Neu", action: #selector(EditorWindowController.newDocument(_:)), key: "n", target: target))
        fileMenu.addItem(menuItem("Öffnen…", action: #selector(EditorWindowController.openDocument(_:)), key: "o", target: target))
        fileMenu.addItem(NSMenuItem.separator())
        fileMenu.addItem(menuItem("Speichern", action: #selector(EditorWindowController.saveDocument(_:)), key: "s", target: target))
        fileMenu.addItem(menuItem("Speichern unter…", action: #selector(EditorWindowController.saveDocumentAs(_:)), key: "S", modifiers: [.command, .shift], target: target))
        fileMenu.addItem(NSMenuItem.separator())
        fileMenu.addItem(menuItem("HTML exportieren…", action: #selector(EditorWindowController.exportHTML(_:)), key: "e", target: target))

        let editItem = NSMenuItem()
        mainMenu.addItem(editItem)
        let editMenu = NSMenu(title: "Bearbeiten")
        editItem.submenu = editMenu
        editMenu.addItem(menuItem("Rückgängig", action: Selector(("undo:")), key: "z", target: nil))
        editMenu.addItem(menuItem("Wiederholen", action: Selector(("redo:")), key: "Z", modifiers: [.command, .shift], target: nil))
        editMenu.addItem(NSMenuItem.separator())
        editMenu.addItem(menuItem("Ausschneiden", action: #selector(NSText.cut(_:)), key: "x", target: nil))
        editMenu.addItem(menuItem("Kopieren", action: #selector(NSText.copy(_:)), key: "c", target: nil))
        editMenu.addItem(menuItem("Einsetzen", action: #selector(NSText.paste(_:)), key: "v", target: nil))
        editMenu.addItem(menuItem("Alles auswählen", action: #selector(NSResponder.selectAll(_:)), key: "a", target: nil))

        let formatItem = NSMenuItem()
        mainMenu.addItem(formatItem)
        let formatMenu = NSMenu(title: "Format")
        formatItem.submenu = formatMenu
        formatMenu.addItem(menuItem("Fett", action: #selector(EditorWindowController.formatBold(_:)), key: "b", target: target))
        formatMenu.addItem(menuItem("Kursiv", action: #selector(EditorWindowController.formatItalic(_:)), key: "i", target: target))
        formatMenu.addItem(menuItem("Durchgestrichen", action: #selector(EditorWindowController.formatStrikethrough(_:)), key: "x", modifiers: [.command, .shift], target: target))
        formatMenu.addItem(NSMenuItem.separator())
        formatMenu.addItem(menuItem("Inline-Code", action: #selector(EditorWindowController.insertInlineCode(_:)), key: "`", target: target))
        formatMenu.addItem(menuItem("Codeblock", action: #selector(EditorWindowController.insertCodeBlock(_:)), key: "c", modifiers: [.command, .option], target: target))
        formatMenu.addItem(NSMenuItem.separator())
        formatMenu.addItem(menuItem("Überschrift 1", action: #selector(EditorWindowController.formatHeading1(_:)), key: "1", target: target))
        formatMenu.addItem(menuItem("Überschrift 2", action: #selector(EditorWindowController.formatHeading2(_:)), key: "2", target: target))
        formatMenu.addItem(menuItem("Zitat", action: #selector(EditorWindowController.formatQuote(_:)), key: ">", target: target))

        let insertItem = NSMenuItem()
        mainMenu.addItem(insertItem)
        let insertMenu = NSMenu(title: "Einfügen")
        insertItem.submenu = insertMenu
        insertMenu.addItem(menuItem("Link", action: #selector(EditorWindowController.insertLink(_:)), key: "k", target: target))
        insertMenu.addItem(menuItem("Tabelle…", action: #selector(EditorWindowController.insertTable(_:)), key: "t", modifiers: [.command, .option], target: target))
        insertMenu.addItem(menuItem("Trennlinie", action: #selector(EditorWindowController.insertHorizontalRule(_:)), key: "-", target: target))
        insertMenu.addItem(menuItem("Aufgabenliste", action: #selector(EditorWindowController.insertTaskList(_:)), key: "l", modifiers: [.command, .option], target: target))

        let viewItem = NSMenuItem()
        mainMenu.addItem(viewItem)
        let viewMenu = NSMenu(title: "Ansicht")
        viewItem.submenu = viewMenu
        viewMenu.addItem(menuItem("Markdown kopieren", action: #selector(EditorWindowController.copyMarkdown(_:)), target: target))
        viewMenu.addItem(menuItem("Markdown-Quelle anzeigen", action: #selector(EditorWindowController.showSource(_:)), target: target))

        NSApp.mainMenu = mainMenu
    }
}
