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
    private bool closeAllowed;
    private ElementTheme currentTheme = ElementTheme.Dark;
    private AppSettings currentSettings = new();
    private readonly DispatcherTimer autoSaveTimer = new();
    private DateTime? lastSavedAt;
    private int currentWordCount;
    private int currentCharacterCount;

    public MainWindow(string? initialFilePath = null)
    {
        InitializeComponent();

        this.initialFilePath = NormalizeInitialPath(initialFilePath);
        editorPath = Path.Combine(AppContext.BaseDirectory, "App", "editor.html");

        LoadSettings();
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
            appWindow.Move(new PointInt32
            {
                X = workArea.X + Math.Max(0, (workArea.Width - windowWidth) / 2),
                Y = workArea.Y + Math.Max(0, (workArea.Height - windowHeight) / 2)
            });
            ApplyWindowChrome();
            appWindow.Closing += OnAppWindowClosing;
        }
        catch
        {
            // Window sizing and close interception are conveniences. The editor can run without them.
        }
    }

    private void ApplyWindowChrome()
    {
        if (appWindow?.TitleBar is not { } titleBar)
        {
            return;
        }

        titleBar.BackgroundColor = Color.FromArgb(255, 32, 31, 28);
        titleBar.ForegroundColor = Color.FromArgb(255, 250, 249, 245);
        titleBar.InactiveBackgroundColor = Color.FromArgb(255, 24, 23, 21);
        titleBar.InactiveForegroundColor = Color.FromArgb(255, 160, 157, 150);
        titleBar.ButtonBackgroundColor = Color.FromArgb(255, 32, 31, 28);
        titleBar.ButtonForegroundColor = Color.FromArgb(255, 250, 249, 245);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 58, 53, 47);
        titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 204, 120, 92);
        titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 24, 23, 21);
        titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(255, 24, 23, 21);
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 157, 150);
    }

    private void ApplyShellTheme()
    {
        var shellTheme = ThemeToElementTheme(currentSettings.Theme);
        Root.RequestedTheme = shellTheme;
        TopBar.RequestedTheme = shellTheme;
        MainCommandBar.RequestedTheme = shellTheme;
        currentTheme = shellTheme;

        try
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        catch
        {
            // Mica is a polish layer. The app should keep running on machines that do not expose it.
        }

        if (IsLightShellTheme(currentSettings.Theme))
        {
            var titleBrush = new SolidColorBrush(Color.FromArgb(255, 30, 29, 27));
            var metadataBrush = new SolidColorBrush(Color.FromArgb(255, 98, 94, 87));
            var separatorBrush = new SolidColorBrush(Color.FromArgb(255, 178, 171, 160));

            Root.Background = new SolidColorBrush(Color.FromArgb(255, 250, 249, 245));
            TopBar.Background = new SolidColorBrush(Color.FromArgb(230, 255, 253, 248));
            TopBar.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 230, 223, 216));
            DocumentTitleText.Foreground = titleBrush;
            DocumentPathText.Foreground = metadataBrush;
            MetadataSeparatorOne.Foreground = separatorBrush;
            MainCommandBar.Foreground = titleBrush;
            ApplyCommandBarForeground(titleBrush);
        }
        else
        {
            var titleBrush = new SolidColorBrush(Color.FromArgb(255, 250, 249, 245));
            var metadataBrush = new SolidColorBrush(Color.FromArgb(255, 160, 157, 150));
            var separatorBrush = new SolidColorBrush(Color.FromArgb(255, 94, 90, 82));

            Root.Background = new SolidColorBrush(Color.FromArgb(255, 24, 23, 21));
            TopBar.Background = new SolidColorBrush(Color.FromArgb(230, 32, 31, 28));
            TopBar.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 58, 53, 47));
            DocumentTitleText.Foreground = titleBrush;
            DocumentPathText.Foreground = metadataBrush;
            MetadataSeparatorOne.Foreground = separatorBrush;
            MainCommandBar.Foreground = titleBrush;
            ApplyCommandBarForeground(titleBrush);
        }

        ApplyWindowChrome();
    }

    private void ApplyCommandBarForeground(Brush foreground)
    {
        foreach (var command in MainCommandBar.PrimaryCommands)
        {
            if (command is AppBarButton button)
            {
                button.Foreground = foreground;

                if (button.Icon is FontIcon icon)
                {
                    icon.Foreground = foreground;
                }
            }
        }
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
        }

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

        markdown = NormalizeLineEndings(markdown);
        await File.WriteAllTextAsync(targetPath, markdown, Utf8NoBom);
        currentFilePath = targetPath;
        currentMarkdown = markdown;
        lastSavedMarkdown = markdown;
        UpdateDocumentStats(markdown);
        isDirty = false;
        lastSavedAt = DateTime.Now;
        AddRecentFile(targetPath);
        UpdateTitle();
        UpdateStatus();
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
        var themeBox = new ComboBox
        {
            Header = "Theme",
            ItemsSource = new[] { "dark", "light", "sepia", "midnight" },
            SelectedItem = currentSettings.Theme,
            MinWidth = 220
        };
        var focusSwitch = new ToggleSwitch
        {
            Header = "Fokusmodus beim Start",
            IsOn = currentSettings.FocusMode
        };
        var spellcheckSwitch = new ToggleSwitch
        {
            Header = "Rechtschreibprüfung",
            IsOn = currentSettings.Spellcheck
        };
        var autoSaveSwitch = new ToggleSwitch
        {
            Header = "Auto-Save für gespeicherte Dateien",
            IsOn = currentSettings.AutoSaveEnabled
        };
        var autoSaveBox = new NumberBox
        {
            Header = "Auto-Save-Intervall (Sekunden)",
            Value = currentSettings.AutoSaveIntervalSeconds,
            Minimum = 10,
            Maximum = 600,
            SmallChange = 5,
            LargeChange = 30,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 220
        };
        var widthBox = new NumberBox
        {
            Header = "Editorbreite (px)",
            Value = currentSettings.EditorWidth,
            Minimum = 680,
            Maximum = 940,
            SmallChange = 20,
            LargeChange = 80,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 220
        };
        var fontBox = new NumberBox
        {
            Header = "Schriftgröße (px)",
            Value = currentSettings.FontSize,
            Minimum = 13,
            Maximum = 22,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 220
        };
        var lineHeightBox = new NumberBox
        {
            Header = "Zeilenhöhe",
            Value = currentSettings.LineHeight,
            Minimum = 1.35,
            Maximum = 2.0,
            SmallChange = 0.05,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 220
        };
        var goalBox = new NumberBox
        {
            Header = "Schreibziel (Wörter, 0 = aus)",
            Value = currentSettings.WordGoal,
            Minimum = 0,
            Maximum = 100000,
            SmallChange = 100,
            LargeChange = 1000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 220
        };

        var panel = new StackPanel { Spacing = 14, MinWidth = 520 };
        panel.Children.Add(themeBox);
        panel.Children.Add(focusSwitch);
        panel.Children.Add(spellcheckSwitch);
        panel.Children.Add(autoSaveSwitch);
        panel.Children.Add(autoSaveBox);
        panel.Children.Add(widthBox);
        panel.Children.Add(fontBox);
        panel.Children.Add(lineHeightBox);
        panel.Children.Add(goalBox);

        var dialog = CreateDialog("Einstellungen", panel);
        dialog.PrimaryButtonText = "Übernehmen";
        dialog.CloseButtonText = "Abbrechen";

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        currentSettings.Theme = themeBox.SelectedItem as string ?? currentSettings.Theme;
        currentSettings.FocusMode = focusSwitch.IsOn;
        currentSettings.Spellcheck = spellcheckSwitch.IsOn;
        currentSettings.AutoSaveEnabled = autoSaveSwitch.IsOn;
        currentSettings.AutoSaveIntervalSeconds = (int)Math.Clamp(autoSaveBox.Value, 10, 600);
        currentSettings.EditorWidth = (int)Math.Clamp(widthBox.Value, 680, 940);
        currentSettings.FontSize = (int)Math.Clamp(fontBox.Value, 13, 22);
        currentSettings.LineHeight = Math.Clamp(lineHeightBox.Value, 1.35, 2.0);
        currentSettings.WordGoal = (int)Math.Clamp(goalBox.Value, 0, 100000);

        Root.RequestedTheme = ThemeToElementTheme(currentSettings.Theme);
        currentTheme = Root.RequestedTheme;
        ApplyShellTheme();
        SaveSettings();
        RestartAutoSaveTimer();
        await SendSettingsAsync();
        UpdateStatus();
        await NotifyAsync("Einstellungen aktualisiert", "Editor und Shell wurden angepasst.");
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
    }

    private static string NormalizeTheme(string? theme)
    {
        return theme?.Trim().ToLowerInvariant() switch
        {
            "light" or "clean" => "light",
            "sepia" => "sepia",
            "midnight" => "midnight",
            "dark" => "dark",
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
                    var title = titleMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return StripMarkdownFormatting(title);
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

        return "dokument";
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
        return string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(theme, "sepia", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateDocumentStats(string markdown)
    {
        var text = Regex.Replace(NormalizeLineEndings(markdown ?? string.Empty), "[#>*_`\\[\\]()|~-]", " ");
        currentWordCount = Regex.Matches(text, "\\S+").Count;
        currentCharacterCount = NormalizeLineEndings(markdown ?? string.Empty).Length;
    }

    private void UpdateTitle()
    {
        var fileName = currentFilePath is null
            ? initialFilePath is null ? "Unbenannt" : Path.GetFileName(initialFilePath)
            : Path.GetFileName(currentFilePath);
        Title = isDirty ? $"{fileName} * - {AppName}" : $"{fileName} - {AppName}";
    }

    private void UpdateStatus()
    {
        var fileName = currentFilePath is null ? "Unbenannt" : Path.GetFileName(currentFilePath);
        DocumentTitleText.Text = fileName;
        DocumentPathText.Text = currentFilePath is null
            ? "Keine Datei geöffnet"
            : currentFilePath;

        SaveStateText.Text = isDirty
            ? currentSettings.AutoSaveEnabled && currentFilePath is not null ? "Auto-Save ausstehend" : "Nicht gespeichert"
            : lastSavedAt is null ? "Gespeichert" : "Gespeichert " + lastSavedAt.Value.ToString("HH:mm");

        SaveStateText.Foreground = new SolidColorBrush(isDirty
            ? Color.FromArgb(255, 204, 120, 92)
            : Color.FromArgb(255, 160, 157, 150));

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
    private async void ThemeSepiaButton_Click(object sender, RoutedEventArgs e) => await ApplyEditorThemeAsync("sepia", ElementTheme.Light);
    private async void ThemeMidnightButton_Click(object sender, RoutedEventArgs e) => await ApplyEditorThemeAsync("midnight", ElementTheme.Dark);

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
}

public sealed record RecentFileItem(string Path, string Name)
{
    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? Path : $"{Name} - {Path}";
}
