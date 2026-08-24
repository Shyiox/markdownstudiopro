using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT.Interop;

namespace MarkdownStudioPro.WinUI;

public sealed partial class MainWindow : Window
{
    private const string AppName = "Markdown Studio Pro";
    private const int MaxRecentFiles = 8;
    private const int MinVisibleWindowPixels = 64;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string editorPath;
    private readonly string? initialFilePath;
    private readonly JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly List<string> recentFiles = new();

    private AppWindow? appWindow;
    private string currentMarkdown = CreateNewDocumentMarkdown();
    private string lastSavedMarkdown = CreateNewDocumentMarkdown();
    private string? currentFilePath;
    private bool editorReady;
    private bool initialDocumentLoaded;
    private bool startupCompleting;
    private bool isDirty;
    private bool isSaving;
    private bool isSupportedFileDrag;
    private bool closeAllowed;
    private ElementTheme currentTheme = ElementTheme.Dark;
    private AppSettings currentSettings = new();
    private readonly DispatcherTimer autoSaveTimer = new();
    private int currentWordCount;
    private int currentCharacterCount;

    public MainWindow(string? initialFilePath = null)
    {
        InitializeComponent();

        var hasExplicitInitialPath = !string.IsNullOrWhiteSpace(initialFilePath);
        var explicitInitialFilePath = NormalizeInitialPath(initialFilePath);
        editorPath = Path.Combine(AppContext.BaseDirectory, "App", "editor.html");

        LoadSettings();
        this.initialFilePath = hasExplicitInitialPath ? explicitInitialFilePath : ResolveLastDocumentPathForStartup();
        LoadRecentFiles();
        ConfigureAutoSaveTimer();
        ConfigureWindow();

        Root.RequestedTheme = ThemeToElementTheme(currentSettings.Theme);
        currentTheme = Root.RequestedTheme;
        ApplyShellTheme();
        UpdateTitle();
        UpdateStatus();

        Root.Loaded += Root_Loaded;
    }

    private static string CreateNewDocumentMarkdown() => string.Join("\n", new[] { "# Neues Dokument", "", string.Empty });

    private void ConfigureAutoSaveTimer()
    {
        autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(currentSettings.AutoSaveIntervalSeconds, 10, 600));
        autoSaveTimer.Tick += AutoSaveTimer_Tick;

        if (currentSettings.AutoSaveEnabled)
        {
            autoSaveTimer.Start();
        }
    }

    private void ConfigureWindow()
    {
        Title = initialFilePath is null
            ? $"Unbenannt - {AppName}"
            : $"{Path.GetFileName(initialFilePath)} - {AppName}";

        try
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            appWindow = AppWindow.GetFromWindowId(windowId);
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                ExtendsContentIntoTitleBar = true;
                appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                SetTitleBar(AppTitleBarDragRegion);
            }
            else
            {
                TopBar.Visibility = Visibility.Collapsed;
                Root.RowDefinitions[0].Height = new GridLength(0);
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "markdown_studio_icon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var windowWidth = Math.Clamp(currentSettings.WindowWidth, 820, Math.Max(820, workArea.Width - 120));
            var windowHeight = Math.Clamp(currentSettings.WindowHeight, 720, Math.Max(720, workArea.Height - 90));
            appWindow.Resize(new SizeInt32 { Width = windowWidth, Height = windowHeight });

            if (TryGetSavedWindowPosition(windowWidth, windowHeight, out var savedPosition))
            {
                appWindow.Move(savedPosition);
            }
            else
            {
                appWindow.Move(new PointInt32
                {
                    X = workArea.X + Math.Max(0, (workArea.Width - windowWidth) / 2),
                    Y = workArea.Y + Math.Max(0, (workArea.Height - windowHeight) / 2)
                });
            }

            ApplyWindowChrome();
            appWindow.Closing += OnAppWindowClosing;
        }
        catch
        {
            // Window sizing and close interception are conveniences. The editor can run without them.
        }
    }

    private bool TryGetSavedWindowPosition(int windowWidth, int windowHeight, out PointInt32 position)
    {
        position = default;

        if (currentSettings.WindowX is not int savedX || currentSettings.WindowY is not int savedY)
        {
            return false;
        }

        foreach (var displayArea in DisplayArea.FindAll())
        {
            var bounds = displayArea.OuterBounds;
            var visibleWidth = Math.Min((long)savedX + windowWidth, (long)bounds.X + bounds.Width)
                - Math.Max((long)savedX, bounds.X);
            var visibleHeight = Math.Min((long)savedY + windowHeight, (long)bounds.Y + bounds.Height)
                - Math.Max((long)savedY, bounds.Y);

            if (visibleWidth >= Math.Min(MinVisibleWindowPixels, windowWidth)
                && visibleHeight >= Math.Min(MinVisibleWindowPixels, windowHeight))
            {
                position = new PointInt32 { X = savedX, Y = savedY };
                return true;
            }
        }

        return false;
    }

    private void ApplyWindowChrome()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported() || appWindow?.TitleBar is not { } titleBar)
        {
            return;
        }

        var light = IsLightShellTheme(currentSettings.Theme);
        var transparent = Color.FromArgb(0, 0, 0, 0);
        var foreground = light
            ? Color.FromArgb(255, 36, 33, 31)
            : Color.FromArgb(255, 242, 240, 236);
        var inactiveForeground = light
            ? Color.FromArgb(255, 125, 117, 109)
            : Color.FromArgb(255, 165, 161, 155);
        var hoverBackground = light
            ? Color.FromArgb(255, 239, 233, 225)
            : Color.FromArgb(255, 40, 42, 46);
        var pressedBackground = light
            ? Color.FromArgb(255, 232, 222, 213)
            : Color.FromArgb(255, 48, 50, 56);

        titleBar.BackgroundColor = transparent;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = transparent;
        titleBar.InactiveForegroundColor = inactiveForeground;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;

        AppTitleBarDragRegion.Padding = new Thickness(
            Math.Max(16, titleBar.LeftInset + 16),
            0,
            Math.Max(150, titleBar.RightInset + 12),
            0);
    }

    private void ApplyShellTheme()
    {
        var shellTheme = ThemeToElementTheme(currentSettings.Theme);
        Root.RequestedTheme = shellTheme;
        TopBar.RequestedTheme = shellTheme;
        EditorialToolbar.RequestedTheme = shellTheme;
        currentTheme = shellTheme;
        ApplyShellSurfacePalette();

        try
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        catch
        {
            // Mica is a polish layer. The app should keep running without it.
        }

        ApplyWindowChrome();
    }

    private void ApplyShellSurfacePalette()
    {
        Color titleBackground;
        Color toolbarBackground;
        Color border;

        if (string.Equals(currentSettings.Theme, "dark", StringComparison.OrdinalIgnoreCase))
        {
            // Dark uses the neutral graphite palette.
            titleBackground = Color.FromArgb(255, 24, 25, 27);   // #18191B
            toolbarBackground = Color.FromArgb(255, 25, 26, 29); // #191A1D
            border = Color.FromArgb(255, 52, 54, 58);             // #34363A
        }
        else
        {
            titleBackground = Color.FromArgb(255, 247, 244, 239);
            toolbarBackground = Color.FromArgb(255, 251, 248, 243);
            border = Color.FromArgb(255, 227, 220, 211);
        }

        TopBar.Background = new SolidColorBrush(titleBackground);
        EditorialToolbar.Background = new SolidColorBrush(toolbarBackground);
        TopBar.BorderBrush = new SolidColorBrush(border);
        EditorialToolbar.BorderBrush = new SolidColorBrush(border);
    }


    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= Root_Loaded;
        await Task.Delay(150);
        await InitializeEditorWithCrashReportAsync();
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (appWindow is not null)
        {
            currentSettings.WindowWidth = appWindow.Size.Width;
            currentSettings.WindowHeight = appWindow.Size.Height;
            var windowPosition = appWindow.Position;
            currentSettings.WindowX = windowPosition.X;
            currentSettings.WindowY = windowPosition.Y;
        }

        currentSettings.LastDocumentPath = NormalizeInitialPath(currentFilePath);
        SaveSettings();

        if (closeAllowed)
        {
            return;
        }

        args.Cancel = true;

        await RefreshDirtyStateFromEditorAsync();

        if (!isDirty)
        {
            closeAllowed = true;
            Close();
            return;
        }

        if (await ConfirmDiscardIfNeededAsync())
        {
            closeAllowed = true;
            Close();
        }
    }

    private async void AutoSaveTimer_Tick(object? sender, object e)
    {
        if (!currentSettings.AutoSaveEnabled || !isDirty || string.IsNullOrWhiteSpace(currentFilePath))
        {
            return;
        }

        try
        {
            await SaveDocumentAsync(await GetMarkdownFromEditorAsync(), forceSaveAs: false, silent: true);
        }
        catch (Exception ex)
        {
            await NotifyAsync("Auto-Save fehlgeschlagen", ex.Message);
        }
    }

    private async Task InitializeEditorWithCrashReportAsync()
    {
        try
        {
            await InitializeEditorAsync();
        }
        catch (Exception ex)
        {
            WriteStartupLog(ex);
            EditorLoadingText.Text = "WebView2 konnte nicht gestartet werden.";
            await ShowMessageAsync("Startfehler", BuildStartupErrorMessage(ex));
        }
    }

    private async Task InitializeEditorAsync()
    {
        if (!File.Exists(editorPath))
        {
            throw new FileNotFoundException("Die Editor-Datei wurde nicht gefunden.", editorPath);
        }

        EditorLoadingText.Text = "WebView2 wird vorbereitet ...";
        await EditorWebView.EnsureCoreWebView2Async();

        if (EditorWebView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 wurde initialisiert, aber CoreWebView2 ist weiterhin null.");
        }

        EditorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
#if DEBUG
        EditorWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
        EditorWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
        EditorWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        EditorWebView.CoreWebView2.NavigationCompleted += OnEditorNavigationCompleted;
        EditorWebView.CoreWebView2.ProcessFailed += OnEditorProcessFailed;

        EditorLoadingText.Text = "Editor-Datei wird geladen ...";
        EditorWebView.CoreWebView2.Navigate(new Uri(editorPath).AbsoluteUri);
    }

    private async void OnEditorNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            var message = $"WebView2 konnte den Editor nicht laden. Status: {args.WebErrorStatus}. Pfad: {editorPath}";
            EditorLoadingText.Text = message;
            WriteStartupLog(new InvalidOperationException(message));
            await ShowMessageAsync("Editor konnte nicht geladen werden", message);
            return;
        }

        _ = WaitForEditorBridgeAndCompleteStartupAsync("navigation-completed");
    }

    private async void OnEditorProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        var message = $"WebView2-Prozessfehler: {args.ProcessFailedKind}";
        EditorLoadingText.Text = message;
        WriteStartupLog(new InvalidOperationException(message));
        await ShowMessageAsync("WebView2-Fehler", message);
    }

    private async Task WaitForEditorBridgeAndCompleteStartupAsync(string source)
    {
        if (EditorWebView.CoreWebView2 is null || initialDocumentLoaded || startupCompleting)
        {
            return;
        }

        AppendStartupTrace($"Waiting for editor bridge after {source}.");

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await ExecuteScriptWithTimeoutAsync(
                    "Boolean(window.markdownStudio && typeof window.markdownStudio.setMarkdownBase64 === 'function')",
                    TimeSpan.FromSeconds(2),
                    "bridge probe");

                if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                {
                    await CompleteEditorStartupAsync(source + "/bridge-ready");
                    return;
                }
            }
            catch (Exception ex)
            {
                AppendStartupTrace($"Bridge probe failed: {ex.GetType().Name}: {ex.Message}");
            }

            await Task.Delay(200);
        }

        var message = "Der Editor wurde geladen, aber die JavaScript-Bridge wurde nicht rechtzeitig bereit. Der Start wurde abgebrochen statt endlos zu laden.";
        EditorLoadingText.Text = message;
        AppendStartupTrace(message);
        await ShowMessageAsync("Editor-Bridge nicht bereit", message + "\n\n" + BuildStartupStateMessage());
    }

    private async Task CompleteEditorStartupAsync(string source)
    {
        if (initialDocumentLoaded || startupCompleting)
        {
            return;
        }

        startupCompleting = true;
        AppendStartupTrace($"Completing editor startup from {source}.");

        try
        {
            editorReady = true;
            EditorLoadingText.Text = "Dokument wird vorbereitet ...";
            await SendSettingsAsync();
            await LoadInitialDocumentAsync();
            if (currentFilePath is null)
            {
                await FocusInitialHeadingAsync();
            }
            initialDocumentLoaded = true;
            EditorLoadingOverlay.Visibility = Visibility.Collapsed;
            AppendStartupTrace("Editor startup completed.");
        }
        catch (Exception ex)
        {
            editorReady = false;
            var message = "Dokumentvorbereitung fehlgeschlagen: " + ex.Message;
            EditorLoadingText.Text = message;
            AppendStartupTrace(message);
            WriteStartupLog(ex);
            await ShowMessageAsync("Dokument konnte nicht vorbereitet werden", message + "\n\n" + BuildStartupStateMessage());
        }
        finally
        {
            startupCompleting = false;
        }
    }

    private async void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            var type = ReadString(root, "type");

            switch (type)
            {
                case "ready":
                    await CompleteEditorStartupAsync("ready-message");
                    break;
                case "changed":
                    HandleChanged(ReadString(root, "markdown") ?? currentMarkdown, root);
                    break;
                case "new":
                    await NewDocumentAsync();
                    break;
                case "open":
                    await OpenDocumentAsync();
                    break;
                case "save":
                    await SaveDocumentAsync(ReadString(root, "markdown") ?? await GetMarkdownFromEditorAsync(), forceSaveAs: false);
                    break;
                case "saveAs":
                    await SaveDocumentAsync(ReadString(root, "markdown") ?? await GetMarkdownFromEditorAsync(), forceSaveAs: true);
                    break;
                case "copyMarkdown":
                    CopyMarkdown(ReadString(root, "markdown") ?? await GetMarkdownFromEditorAsync());
                    break;
                case "exportMarkdownFile":
                    await ExportMarkdownFileAsync(ReadString(root, "markdown") ?? await GetMarkdownFromEditorAsync());
                    break;
                case "exportHtml":
                    await ExportHtmlAsync(ReadString(root, "html") ?? string.Empty);
                    break;
                case "printPdf":
                    await PrintPdfAsync();
                    break;
                case "showSource":
                    await ShowSourceAsync(ReadString(root, "markdown") ?? await GetMarkdownFromEditorAsync());
                    break;
                case "setSourceMarkdown":
                    await SetMarkdownAsync(ReadString(root, "markdown") ?? string.Empty, markClean: false, focusWritingArea: true);
                    HandleChanged(ReadString(root, "markdown") ?? currentMarkdown, root);
                    break;
                case "getSettings":
                    await SendSettingsAsync();
                    break;
                case "getRecentFiles":
                    await SendRecentFilesAsync();
                    break;
                case "openRecent":
                    await OpenRecentAsync();
                    break;
                case "openRecentFile":
                    await OpenRecentFileAsync(ReadString(root, "path"));
                    break;
                case "updateSetting":
                    UpdateSettingFromWeb(root);
                    break;
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Bridge-Fehler", ex.Message);
        }
    }

    private async Task LoadInitialDocumentAsync()
    {
        if (!editorReady)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentFilePath))
        {
            await SetMarkdownAsync(currentMarkdown, markClean: true, focusWritingArea: true);
            return;
        }

        if (!string.IsNullOrEmpty(initialFilePath) && File.Exists(initialFilePath))
        {
            await LoadMarkdownFileAsync(initialFilePath, notify: false);
            return;
        }

        currentFilePath = null;
        await SetMarkdownAsync(CreateNewDocumentMarkdown(), markClean: true, focusHeading: true, showStartPlaceholder: true);
    }

    private async Task NewDocumentAsync()
    {
        if (!await ConfirmDiscardIfNeededAsync())
        {
            return;
        }

        currentFilePath = null;
        await SetMarkdownAsync(CreateNewDocumentMarkdown(), markClean: true, focusHeading: true, showStartPlaceholder: true);
        await NotifyAsync("Neues Dokument", "Bereit zum Schreiben.");
    }

    private async Task OpenDocumentAsync()
    {
        if (!await ConfirmDiscardIfNeededAsync())
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add(".markdown");
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await LoadMarkdownFileAsync(file, notify: true);
        }
        catch (Exception ex)
        {
            await NotifyAsync("Datei konnte nicht geöffnet werden", file.Name + ": " + ex.Message);
        }
    }

    private static bool IsSupportedDroppedDocument(StorageFile file)
    {
        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        return extension is ".md" or ".markdown" or ".txt";
    }

    private static async Task<bool> IsSupportedFileDragAsync(DataPackageView dataView)
    {
        if (!dataView.Contains(StandardDataFormats.StorageItems))
        {
            return false;
        }

        try
        {
            var items = await dataView.GetStorageItemsAsync();
            return items.Count == 1
                && items[0] is StorageFile file
                && IsSupportedDroppedDocument(file);
        }
        catch
        {
            return false;
        }
    }

    private void SetFileDropOverlayVisible(bool visible)
    {
        FileDropOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void EditorSurface_DragEnter(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var deferral = e.GetDeferral();

        try
        {
            isSupportedFileDrag = await IsSupportedFileDragAsync(e.DataView);
            e.AcceptedOperation = isSupportedFileDrag
                ? DataPackageOperation.Copy
                : DataPackageOperation.None;
            SetFileDropOverlayVisible(isSupportedFileDrag);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void EditorSurface_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = isSupportedFileDrag
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private void EditorSurface_DragLeave(object sender, DragEventArgs e)
    {
        isSupportedFileDrag = false;
        SetFileDropOverlayVisible(false);
        e.Handled = true;
    }

    private async void EditorSurface_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        isSupportedFileDrag = false;
        SetFileDropOverlayVisible(false);

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count != 1 || items[0] is not StorageFile file)
            {
                await NotifyAsync("Datei nicht geöffnet", "Bitte genau eine Markdown- oder Textdatei ablegen.");
                return;
            }

            if (!IsSupportedDroppedDocument(file))
            {
                await NotifyAsync("Datei nicht unterstützt", "Unterstützt werden .md, .markdown und .txt.");
                return;
            }

            if (!await ConfirmDiscardIfNeededAsync())
            {
                return;
            }

            await LoadMarkdownFileAsync(file, notify: true);
        }
        catch (Exception ex)
        {
            await NotifyAsync("Datei konnte nicht geöffnet werden", ex.Message);
        }
    }

    private async Task LoadMarkdownFileAsync(StorageFile file, bool notify)
    {
        var path = !string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path)
            ? file.Path
            : null;
        string markdown;

        if (path is not null)
        {
            markdown = await ReadMarkdownFileAsync(path);
        }
        else
        {
            markdown = DecodeMarkdownBytes(await ReadStorageFileBytesAsync(file));
        }

        await LoadMarkdownContentAsync(markdown, path, notify, file.Name);
    }

    private async Task LoadMarkdownFileAsync(string path, bool notify)
    {
        var markdown = await ReadMarkdownFileAsync(path);
        await LoadMarkdownContentAsync(markdown, path, notify);
    }

    private async Task LoadMarkdownContentAsync(string markdown, string? path, bool notify, string? displayName = null)
    {
        currentFilePath = path;
        lastSavedMarkdown = markdown;
        isDirty = false;
        await SetMarkdownAsync(markdown, markClean: true, focusWritingArea: true);

        if (path is not null)
        {
            AddRecentFile(path);
        }

        if (notify)
        {
            await NotifyAsync("Datei geöffnet", path is null ? displayName ?? "Markdown-Datei" : Path.GetFileName(path));
        }
    }

    private async Task SaveDocumentAsync(string markdown, bool forceSaveAs, bool silent = false)
    {
        var targetPath = currentFilePath;

        if (forceSaveAs || string.IsNullOrWhiteSpace(targetPath))
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(BuildSuggestedMarkdownFileName(markdown, targetPath))
            };
            picker.FileTypeChoices.Add("Markdown", new[] { ".md" });
            picker.FileTypeChoices.Add("Text", new[] { ".txt" });
            picker.DefaultFileExtension = ".md";
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            targetPath = file.Path;
        }

        var markdownToSave = NormalizeLineEndings(markdown);
        currentMarkdown = markdownToSave;
        UpdateDocumentStats(currentMarkdown);
        isDirty = !string.Equals(
            NormalizeLineEndings(currentMarkdown),
            NormalizeLineEndings(lastSavedMarkdown),
            StringComparison.Ordinal);
        isSaving = true;
        UpdateTitle();
        UpdateStatus();

        try
        {
            await File.WriteAllTextAsync(targetPath, markdownToSave, Utf8NoBom);
            currentFilePath = targetPath;
            lastSavedMarkdown = markdownToSave;
            isDirty = !string.Equals(
                NormalizeLineEndings(currentMarkdown),
                markdownToSave,
                StringComparison.Ordinal);
            AddRecentFile(targetPath);
        }
        finally
        {
            isSaving = false;
            UpdateTitle();
            UpdateStatus();
        }

        if (!silent)
        {
            await NotifyAsync("Gespeichert", Path.GetFileName(targetPath));
        }
    }

    private async Task SetMarkdownAsync(string markdown, bool markClean, bool focusWritingArea = false, bool focusHeading = false, bool showStartPlaceholder = false)
    {
        markdown ??= string.Empty;
        currentMarkdown = markdown;
        UpdateDocumentStats(markdown);

        if (markClean)
        {
            lastSavedMarkdown = markdown;
            isDirty = false;
        }

        UpdateTitle();
        UpdateStatus();

        if (!editorReady || EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown));
        var encodedBase64 = JsonSerializer.Serialize(base64);
        var options = JsonSerializer.Serialize(new { focusWritingArea, focusHeading, showStartPlaceholder }, jsonOptions);
        var script =
            "(function(){" +
            "try{" +
            "if(window.markdownStudio && typeof window.markdownStudio.setMarkdownBase64 === 'function'){" +
            "return window.markdownStudio.setMarkdownBase64(" + encodedBase64 + "," + options + ") === true;" +
            "}" +
            "return false;" +
            "}catch(e){console.error(e);return false;}" +
            "})();";

        var result = await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "set markdown");
        if (!string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
        {
            var payload = JsonSerializer.Serialize(new { type = "setMarkdownBase64", base64, focusWritingArea, focusHeading, showStartPlaceholder }, jsonOptions);
            EditorWebView.CoreWebView2.PostWebMessageAsJson(payload);
            await Task.Delay(100);
            var verify = await GetMarkdownFromEditorAsync();
            if (!string.Equals(NormalizeLineEndings(verify), NormalizeLineEndings(markdown), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Die Datei wurde gelesen, aber der Editor hat den Inhalt nicht übernommen.");
            }
        }

        await SyncDocumentStatsFromEditorAsync();
    }

    private async Task SyncDocumentStatsFromEditorAsync()
    {
        if (EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var result = await ExecuteScriptWithTimeoutAsync(
                "window.markdownStudio && typeof window.markdownStudio.documentStats === 'function' ? window.markdownStudio.documentStats() : null",
                TimeSpan.FromSeconds(4),
                "get document stats");

            if (!string.IsNullOrWhiteSpace(result) && !string.Equals(result, "null", StringComparison.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(result);
                ApplyDocumentStats(document.RootElement);
                UpdateStatus();
            }
        }
        catch
        {
            // Stats are presentation-only. Loading the document must not fail because of them.
        }
    }

    private async Task<string> GetMarkdownFromEditorAsync()
    {
        if (EditorWebView.CoreWebView2 is null)
        {
            return currentMarkdown;
        }

        var script = "window.markdownStudio && typeof window.markdownStudio.toMarkdown === 'function' ? window.markdownStudio.toMarkdown() : ''";
        var result = await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "get markdown");
        var markdown = JsonSerializer.Deserialize<string>(result);
        if (markdown is not null)
        {
            currentMarkdown = markdown;
        }

        return currentMarkdown;
    }

    private async Task RunEditorCommandAsync(string command)
    {
        if (EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var script = "if (typeof runCommand === 'function') runCommand(" + JsonSerializer.Serialize(command) + ");";
        await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "editor command");
    }

    private async Task FocusInitialHeadingAsync()
    {
        if (EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        EditorWebView.Focus(FocusState.Programmatic);
        var script =
            "requestAnimationFrame(() => requestAnimationFrame(() => {" +
            "window.focus();" +
            "if(window.markdownStudio && typeof window.markdownStudio.focusInitialTitle === 'function') window.markdownStudio.focusInitialTitle();" +
            "}));";
        await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "focus initial heading");
    }

    private async Task RunEditorFormatAsync(string format)
    {
        if (EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var script = "if (typeof runFormat === 'function') runFormat(" + JsonSerializer.Serialize(format) + ");";
        await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "editor format");
    }

    private void HandleChanged(string markdown, JsonElement? message = null)
    {
        currentMarkdown = markdown;
        if (message.HasValue && message.Value.TryGetProperty("stats", out var stats))
        {
            ApplyDocumentStats(stats);
        }
        else
        {
            UpdateDocumentStats(markdown);
        }

        isDirty = currentMarkdown != lastSavedMarkdown;
        UpdateTitle();
        UpdateStatus();
    }

    private async Task RefreshDirtyStateFromEditorAsync()
    {
        if (EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        currentMarkdown = await GetMarkdownFromEditorAsync();
        isDirty = !string.Equals(
            NormalizeLineEndings(currentMarkdown),
            NormalizeLineEndings(lastSavedMarkdown),
            StringComparison.Ordinal);
        UpdateTitle();
        UpdateStatus();
    }

    private void ApplyDocumentStats(JsonElement stats)
    {
        currentWordCount = ReadInt(stats, "words") ?? currentWordCount;
        currentCharacterCount = ReadInt(stats, "chars") ?? currentCharacterCount;
    }

    private void CopyMarkdown(string markdown)
    {
        var package = new DataPackage();
        package.SetText(NormalizeLineEndings(markdown));
        Clipboard.SetContent(package);
        _ = NotifyAsync("Markdown kopiert", "Der aktuelle Inhalt liegt in der Zwischenablage.");
    }

    private async Task ExportMarkdownFileAsync(string markdown)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(BuildSuggestedMarkdownFileName(markdown, currentFilePath))
        };
        picker.FileTypeChoices.Add("Markdown", new[] { ".md" });
        picker.FileTypeChoices.Add("Text", new[] { ".txt" });
        picker.DefaultFileExtension = ".md";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await File.WriteAllTextAsync(file.Path, NormalizeLineEndings(markdown), Utf8NoBom);
        await NotifyAsync("Markdown exportiert", Path.GetFileName(file.Path));
    }

    private async Task ExportHtmlAsync(string html)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = "dokument"
        };
        picker.FileTypeChoices.Add("HTML", new[] { ".html" });
        picker.DefaultFileExtension = ".html";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await File.WriteAllTextAsync(file.Path, html, Utf8NoBom);
        await NotifyAsync("HTML exportiert", Path.GetFileName(file.Path));
    }

    private async Task PrintPdfAsync()
    {
        await RunEditorCommandAsync("printPdf");
    }

    private async Task ShowSourceAsync(string markdown)
    {
        var textBox = new TextBox
        {
            Text = markdown,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            MinHeight = 420,
            MinWidth = 720
        };

        var dialog = CreateDialog("Markdown-Quelle bearbeiten", textBox);
        dialog.PrimaryButtonText = "Anwenden";
        dialog.CloseButtonText = "Schließen";
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await SetMarkdownAsync(textBox.Text, markClean: false, focusWritingArea: true);
            HandleChanged(textBox.Text);
            await NotifyAsync("Quelle übernommen", "Das Dokument wurde aus der Markdown-Quelle aktualisiert.");
        }
    }

    private async Task OpenRecentAsync()
    {
        if (recentFiles.Count == 0)
        {
            await NotifyAsync("Keine zuletzt verwendeten Dateien", "Öffne zuerst ein Dokument.");
            return;
        }

        var list = new ListView
        {
            ItemsSource = recentFiles
                .Select(path => new RecentFileItem(path, Path.GetFileName(path)))
                .ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            MinWidth = 680,
            MaxHeight = 380,
            DisplayMemberPath = nameof(RecentFileItem.DisplayText)
        };
        list.SelectedIndex = 0;

        var dialog = CreateDialog("Zuletzt verwendet", list);
        dialog.SecondaryButtonText = "Entfernen";
        dialog.PrimaryButtonText = "Öffnen";
        dialog.CloseButtonText = "Abbrechen";

        var result = await dialog.ShowAsync();
        if (list.SelectedItem is not RecentFileItem selected)
        {
            return;
        }

        if (result == ContentDialogResult.Secondary)
        {
            recentFiles.Remove(selected.Path);
            SaveRecentFiles();
            await NotifyAsync("Eintrag entfernt", selected.DisplayText);
            return;
        }

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (!File.Exists(selected.Path))
        {
            recentFiles.Remove(selected.Path);
            SaveRecentFiles();
            await NotifyAsync("Datei nicht gefunden", selected.Path);
            return;
        }

        if (await ConfirmDiscardIfNeededAsync())
        {
            await LoadMarkdownFileAsync(selected.Path, notify: true);
        }
    }

    private async Task OpenRecentFileAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            recentFiles.Remove(path);
            SaveRecentFiles();
            await NotifyAsync("Datei nicht gefunden", path);
            return;
        }

        if (await ConfirmDiscardIfNeededAsync())
        {
            await LoadMarkdownFileAsync(path, notify: true);
        }
    }

    private async Task<bool> ConfirmDiscardIfNeededAsync()
    {
        if (!isDirty)
        {
            return true;
        }

        currentMarkdown = await GetMarkdownFromEditorAsync();

        var dialog = CreateDialog("Änderungen sichern?", new TextBlock
        {
            Text = "Dieses Dokument enthält ungespeicherte Änderungen.",
            TextWrapping = TextWrapping.Wrap
        });
        dialog.PrimaryButtonText = "Speichern";
        dialog.SecondaryButtonText = "Nicht speichern";
        dialog.CloseButtonText = "Abbrechen";

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await SaveDocumentAsync(currentMarkdown, forceSaveAs: false);
            return !isDirty;
        }

        return result == ContentDialogResult.Secondary;
    }

    private async Task SendSettingsAsync()
    {
        if (!editorReady || EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(currentSettings, jsonOptions);
        var script = "window.markdownStudio && window.markdownStudio.applySettings && window.markdownStudio.applySettings(" + payload + ");";
        await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "send settings");
    }

    private async Task SendSettingsPreviewAsync(string theme, int editorWidth, int fontSize, double lineHeight)
    {
        if (!editorReady || EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var preview = new
        {
            theme = NormalizeTheme(theme),
            editorWidth = Math.Clamp(editorWidth, 680, 940),
            fontSize = Math.Clamp(fontSize, 13, 22),
            lineHeight = Math.Clamp(lineHeight, 1.35, 2.0)
        };
        var payload = JsonSerializer.Serialize(preview, jsonOptions);
        var script = "window.markdownStudio && window.markdownStudio.applySettings && window.markdownStudio.applySettings(" + payload + ");";
        await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "preview settings");
    }

    private async Task SendRecentFilesAsync()
    {
        if (!editorReady || EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var files = recentFiles
            .Where(File.Exists)
            .ToList();
        var payload = JsonSerializer.Serialize(files, jsonOptions);
        var script = "if (typeof receiveRecentFiles === 'function') receiveRecentFiles(" + payload + ");";
        await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "send recent files");
    }

    private void UpdateSettingFromWeb(JsonElement root)
    {
        var key = ReadString(root, "key");
        if (string.IsNullOrWhiteSpace(key) || !root.TryGetProperty("value", out var value))
        {
            return;
        }

        switch (key)
        {
            case "theme":
                currentSettings.Theme = NormalizeTheme(value.GetString() ?? currentSettings.Theme);
                Root.RequestedTheme = ThemeToElementTheme(currentSettings.Theme);
                currentTheme = Root.RequestedTheme;
                ApplyShellTheme();
                break;
            case "focusMode":
                currentSettings.FocusMode = value.ValueKind == JsonValueKind.True;
                break;
            case "autoSaveEnabled":
                currentSettings.AutoSaveEnabled = value.ValueKind == JsonValueKind.True;
                RestartAutoSaveTimer();
                break;
            case "toolsOpen":
                currentSettings.ToolsOpen = value.ValueKind == JsonValueKind.True;
                break;
            case "wordGoal":
                currentSettings.WordGoal = value.ValueKind == JsonValueKind.Number ? value.GetInt32() : currentSettings.WordGoal;
                break;
            case "targetWordCount":
                currentSettings.WordGoal = value.ValueKind == JsonValueKind.Number ? value.GetInt32() : currentSettings.WordGoal;
                break;
        }

        SaveSettings();
        UpdateStatus();
    }

    private async Task ShowSettingsAsync()
    {
        var originalTheme = NormalizeTheme(currentSettings.Theme);
        var originalEditorWidth = currentSettings.EditorWidth;
        var originalFontSize = currentSettings.FontSize;
        var originalLineHeight = currentSettings.LineHeight;
        var lightDialog = IsLightShellTheme(currentSettings.Theme);
        var textBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 30, 29, 27)
            : Color.FromArgb(255, 244, 241, 236));
        var mutedBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 101, 96, 88)
            : Color.FromArgb(255, 184, 178, 168));
        var cardBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 250, 247, 242)
            : Color.FromArgb(255, 32, 33, 36));
        var controlBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 255, 253, 249)
            : Color.FromArgb(255, 23, 24, 26));
        var borderBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 221, 213, 202)
            : Color.FromArgb(255, 52, 54, 58));
        var accentBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 204, 120, 92)
            : Color.FromArgb(255, 216, 138, 108));
        var accentHoverBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 187, 103, 78)
            : Color.FromArgb(255, 228, 154, 126));
        var accentPressedBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 169, 88, 67)
            : Color.FromArgb(255, 198, 116, 88));
        var onAccentTextBrush = new SolidColorBrush(Color.FromArgb(255, 23, 21, 18));

        var themeBox = new ComboBox
        {
            ItemsSource = new[] { "Hell", "Dunkel" },
            SelectedIndex = lightDialog ? 0 : 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 180,
            Background = controlBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(8)
        };

        var focusSwitch = CreateSettingsToggle("Fokusmodus beim Start", currentSettings.FocusMode);
        var spellcheckSwitch = CreateSettingsToggle("Rechtschreibprüfung", currentSettings.Spellcheck);
        var autoSaveSwitch = CreateSettingsToggle("Auto-Save für gespeicherte Dateien", currentSettings.AutoSaveEnabled);
        var reopenLastDocumentSwitch = CreateSettingsToggle("Letztes Dokument beim Start öffnen", currentSettings.ReopenLastDocument);

        var autoSaveBox = new NumberBox
        {
            Value = currentSettings.AutoSaveIntervalSeconds,
            Minimum = 10,
            Maximum = 600,
            SmallChange = 5,
            LargeChange = 30,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Background = controlBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(8)
        };
        var widthBox = new NumberBox
        {
            Value = currentSettings.EditorWidth,
            Minimum = 680,
            Maximum = 940,
            SmallChange = 20,
            LargeChange = 80,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Background = controlBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(8)
        };
        var fontBox = new NumberBox
        {
            Value = currentSettings.FontSize,
            Minimum = 13,
            Maximum = 22,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Background = controlBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(8)
        };
        var lineHeightBox = new NumberBox
        {
            Value = currentSettings.LineHeight,
            Minimum = 1.35,
            Maximum = 2.0,
            SmallChange = 0.05,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Background = controlBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(8)
        };
        var goalBox = new NumberBox
        {
            Value = currentSettings.WordGoal,
            Minimum = 0,
            Maximum = 100000,
            SmallChange = 100,
            LargeChange = 1000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Background = controlBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(8)
        };

        var appearanceContent = new StackPanel { Spacing = 7 };
        appearanceContent.Children.Add(themeBox);

        var behaviorContent = new StackPanel { Spacing = 10 };
        behaviorContent.Children.Add(CreateSettingsToggleRow(
            "Fokusmodus beim Start",
            "Öffnet neue Sitzungen direkt in der reduzierten Schreibansicht.",
            focusSwitch,
            textBrush,
            mutedBrush));
        behaviorContent.Children.Add(CreateSettingsSeparator(borderBrush));
        behaviorContent.Children.Add(CreateSettingsToggleRow(
            "Rechtschreibprüfung",
            "Markiert mögliche Schreibfehler direkt im Editor.",
            spellcheckSwitch,
            textBrush,
            mutedBrush));
        behaviorContent.Children.Add(CreateSettingsSeparator(borderBrush));
        behaviorContent.Children.Add(CreateSettingsToggleRow(
            "Auto-Save",
            "Speichert bereits angelegte Dateien automatisch.",
            autoSaveSwitch,
            textBrush,
            mutedBrush));
        behaviorContent.Children.Add(CreateSettingsField(
            "Intervall",
            "Sekunden zwischen automatischen Speicherungen",
            autoSaveBox,
            textBrush,
            mutedBrush));
        behaviorContent.Children.Add(CreateSettingsSeparator(borderBrush));
        behaviorContent.Children.Add(CreateSettingsToggleRow(
            "Letztes Dokument beim Start öffnen",
            "Öffnet die zuletzt aktive Datei erneut, wenn sie noch vorhanden ist.",
            reopenLastDocumentSwitch,
            textBrush,
            mutedBrush));

        var editorGrid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12
        };
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var widthField = CreateSettingsField("Editorbreite", "680–940 px", widthBox, textBrush, mutedBrush);
        var fontField = CreateSettingsField("Schriftgröße", "13–22 px", fontBox, textBrush, mutedBrush);
        var lineHeightField = CreateSettingsField("Zeilenhöhe", "1,35–2,00", lineHeightBox, textBrush, mutedBrush);
        var goalField = CreateSettingsField("Schreibziel", "Wörter · 0 = aus", goalBox, textBrush, mutedBrush);

        Grid.SetColumn(widthField, 0);
        Grid.SetRow(widthField, 0);
        Grid.SetColumn(fontField, 1);
        Grid.SetRow(fontField, 0);
        Grid.SetColumn(lineHeightField, 0);
        Grid.SetRow(lineHeightField, 1);
        Grid.SetColumn(goalField, 1);
        Grid.SetRow(goalField, 1);
        editorGrid.Children.Add(widthField);
        editorGrid.Children.Add(fontField);
        editorGrid.Children.Add(lineHeightField);
        editorGrid.Children.Add(goalField);

        var panel = new StackPanel
        {
            Spacing = 12,
            Width = 440
        };
        panel.Children.Add(CreateSettingsSection(
            "Darstellung",
            "Wähle die Oberfläche für Editor und App-Shell.",
            appearanceContent,
            cardBrush,
            borderBrush,
            textBrush,
            mutedBrush));
        panel.Children.Add(CreateSettingsSection(
            "Verhalten",
            "Einstellungen für Fokus, Prüfung und Speichern.",
            behaviorContent,
            cardBrush,
            borderBrush,
            textBrush,
            mutedBrush));
        panel.Children.Add(CreateSettingsSection(
            "Editor",
            "Passe Lesefläche und Typografie an.",
            editorGrid,
            cardBrush,
            borderBrush,
            textBrush,
            mutedBrush));

        var settingsScroll = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 560,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var dialog = CreateDialog("Einstellungen", settingsScroll);
        dialog.PrimaryButtonText = "Übernehmen";
        dialog.CloseButtonText = "Abbrechen";
        dialog.DefaultButton = ContentDialogButton.Primary;

        dialog.Resources["ToggleSwitchFillOn"] = accentBrush;
        dialog.Resources["ToggleSwitchFillOnPointerOver"] = accentHoverBrush;
        dialog.Resources["ToggleSwitchFillOnPressed"] = accentPressedBrush;
        dialog.Resources["ToggleSwitchStrokeOn"] = accentBrush;
        dialog.Resources["ToggleSwitchStrokeOnPointerOver"] = accentHoverBrush;
        dialog.Resources["ToggleSwitchStrokeOnPressed"] = accentPressedBrush;
        dialog.Resources["ToggleSwitchKnobFillOn"] = onAccentTextBrush;
        dialog.Resources["ToggleSwitchKnobFillOnPointerOver"] = onAccentTextBrush;
        dialog.Resources["ToggleSwitchKnobFillOnPressed"] = onAccentTextBrush;
        dialog.Resources["AccentButtonBackground"] = accentBrush;
        dialog.Resources["AccentButtonBackgroundPointerOver"] = accentHoverBrush;
        dialog.Resources["AccentButtonBackgroundPressed"] = accentPressedBrush;
        dialog.Resources["AccentButtonForeground"] = onAccentTextBrush;
        dialog.Resources["AccentButtonForegroundPointerOver"] = onAccentTextBrush;
        dialog.Resources["AccentButtonForegroundPressed"] = onAccentTextBrush;

        void ApplySettingsDialogPalette(bool light)
        {
            textBrush.Color = light
                ? Color.FromArgb(255, 30, 29, 27)
                : Color.FromArgb(255, 244, 241, 236);
            mutedBrush.Color = light
                ? Color.FromArgb(255, 101, 96, 88)
                : Color.FromArgb(255, 184, 178, 168);
            cardBrush.Color = light
                ? Color.FromArgb(255, 250, 247, 242)
                : Color.FromArgb(255, 32, 33, 36);
            controlBrush.Color = light
                ? Color.FromArgb(255, 255, 253, 249)
                : Color.FromArgb(255, 23, 24, 26);
            borderBrush.Color = light
                ? Color.FromArgb(255, 221, 213, 202)
                : Color.FromArgb(255, 52, 54, 58);
            accentBrush.Color = light
                ? Color.FromArgb(255, 204, 120, 92)
                : Color.FromArgb(255, 216, 138, 108);
            accentHoverBrush.Color = light
                ? Color.FromArgb(255, 187, 103, 78)
                : Color.FromArgb(255, 228, 154, 126);
            accentPressedBrush.Color = light
                ? Color.FromArgb(255, 169, 88, 67)
                : Color.FromArgb(255, 198, 116, 88);
            dialog.RequestedTheme = ThemeToElementTheme(currentSettings.Theme);
        }

        Task latestSettingsPreviewTask = Task.CompletedTask;

        async Task RunSettingsPreviewAfterAsync(
            Task previousPreviewTask,
            string previewTheme,
            int previewEditorWidth,
            int previewFontSize,
            double previewLineHeight)
        {
            try
            {
                await previousPreviewTask;
            }
            catch
            {
                // A later preview should still be allowed to replace a failed transient preview.
            }

            await SendSettingsPreviewAsync(previewTheme, previewEditorWidth, previewFontSize, previewLineHeight);
        }

        void QueueSettingsPreview()
        {
            var previewTheme = themeBox.SelectedIndex == 0 ? "light" : "dark";
            var previewEditorWidth = ReadSettingsIntPreviewValue(widthBox, originalEditorWidth, 680, 940);
            var previewFontSize = ReadSettingsIntPreviewValue(fontBox, originalFontSize, 13, 22);
            var previewLineHeight = ReadSettingsDoublePreviewValue(lineHeightBox, originalLineHeight, 1.35, 2.0);
            var previousPreviewTask = latestSettingsPreviewTask;
            latestSettingsPreviewTask = RunSettingsPreviewAfterAsync(
                previousPreviewTask,
                previewTheme,
                previewEditorWidth,
                previewFontSize,
                previewLineHeight);
        }

        themeBox.SelectionChanged += (_, _) =>
        {
            var previewTheme = themeBox.SelectedIndex == 0 ? "light" : "dark";
            if (!string.Equals(currentSettings.Theme, previewTheme, StringComparison.OrdinalIgnoreCase))
            {
                currentSettings.Theme = previewTheme;
                ApplyShellTheme();
                ApplySettingsDialogPalette(IsLightShellTheme(previewTheme));
            }

            QueueSettingsPreview();
        };
        widthBox.ValueChanged += (_, _) => QueueSettingsPreview();
        fontBox.ValueChanged += (_, _) => QueueSettingsPreview();
        lineHeightBox.ValueChanged += (_, _) => QueueSettingsPreview();

        var dialogResult = await dialog.ShowAsync();
        await latestSettingsPreviewTask;
        if (dialogResult != ContentDialogResult.Primary)
        {
            await RestoreSettingsPreviewAsync(originalTheme, originalEditorWidth, originalFontSize, originalLineHeight);
            return;
        }

        currentSettings.Theme = themeBox.SelectedIndex == 0 ? "light" : "dark";
        currentSettings.FocusMode = focusSwitch.IsOn;
        currentSettings.Spellcheck = spellcheckSwitch.IsOn;
        currentSettings.AutoSaveEnabled = autoSaveSwitch.IsOn;
        currentSettings.ReopenLastDocument = reopenLastDocumentSwitch.IsOn;
        currentSettings.AutoSaveIntervalSeconds = ReadSettingsIntPreviewValue(autoSaveBox, currentSettings.AutoSaveIntervalSeconds, 10, 600);
        currentSettings.EditorWidth = ReadSettingsIntPreviewValue(widthBox, originalEditorWidth, 680, 940);
        currentSettings.FontSize = ReadSettingsIntPreviewValue(fontBox, originalFontSize, 13, 22);
        currentSettings.LineHeight = ReadSettingsDoublePreviewValue(lineHeightBox, originalLineHeight, 1.35, 2.0);
        currentSettings.WordGoal = ReadSettingsIntPreviewValue(goalBox, currentSettings.WordGoal, 0, 100000);

        Root.RequestedTheme = ThemeToElementTheme(currentSettings.Theme);
        currentTheme = Root.RequestedTheme;
        ApplyShellTheme();
        SaveSettings();
        RestartAutoSaveTimer();
        await SendSettingsAsync();
        UpdateStatus();
        await NotifyAsync("Einstellungen aktualisiert", "Editor und Shell wurden angepasst.");
    }

    private async Task RestoreSettingsPreviewAsync(string theme, int editorWidth, int fontSize, double lineHeight)
    {
        currentSettings.Theme = NormalizeTheme(theme);
        ApplyShellTheme();
        await SendSettingsPreviewAsync(currentSettings.Theme, editorWidth, fontSize, lineHeight);
    }

    private static int ReadSettingsIntPreviewValue(NumberBox box, int fallback, int min, int max)
    {
        var value = box.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return Math.Clamp(fallback, min, max);
        }

        return (int)Math.Clamp(value, min, max);
    }

    private static double ReadSettingsDoublePreviewValue(NumberBox box, double fallback, double min, double max)
    {
        var value = box.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return Math.Clamp(fallback, min, max);
        }

        return Math.Clamp(value, min, max);
    }

    private static ToggleSwitch CreateSettingsToggle(string accessibleName, bool isOn)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = isOn,
            OnContent = string.Empty,
            OffContent = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, accessibleName);
        return toggle;
    }

    private static Grid CreateSettingsToggleRow(
        string title,
        string description,
        ToggleSwitch toggle,
        Brush textBrush,
        Brush mutedBrush)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { Spacing = 2 };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = textBrush,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        copy.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = mutedBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        });
        row.Children.Add(copy);

        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    private static StackPanel CreateSettingsField(
        string label,
        string hint,
        Control control,
        Brush textBrush,
        Brush mutedBrush)
    {
        var field = new StackPanel { Spacing = 6 };
        field.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = textBrush,
            FontSize = 12.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        field.Children.Add(control);
        field.Children.Add(new TextBlock
        {
            Text = hint,
            Foreground = mutedBrush,
            FontSize = 10.5
        });
        return field;
    }

    private static Border CreateSettingsSeparator(Brush borderBrush) => new()
    {
        Height = 1,
        Background = borderBrush,
        Opacity = 0.68,
        Margin = new Thickness(0, 1, 0, 1)
    };

    private static Border CreateSettingsSection(
        string title,
        string description,
        UIElement content,
        Brush surfaceBrush,
        Brush borderBrush,
        Brush textBrush,
        Brush mutedBrush)
    {
        var stack = new StackPanel { Spacing = 12 };
        var heading = new StackPanel { Spacing = 3 };
        heading.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = textBrush,
            FontSize = 13.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        heading.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = mutedBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(heading);
        stack.Children.Add(content);

        return new Border
        {
            Background = surfaceBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = stack
        };
    }

    private async Task ShowAboutAsync()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        var lightDialog = IsLightShellTheme(currentSettings.Theme);
        var textBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 30, 29, 27)
            : Color.FromArgb(255, 245, 241, 234));
        var metadataBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 99, 94, 86)
            : Color.FromArgb(255, 174, 169, 160));
        var borderBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 222, 214, 204)
            : Color.FromArgb(255, 66, 61, 54));
        var accentBrush = new SolidColorBrush(Color.FromArgb(255, 184, 93, 66));
        var logoBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 247, 244, 238)
            : Color.FromArgb(255, 22, 21, 20));

        var panel = new StackPanel
        {
            Spacing = 20,
            Width = 420
        };

        var header = new Grid
        {
            ColumnSpacing = 16
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "markdown_studio_icon.png");
        var logoSource = File.Exists(logoPath)
            ? new BitmapImage(new Uri(logoPath))
            : null;

        header.Children.Add(new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(16),
            Background = logoBrush,
            Child = logoSource is null
                ? new TextBlock
                {
                    Text = "M",
                    Foreground = textBrush,
                    FontSize = 24,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
                : new Image
                {
                    Source = logoSource,
                    Width = 58,
                    Height = 58,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
        });

        var titleStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 3
        };
        Grid.SetColumn(titleStack, 1);
        titleStack.Children.Add(new TextBlock
        {
            Text = "Markdown Studio Pro",
            Foreground = textBrush,
            FontSize = 21,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Windows-Version  ·  Version " + version,
            Foreground = metadataBrush,
            FontSize = 13
        });
        header.Children.Add(titleStack);
        panel.Children.Add(header);

        panel.Children.Add(new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Height = 1,
            Opacity = 0.75
        });

        panel.Children.Add(new StackPanel
        {
            Spacing = 7,
            Children =
            {
                new TextBlock
                {
                    Text = "Kontakt",
                    Foreground = metadataBrush,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = "Nils Groon",
                    Foreground = textBrush,
                    FontSize = 15,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = "Großer Weidstückerweg 12\n68163 Mannheim",
                    Foreground = metadataBrush,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        });

        var donateLink = new HyperlinkButton
        {
            Content = "paypal.me/Shyiox",
            NavigateUri = new Uri("https://paypal.me/Shyiox"),
            Foreground = accentBrush,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        panel.Children.Add(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "Spenden",
                    Foreground = metadataBrush,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = "Freiwillige Unterstützung für die Weiterentwicklung.",
                    Foreground = metadataBrush,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                },
                donateLink
            }
        });

        var dialog = CreateDialog("Über Markdown Studio Pro", panel);
        dialog.CloseButtonText = "Schließen";
        await dialog.ShowAsync();
    }

    private void RestartAutoSaveTimer()
    {
        autoSaveTimer.Stop();
        autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(currentSettings.AutoSaveIntervalSeconds, 10, 600));
        if (currentSettings.AutoSaveEnabled)
        {
            autoSaveTimer.Start();
        }
    }

    private async Task NotifyAsync(string title, string message)
    {
        if (EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var script = "window.markdownStudio && window.markdownStudio.notify(" +
            JsonSerializer.Serialize(title ?? string.Empty) + "," +
            JsonSerializer.Serialize(message ?? string.Empty) + ");";
        await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "notify");
    }

    private async Task<string> ExecuteScriptWithTimeoutAsync(string script, TimeSpan timeout, string operation)
    {
        if (EditorWebView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("CoreWebView2 ist nicht bereit für: " + operation);
        }

        AppendStartupTrace("ExecuteScript start: " + operation);

        try
        {
            var scriptTask = EditorWebView.CoreWebView2.ExecuteScriptAsync(script).AsTask();
            var completed = await Task.WhenAny(scriptTask, Task.Delay(timeout));
            if (completed != scriptTask)
            {
                throw new TimeoutException();
            }

            var result = await scriptTask;
            AppendStartupTrace("ExecuteScript done: " + operation + " => " + TrimForLog(result));
            return result;
        }
        catch (TimeoutException ex)
        {
            var wrapped = new TimeoutException($"WebView2 Script-Operation '{operation}' hat nach {timeout.TotalSeconds:0.#} Sekunden nicht geantwortet.", ex);
            AppendStartupTrace(wrapped.Message);
            throw wrapped;
        }
    }

    private static string TrimForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 160 ? value : value[..160] + "...";
    }

    private static void AppendStartupTrace(string message)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "winui-startup-trace.log");
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Trace logging must never block startup.
        }
    }

    private string BuildStartupStateMessage()
    {
        var rootLoaderPath = Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll");
        var runtimeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application");
        var tracePath = Path.Combine(AppContext.BaseDirectory, "winui-startup-trace.log");

        return string.Join(Environment.NewLine, new[]
        {
            "Diagnose:",
            "Editor: " + editorPath,
            "Ausgabeordner: " + AppContext.BaseDirectory,
            "Root-WebView2Loader.dll: " + (File.Exists(rootLoaderPath) ? "vorhanden" : "fehlt") + " (" + rootLoaderPath + ")",
            "WebView2 Runtime: " + (Directory.Exists(runtimeRoot) ? "gefunden" : "nicht gefunden") + " (" + runtimeRoot + ")",
            "Startup-Trace: " + tracePath
        });
    }

    private ContentDialog CreateDialog(string title, object content) => new()
    {
        XamlRoot = Root.XamlRoot,
        RequestedTheme = ThemeToElementTheme(currentSettings.Theme),
        Title = title,
        Content = content
    };

    private async Task ShowMessageAsync(string title, string message)
    {
        var scrollViewer = new ScrollViewer
        {
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            MaxHeight = 520
        };

        var dialog = CreateDialog(title, scrollViewer);
        dialog.CloseButtonText = "OK";
        await dialog.ShowAsync();
    }

    private string BuildStartupErrorMessage(Exception ex)
    {
        var rootLoaderPath = Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll");
        var nativeLoaderPath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "WebView2Loader.dll");
        var runtimeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application");

        var message = new StringBuilder();
        message.AppendLine(ex.Message);

        if (ex is FileNotFoundException || ex is DllNotFoundException || ex.Message.Contains("Modul", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("module", StringComparison.OrdinalIgnoreCase))
        {
            message.AppendLine();
            message.AppendLine("WebView2 konnte eine native Komponente nicht laden.");
            message.AppendLine("Prüfe, ob WebView2Loader.dll direkt neben der EXE liegt und die Microsoft Edge WebView2 Runtime installiert ist.");
        }

        message.AppendLine();
        message.AppendLine($"Editor: {editorPath}");
        message.AppendLine($"Ausgabeordner: {AppContext.BaseDirectory}");
        message.AppendLine($"Root-WebView2Loader.dll: {(File.Exists(rootLoaderPath) ? "vorhanden" : "fehlt")} ({rootLoaderPath})");
        message.AppendLine($"Runtime-WebView2Loader.dll: {(File.Exists(nativeLoaderPath) ? "vorhanden" : "fehlt")} ({nativeLoaderPath})");
        message.AppendLine($"WebView2 Runtime: {(Directory.Exists(runtimeRoot) ? "gefunden" : "nicht gefunden")} ({runtimeRoot})");
        message.AppendLine($"Fehlertyp: {ex.GetType().FullName}");

        if (ex.InnerException is not null)
        {
            message.AppendLine($"Inner: {ex.InnerException.Message}");
        }

        return message.ToString().Trim();
    }

    private static void WriteStartupLog(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "winui-startup-error.log");
            File.WriteAllText(logPath, ex.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Never let crash logging crash startup.
        }
    }

    private void LoadSettings()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var path = Path.Combine(AppDataDirectory, "settings.json");
            if (File.Exists(path))
            {
                currentSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path, Utf8NoBom), jsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            currentSettings = new AppSettings();
        }

        NormalizeSettings();
    }

    private void NormalizeSettings()
    {
        currentSettings.Theme = NormalizeTheme(currentSettings.Theme);
        currentSettings.AutoSaveIntervalSeconds = Math.Clamp(currentSettings.AutoSaveIntervalSeconds, 10, 600);
        currentSettings.EditorWidth = Math.Clamp(currentSettings.EditorWidth, 680, 940);
        currentSettings.FontSize = Math.Clamp(currentSettings.FontSize, 13, 22);
        currentSettings.LineHeight = Math.Clamp(currentSettings.LineHeight, 1.35, 2.0);
        currentSettings.WordGoal = Math.Clamp(currentSettings.WordGoal, 0, 100000);
        currentSettings.WindowWidth = Math.Clamp(currentSettings.WindowWidth, 820, 2560);
        currentSettings.WindowHeight = Math.Clamp(currentSettings.WindowHeight, 720, 1600);
        currentSettings.LastDocumentPath = NormalizeInitialPath(currentSettings.LastDocumentPath);
    }

    private static string NormalizeTheme(string? theme)
    {
        return theme?.Trim().ToLowerInvariant() switch
        {
            "light" or "clean" or "sepia" => "light",
            "dark" or "midnight" => "dark",
            _ => "dark"
        };
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var path = Path.Combine(AppDataDirectory, "settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(currentSettings, jsonOptions), Utf8NoBom);
        }
        catch
        {
            // Settings persistence must not break editing.
        }
    }

    private void LoadRecentFiles()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var path = Path.Combine(AppDataDirectory, "recent.json");
            if (!File.Exists(path))
            {
                return;
            }

            var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path, Utf8NoBom), jsonOptions) ?? new List<string>();
            recentFiles.Clear();
            recentFiles.AddRange(saved.Where(File.Exists).Take(MaxRecentFiles));
        }
        catch
        {
            recentFiles.Clear();
        }
    }

    private void SaveRecentFiles()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var path = Path.Combine(AppDataDirectory, "recent.json");
            File.WriteAllText(path, JsonSerializer.Serialize(recentFiles, jsonOptions), Utf8NoBom);
        }
        catch
        {
            // Recent files are helpful but not critical.
        }
    }

    private void AddRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        recentFiles.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
        recentFiles.Insert(0, path);

        if (recentFiles.Count > MaxRecentFiles)
        {
            recentFiles.RemoveRange(MaxRecentFiles, recentFiles.Count - MaxRecentFiles);
        }

        SaveRecentFiles();
    }

    private static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Markdown Studio Pro");

    private string? ResolveLastDocumentPathForStartup()
    {
        if (!currentSettings.ReopenLastDocument)
        {
            return null;
        }

        return NormalizeInitialPath(currentSettings.LastDocumentPath);
    }

    private static string? NormalizeInitialPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var cleaned = path.Trim().Trim('"');
        if (Uri.TryCreate(cleaned, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            cleaned = uri.LocalPath;
        }

        return File.Exists(cleaned) ? Path.GetFullPath(cleaned) : null;
    }

    private static async Task<string> ReadMarkdownFileAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        return DecodeMarkdownBytes(bytes);
    }

    private static async Task<byte[]> ReadStorageFileBytesAsync(StorageFile file)
    {
        using var stream = await file.OpenReadAsync();
        using var dataReader = new DataReader(stream);
        var length = (uint)stream.Size;
        await dataReader.LoadAsync(length);
        var bytes = new byte[length];
        dataReader.ReadBytes(bytes);
        return bytes;
    }

    private static string DecodeMarkdownBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string BuildSuggestedMarkdownFileName(string markdown, string? existingPath)
    {
        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            var existingFileName = Path.GetFileName(existingPath);
            if (!string.IsNullOrWhiteSpace(existingFileName))
            {
                return existingFileName;
            }
        }

        var safeTitle = SanitizeFileNameStem(ExtractMarkdownTitle(markdown));
        return safeTitle.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? safeTitle : safeTitle + ".md";
    }

    private static string ExtractMarkdownTitle(string markdown)
    {
        return TryExtractMarkdownTitle(markdown) ?? "dokument";
    }

    private static string? TryExtractMarkdownTitle(string markdown)
    {
        var normalized = NormalizeLineEndings(markdown ?? string.Empty);
        var lines = normalized.Split('\n');
        var index = 0;

        if (lines.Length > 0 && string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
        {
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.Equals(line, "---", StringComparison.Ordinal))
                {
                    index = i + 1;
                    break;
                }

                var titleMatch = Regex.Match(line, "^title\\s*:\\s*[\"']?(.*?)[\"']?\\s*$", RegexOptions.IgnoreCase);
                if (titleMatch.Success)
                {
                    var title = StripMarkdownFormatting(titleMatch.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return title;
                    }
                }
            }
        }

        for (var i = index; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], "^\\s{0,3}#{1,6}\\s+(.+?)\\s*#*\\s*$");
            if (match.Success)
            {
                var title = StripMarkdownFormatting(match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }
        }

        return null;
    }

    private static string StripMarkdownFormatting(string value)
    {
        var text = value.Trim();
        text = Regex.Replace(text, "!\\[[^\\]]*\\]\\([^\\)]*\\)", string.Empty);
        text = Regex.Replace(text, "\\[([^\\]]+)\\]\\([^\\)]*\\)", "$1");
        text = Regex.Replace(text, "`([^`]+)`", "$1");
        text = Regex.Replace(text, "(\\*\\*|__)(.*?)\\1", "$2");
        text = Regex.Replace(text, "(\\*|_)(.*?)\\1", "$2");
        text = Regex.Replace(text, "~~(.*?)~~", "$1");
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        return text.Trim();
    }

    private static string SanitizeFileNameStem(string title)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToHashSet();
        var builder = new StringBuilder();

        foreach (var character in title)
        {
            builder.Append(char.IsControl(character) || invalidCharacters.Contains(character) ? ' ' : character);
        }

        var safe = Regex.Replace(builder.ToString(), "\\s+", " ").Trim(' ', '.', '-');
        if (safe.Length > 80)
        {
            safe = safe[..80].Trim(' ', '.', '-');
        }

        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "dokument";
        }

        var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        return reservedNames.Contains(safe) ? "dokument-" + safe : safe;
    }

    private static string NormalizeLineEndings(string value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static int? ReadInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
            ? number
            : null;
    }

    private static ElementTheme ThemeToElementTheme(string? theme)
    {
        return IsLightShellTheme(theme) ? ElementTheme.Light : ElementTheme.Dark;
    }

    private static bool IsLightShellTheme(string? theme)
    {
        return string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateDocumentStats(string markdown)
    {
        var text = Regex.Replace(NormalizeLineEndings(markdown ?? string.Empty), "[#>*_`\\[\\]()|~-]", " ");
        currentWordCount = Regex.Matches(text, "\\S+").Count;
        currentCharacterCount = NormalizeLineEndings(markdown ?? string.Empty).Length;
    }

    private string ResolveDisplayTitle()
    {
        if (!string.IsNullOrWhiteSpace(currentFilePath))
        {
            return Path.GetFileName(currentFilePath);
        }

        if (!string.IsNullOrWhiteSpace(initialFilePath))
        {
            return Path.GetFileName(initialFilePath);
        }

        return TryExtractMarkdownTitle(currentMarkdown) ?? "Unbenannt";
    }

    private void UpdateTitle()
    {
        var displayTitle = ResolveDisplayTitle();
        Title = isDirty ? $"{displayTitle} * - {AppName}" : $"{displayTitle} - {AppName}";
    }

    private void UpdateStatus()
    {
        var displayTitle = ResolveDisplayTitle();
        DocumentTitleText.Text = displayTitle;
        DocumentPathText.Text = currentFilePath is null
            ? "Keine Datei geöffnet"
            : currentFilePath;

        SaveStateText.Text = isSaving
            ? "Speichert…"
            : isDirty ? "Ungespeichert" : "Gespeichert";

        SaveStateText.Foreground = new SolidColorBrush(isSaving || isDirty
            ? Color.FromArgb(255, 204, 120, 92)
            : Color.FromArgb(255, 160, 157, 150));

        SendSaveStateToEditor();
    }

    private void SendSaveStateToEditor()
    {
        if (!editorReady || EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var state = isSaving ? "saving" : isDirty ? "unsaved" : "saved";
        var payload = JsonSerializer.Serialize(new { type = "saveState", state }, jsonOptions);
        try
        {
            EditorWebView.CoreWebView2.PostWebMessageAsJson(payload);
        }
        catch
        {
            // Save-state presentation must not interrupt editing or shutdown.
        }
    }

    private async void NewButton_Click(object sender, RoutedEventArgs e) => await NewDocumentAsync();
    private async void OpenButton_Click(object sender, RoutedEventArgs e) => await OpenDocumentAsync();
    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveDocumentAsync(await GetMarkdownFromEditorAsync(), forceSaveAs: false);
    private async void SaveAsButton_Click(object sender, RoutedEventArgs e) => await SaveDocumentAsync(await GetMarkdownFromEditorAsync(), forceSaveAs: true);
    private async void RecentFilesButton_Click(object sender, RoutedEventArgs e) => await OpenRecentAsync();
    private async void CopyMarkdownButton_Click(object sender, RoutedEventArgs e) => CopyMarkdown(await GetMarkdownFromEditorAsync());
    private async void ExportButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("exportHtml");
    private async void ExportMarkdownFileButton_Click(object sender, RoutedEventArgs e) => await ExportMarkdownFileAsync(await GetMarkdownFromEditorAsync());
    private async void ExportHtmlButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("exportHtml");
    private async void PrintPdfButton_Click(object sender, RoutedEventArgs e) => await PrintPdfAsync();
    private async void ShowSourceButton_Click(object sender, RoutedEventArgs e) => await ShowSourceAsync(await GetMarkdownFromEditorAsync());
    private async void HeadingOneButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("h1");
    private async void HeadingTwoButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("h2");
    private async void HeadingThreeButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("h3");
    private async void BoldButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("bold");
    private async void ItalicButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("italic");
    private async void StrikeButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("strike");
    private async void QuoteButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("quote");
    private async void CodeButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("code");
    private async void InlineCodeButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("inlineCode");
    private async void TableButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("table");
    private async void TaskListButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("task");
    private async void HorizontalRuleButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("hr");
    private async void LinkButton_Click(object sender, RoutedEventArgs e) => await RunEditorFormatAsync("link");
    private async void TabHelpButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("showTabHelp");
    private async void PaletteButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("palette");
    private async void AddTableRowButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("addTableRow");
    private async void AddTableColumnButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("addTableCol");
    private async void DeleteTableRowButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("delTableRow");
    private async void DeleteTableColumnButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("delTableCol");
    private async void FindReplaceButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("findReplace");
    private async void FocusModeButton_Click(object sender, RoutedEventArgs e) => await RunEditorCommandAsync("toggleFocus");
    private async void SettingsButton_Click(object sender, RoutedEventArgs e) => await ShowSettingsAsync();
    private async void AboutButton_Click(object sender, RoutedEventArgs e) => await ShowAboutAsync();

    private async void ThemeLightButton_Click(object sender, RoutedEventArgs e) => await ApplyEditorThemeAsync("light", ElementTheme.Light);
    private async void ThemeDarkButton_Click(object sender, RoutedEventArgs e) => await ApplyEditorThemeAsync("dark", ElementTheme.Dark);

    private async Task ApplyEditorThemeAsync(string theme, ElementTheme shellTheme)
    {
        currentTheme = shellTheme;
        currentSettings.Theme = theme;
        Root.RequestedTheme = shellTheme;
        ApplyShellTheme();
        SaveSettings();
        await RunEditorCommandAsync("applyTheme" + char.ToUpperInvariant(theme[0]) + theme[1..]);
        await SendSettingsAsync();
    }
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "dark";
    public bool FocusMode { get; set; }
    public bool ToolsOpen { get; set; }
    public bool Spellcheck { get; set; } = true;
    public bool AutoSaveEnabled { get; set; }
    public int AutoSaveIntervalSeconds { get; set; } = 30;
    public int WordGoal { get; set; }
    public int TargetWordCount
    {
        get => WordGoal;
        set => WordGoal = value;
    }
    public int EditorWidth { get; set; } = 900;
    public int FontSize { get; set; } = 16;
    public double LineHeight { get; set; } = 1.68;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 860;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool ReopenLastDocument { get; set; }
    public string? LastDocumentPath { get; set; }
}

public sealed record RecentFileItem(string Path, string Name)
{
    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? Path : $"{Name} - {Path}";
}
