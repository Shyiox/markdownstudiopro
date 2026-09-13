using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT;
using WinRT.Interop;

namespace MarkdownStudioPro.WinUI;

public sealed partial class MainWindow : Window
{
    private const string AppName = "Markdown Studio Pro";
    private const int MaxRecentFiles = 8;
    private const int MinVisibleWindowPixels = 64;
    private const int MinWindowWidthLogical = 640;
    private const int MinWindowHeightLogical = 480;
    private const int V2AppearanceVersion = 2;
    private const string V2DefaultTheme = "light";
    private const int V2DefaultEditorWidth = 848;
    private const int V2DefaultFontSize = 20;
    private const double V2DefaultLineHeight = 1.45;
    private const string AppearanceBackupFileName = "settings.appearance-v1-backup.json";

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string editorPath;
    private readonly string? initialFilePath;
    private readonly JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly List<string> recentFiles = new();
    private readonly DocumentSessionCoordinator documentSession = new();
    private readonly SemaphoreSlim saveWriteGate = new(1, 1);
    private readonly AsyncOperationQueue dialogQueue = new();
    private readonly ShutdownLifetime shutdownLifetime = new();
    private readonly UiAsyncErrorBoundary uiErrorBoundary;

    private AppWindow? appWindow;
    private DesktopAcrylicController? desktopAcrylicController;
    private SystemBackdropConfiguration? backdropConfiguration;
    private string currentMarkdown = CreateNewDocumentMarkdown();
    private string lastSavedMarkdown = CreateNewDocumentMarkdown();
    private string? currentFilePath;
    private bool editorReady;
    private bool initialDocumentLoaded;
    private bool startupCompleting;
    private bool isDirty;
    private bool isSaving => documentSession.IsSaving;
    private bool isSupportedFileDrag;
    private bool closeAllowed;
    private ElementTheme currentTheme = ElementTheme.Dark;
    private AppSettings currentSettings = new();
    private readonly DispatcherTimer autoSaveTimer = new();
    private readonly DispatcherTimer nativeToastTimer = new();
    private int currentWordCount;
    private int currentCharacterCount;
    private int currentHeadingCount;
    private int currentReadingMinutes = 1;
    private bool hasSelectionStats;
    private int currentSelectionWordCount;
    private int currentSelectionCharacterCount;
    private Flyout? statisticsFlyout;
    private Storyboard? startupSkeletonPulse;
    private bool xamlRootHandlersAttached;

    public MainWindow(string? initialFilePath = null, bool hasExplicitInitialPath = false)
    {
        uiErrorBoundary = new UiAsyncErrorBoundary(
            (context, ex) => Diagnostics.LogException("UI." + context, ex),
            ReportUiErrorAsync);
        Diagnostics.StepStart("MainWindow.ctor");

        try
        {
            Diagnostics.RunStep("MainWindow.InitializeComponent", InitializeComponent);

            var launchRequest = new LaunchFileRequest(
                hasExplicitInitialPath || !string.IsNullOrWhiteSpace(initialFilePath),
                NormalizeInitialPath(initialFilePath));
            editorPath = Path.Combine(AppContext.BaseDirectory, "App", "editor.html");
            Diagnostics.Info("MainWindow.HasExplicitInitialPath", launchRequest.HasExplicitIntent.ToString());

            Diagnostics.RunStep("MainWindow.LoadSettings", LoadSettings);
            this.initialFilePath = StartupDocumentPolicy.Choose(
                launchRequest,
                currentSettings.ReopenLastDocument,
                NormalizeInitialPath(currentSettings.LastDocumentPath),
                File.Exists).FilePath;
            Diagnostics.RunStep("MainWindow.LoadRecentFiles", LoadRecentFiles);
            Diagnostics.RunStep("MainWindow.ConfigureAutoSaveTimer", ConfigureAutoSaveTimer);
            Diagnostics.RunStep("MainWindow.ConfigureNativeToastTimer", ConfigureNativeToastTimer);
            Diagnostics.RunStep("MainWindow.ConfigureWindow", ConfigureWindow);

            Diagnostics.RunStep("MainWindow.ApplyRequestedTheme", () =>
            {
                Root.RequestedTheme = ThemeToElementTheme(currentSettings.Theme);
                currentTheme = Root.RequestedTheme;
            });
            Diagnostics.RunStep("MainWindow.ConfigureBackdrop", ConfigureBackdrop);
            Diagnostics.RunStep("MainWindow.ApplyShellTheme", ApplyShellTheme);
            Diagnostics.RunStep("MainWindow.UpdateTitle", UpdateTitle);
            Diagnostics.RunStep("MainWindow.UpdateStatus", UpdateStatus);

            Diagnostics.RunStep("MainWindow.AttachLoaded", () =>
            {
                Root.Loaded += Root_Loaded;
                Root.SizeChanged += Root_SizeChanged;
                Closed += Window_Closed;
            });
            Diagnostics.StepOk("MainWindow.ctor");
        }
        catch (Exception ex)
        {
            Diagnostics.StepFailed("MainWindow.ctor", ex);
            throw;
        }
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

    private void ConfigureNativeToastTimer()
    {
        nativeToastTimer.Interval = TimeSpan.FromMilliseconds(2800);
        nativeToastTimer.Tick += NativeToastTimer_Tick;
    }

    private void NativeToastTimer_Tick(object? sender, object e)
    {
        nativeToastTimer.Stop();
        NativeToastHost.Visibility = Visibility.Collapsed;
    }

    private void ShowNativeToast(string title, string message)
    {
        NativeToastTitleText.Text = title ?? string.Empty;
        var hasMessage = !string.IsNullOrWhiteSpace(message);
        NativeToastMessageText.Text = hasMessage ? message : string.Empty;
        NativeToastMessageText.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        NativeToastHost.Visibility = Visibility.Visible;
        nativeToastTimer.Stop();
        nativeToastTimer.Start();
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
                // Without custom title-bar support Windows owns the non-client area.
                // Hide only our drag strip; the floating command band remains in the client surface.
                AppTitleBarDragRegion.Visibility = Visibility.Collapsed;
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "markdown_studio_icon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var initialMinWidth = Math.Min(MinWindowWidthLogical, Math.Max(1, workArea.Width));
            var initialMinHeight = Math.Min(MinWindowHeightLogical, Math.Max(1, workArea.Height));
            var maxWindowWidth = Math.Max(initialMinWidth, workArea.Width - 120);
            var maxWindowHeight = Math.Max(initialMinHeight, workArea.Height - 90);
            var windowWidth = Math.Clamp(currentSettings.WindowWidth, initialMinWidth, maxWindowWidth);
            var windowHeight = Math.Clamp(currentSettings.WindowHeight, initialMinHeight, maxWindowHeight);
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
        catch (Exception ex)
        {
            Diagnostics.LogException("MainWindow.ConfigureWindow/fallback", ex);
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

        var savedBounds = new RectInt32
        {
            X = savedX,
            Y = savedY,
            Width = windowWidth,
            Height = windowHeight
        };
        var displayArea = DisplayArea.GetFromRect(savedBounds, DisplayAreaFallback.None);
        if (displayArea is null)
        {
            return false;
        }

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

    private void ConfigureBackdrop()
    {
        if (desktopAcrylicController is not null || backdropConfiguration is not null)
        {
            UpdateBackdropAppearance();
            return;
        }

        if (!DesktopAcrylicController.IsSupported())
        {
            ApplyBackdropFallback();
            return;
        }

        try
        {
            DispatcherQueue.EnsureSystemDispatcherQueue();

            var configuration = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = ResolveBackdropTheme()
            };

            var controller = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Base
            };
            ApplyBackdropPalette(controller);

            if (!controller.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>()))
            {
                controller.Dispose();
                ApplyBackdropFallback();
                return;
            }

            controller.SetSystemBackdropConfiguration(configuration);
            desktopAcrylicController = controller;
            backdropConfiguration = configuration;

            Activated += Window_Activated;
            Root.ActualThemeChanged += Root_ActualThemeChanged;

            Root.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("MainWindow.ConfigureBackdrop/fallback", ex);
            desktopAcrylicController?.Dispose();
            desktopAcrylicController = null;
            backdropConfiguration = null;
            ApplyBackdropFallback();
        }
    }

    private void UpdateBackdropAppearance()
    {
        if (backdropConfiguration is not null)
        {
            backdropConfiguration.Theme = ResolveBackdropTheme();
        }

        if (desktopAcrylicController is not null)
        {
            ApplyBackdropPalette(desktopAcrylicController);
            Root.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            return;
        }

        ApplyBackdropFallback();
    }

    private void ApplyBackdropPalette(DesktopAcrylicController controller)
    {
        var light = IsLightShellTheme(currentSettings.Theme);
        controller.Kind = DesktopAcrylicKind.Base;
        controller.TintColor = light
            ? Color.FromArgb(255, 240, 242, 243)
            : Color.FromArgb(255, 24, 24, 26);
        controller.TintOpacity = light ? 0.22f : 0.58f;
        controller.LuminosityOpacity = light ? 0.56f : 0.34f;
        controller.FallbackColor = GetBackdropFallbackColor();
    }

    private void ApplyBackdropFallback()
    {
        Root.Background = new SolidColorBrush(GetBackdropFallbackColor());
    }

    private Color GetBackdropFallbackColor()
    {
        return IsLightShellTheme(currentSettings.Theme)
            ? Color.FromArgb(255, 232, 229, 223)
            : Color.FromArgb(255, 23, 22, 20);
    }

    private SystemBackdropTheme ResolveBackdropTheme()
    {
        return IsLightShellTheme(currentSettings.Theme)
            ? SystemBackdropTheme.Light
            : SystemBackdropTheme.Dark;
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (backdropConfiguration is not null)
        {
            backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }
    }

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateBackdropAppearance();
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (!shutdownLifetime.TryBeginShutdown())
        {
            return;
        }

        Diagnostics.StepStart("MainWindow.ShutdownCleanup");
        ShutdownCleanup.Run(
            new (string Name, Action Action)[]
            {
                ("Timers", () =>
                {
                    autoSaveTimer.Stop();
                    autoSaveTimer.Tick -= AutoSaveTimer_Tick;
                    nativeToastTimer.Stop();
                    nativeToastTimer.Tick -= NativeToastTimer_Tick;
                }),
                ("WindowEvents", () =>
                {
                    Root.Loaded -= Root_Loaded;
                    Activated -= Window_Activated;
                    Closed -= Window_Closed;
                    Root.ActualThemeChanged -= Root_ActualThemeChanged;
                    Root.SizeChanged -= Root_SizeChanged;
                    if (appWindow is not null)
                    {
                        appWindow.Closing -= OnAppWindowClosing;
                    }
                }),
                ("XamlRootEvents", () =>
                {
                    if (xamlRootHandlersAttached && Root.XamlRoot is { } xamlRoot)
                    {
                        xamlRoot.Changed -= XamlRoot_Changed;
                        xamlRootHandlersAttached = false;
                    }
                }),
                ("Backdrop", () =>
                {
                    try
                    {
                        desktopAcrylicController?.Dispose();
                    }
                    finally
                    {
                        desktopAcrylicController = null;
                        backdropConfiguration = null;
                    }
                }),
                ("WebViewEvents", () =>
                {
                    if (EditorWebView.CoreWebView2 is { } coreWebView)
                    {
                        coreWebView.WebMessageReceived -= OnWebMessageReceived;
                        coreWebView.NavigationCompleted -= OnEditorNavigationCompleted;
                        coreWebView.ProcessFailed -= OnEditorProcessFailed;
                        coreWebView.DownloadStarting -= OnWebViewDownloadStarting;
                    }
                }),
                ("WebView2", EditorWebView.Close),
                ("TransientSurfaces", () =>
                {
                    try
                    {
                        statisticsFlyout?.Hide();
                        startupSkeletonPulse?.Stop();
                    }
                    finally
                    {
                        statisticsFlyout = null;
                        startupSkeletonPulse = null;
                    }
                })
            },
            (name, ex) => Diagnostics.LogException("MainWindow.ShutdownCleanup/" + name, ex));
        Diagnostics.StepOk("MainWindow.ShutdownCleanup");
    }

    private void AttachResponsiveWindowHandlers()
    {
        if (xamlRootHandlersAttached || Root.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        xamlRoot.Changed += XamlRoot_Changed;
        xamlRootHandlersAttached = true;
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveShellGeometry();
        ApplyWindowChrome();
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        UpdateResponsiveWindowConstraints();
        UpdateResponsiveShellGeometry();
        ApplyWindowChrome();
    }

    private void UpdateResponsiveWindowConstraints()
    {
        if (appWindow?.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        try
        {
            var scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
            if (!double.IsFinite(scale) || scale <= 0)
            {
                scale = 1.0;
            }

            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var requestedMinimumWidth = Math.Max(1, (int)Math.Ceiling(MinWindowWidthLogical * scale));
            var requestedMinimumHeight = Math.Max(1, (int)Math.Ceiling(MinWindowHeightLogical * scale));

            var minimumWidth = Math.Min(requestedMinimumWidth, Math.Max(1, workArea.Width));
            var minimumHeight = Math.Min(requestedMinimumHeight, Math.Max(1, workArea.Height));

            presenter.PreferredMinimumWidth = minimumWidth;
            presenter.PreferredMinimumHeight = minimumHeight;

            var currentSize = appWindow.Size;
            if (currentSize.Width < minimumWidth || currentSize.Height < minimumHeight)
            {
                appWindow.Resize(new SizeInt32
                {
                    Width = Math.Max(currentSize.Width, minimumWidth),
                    Height = Math.Max(currentSize.Height, minimumHeight)
                });
            }
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("MainWindow.UpdateResponsiveWindowConstraints", ex);
        }
    }

    private void UpdateResponsiveShellGeometry()
    {
        var width = Root.ActualWidth;
        var height = Root.ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return;
        }

        static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);
        static double Progress(double value, double from, double to) => Math.Clamp((value - from) / (to - from), 0, 1);

        var shortWindowProgress = Progress(height, MinWindowHeightLogical, 720);
        var bandTop = height >= 720 ? 57 : Lerp(44, 57, shortWindowProgress);
        var bandGap = height >= 720 ? 24 : Lerp(18, 24, shortWindowProgress);
        var paperTop = bandTop + CommandBand.Height + bandGap;
        var bottomClearance = height >= 860
            ? 89
            : Lerp(24, 89, Progress(height, MinWindowHeightLogical, 860));

        CommandBand.Margin = new Thickness(24, bandTop, 24, 0);
        var toastTop = paperTop + 10;
        NativeToastHost.Margin = new Thickness(24, toastTop, 24, 0);
        NativePaper.Margin = new Thickness(24, paperTop, 24, bottomClearance);

        var estimatedPaperHeight = Math.Max(0, height - paperTop - bottomClearance);
        var compactPaper = estimatedPaperHeight < 420;
        EditorWebView.Margin = compactPaper
            ? new Thickness(24, 72, 24, 56)
            : new Thickness(24, 96, 24, 72);
    }

    private void ApplyNativePaperWidth(int editorWidth)
    {
        NativePaper.MaxWidth = Math.Clamp(editorWidth + 112, 792, 1052);
    }

    private void StartupSkeletonLayer_Loaded(object sender, RoutedEventArgs e)
    {
        if (startupSkeletonPulse is not null)
        {
            return;
        }

        if (!AreSystemAnimationsEnabled())
        {
            StartupSkeletonLayer.Opacity = 0.14;
            return;
        }

        try
        {
            startupSkeletonPulse = StartupSkeletonLayer.Resources["StartupSkeletonPulse"] as Storyboard;
            if (startupSkeletonPulse is null)
            {
                return;
            }

            foreach (var animation in startupSkeletonPulse.Children)
            {
                Storyboard.SetTarget(animation, StartupSkeletonLayer);
            }

            startupSkeletonPulse.Begin();
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("StartupSkeleton.Begin", ex);
        }
    }


    private static bool AreSystemAnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private void ShowEditorLoadingError(string message)
    {
        try
        {
            startupSkeletonPulse?.Stop();
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("StartupSkeleton.Stop", ex);
        }

        StartupSkeletonLayer.Visibility = Visibility.Collapsed;
        EditorLoadingText.Text = message;
        EditorLoadingText.Visibility = Visibility.Visible;
    }

    private void ApplyShellTheme()
    {
        var shellTheme = ThemeToElementTheme(currentSettings.Theme);
        Root.RequestedTheme = shellTheme;
        CommandBand.RequestedTheme = shellTheme;
        NativePaper.RequestedTheme = shellTheme;
        currentTheme = shellTheme;
        ApplyNativePaperWidth(currentSettings.EditorWidth);
        UpdateBackdropAppearance();
        ApplyWindowChrome();
        UpdateThemeMenuChecks();
    }

    private void UpdateThemeMenuChecks()
    {
        var light = IsLightShellTheme(currentSettings.Theme);
        ThemeLightMenuItem.IsChecked = light;
        ThemeDarkMenuItem.IsChecked = !light;
    }


    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        await RunUiEventAsync("RootLoaded", async () =>
        {
            Diagnostics.StepStart("MainWindow.RootLoaded");
            try
            {
                Root.Loaded -= Root_Loaded;
                AttachResponsiveWindowHandlers();
                UpdateResponsiveWindowConstraints();
                UpdateResponsiveShellGeometry();
                await Task.Delay(150, shutdownLifetime.Token);
                await InitializeEditorWithCrashReportAsync();
                Diagnostics.StepOk("MainWindow.RootLoaded");
            }
            catch (OperationCanceledException) when (shutdownLifetime.Token.IsCancellationRequested)
            {
                Diagnostics.Info("MainWindow.RootLoaded", "Cancelled during shutdown.");
            }
            catch (Exception ex)
            {
                Diagnostics.StepFailed("MainWindow.RootLoaded", ex);
                throw;
            }
        });
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!closeAllowed)
        {
            args.Cancel = true;
        }

        await RunUiEventAsync("AppWindowClosing", () => HandleAppWindowClosingAsync(args));
    }

    private async Task HandleAppWindowClosingAsync(AppWindowClosingEventArgs args)
    {
        Diagnostics.StepStart("MainWindow.Closing");
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
            Diagnostics.StepOk("MainWindow.Closing");
            return;
        }

        await RefreshDirtyStateFromEditorAsync();

        if (!isDirty)
        {
            Diagnostics.Info("MainWindow.Closing", "Clean document; closing without prompt.");
            closeAllowed = true;
            Close();
            return;
        }

        Diagnostics.Info("MainWindow.Closing", "Dirty document; awaiting save/discard decision.");
        if (await ConfirmDiscardIfNeededAsync())
        {
            closeAllowed = true;
            Close();
        }
    }

    private async void AutoSaveTimer_Tick(object? sender, object e)
    {
        await RunUiEventAsync("AutoSaveTimer", async () =>
        {
            if (!currentSettings.AutoSaveEnabled || !isDirty || string.IsNullOrWhiteSpace(currentFilePath))
            {
                return;
            }

            await SaveDocumentAsync(await GetMarkdownFromEditorAsync(), forceSaveAs: false, silent: true);
        });
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
            ShowEditorLoadingError("WebView2 konnte nicht gestartet werden.");
            await ShowMessageAsync("Startfehler", BuildStartupErrorMessage(ex));
        }
    }

    private async Task InitializeEditorAsync()
    {
        if (!File.Exists(editorPath))
        {
            throw new FileNotFoundException("Die Editor-Datei wurde nicht gefunden.", editorPath);
        }

        Diagnostics.StepStart("WebView2.EnsureCoreWebView2Async");
        try
        {
            await EditorWebView.EnsureCoreWebView2Async();
            Diagnostics.StepOk("WebView2.EnsureCoreWebView2Async");
        }
        catch (Exception ex)
        {
            Diagnostics.StepFailed("WebView2.EnsureCoreWebView2Async", ex);
            throw;
        }

        if (EditorWebView.CoreWebView2 is null)
        {
            var ex = new InvalidOperationException("WebView2 wurde initialisiert, aber CoreWebView2 ist weiterhin null.");
            Diagnostics.LogException("WebView2.CoreWebView2/null", ex);
            throw ex;
        }

        try
        {
            Diagnostics.Info("WebView2.Runtime", EditorWebView.CoreWebView2.Environment.BrowserVersionString);
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("WebView2.Runtime/query", ex);
        }

        EditorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        EditorWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if DEBUG
        EditorWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
        EditorWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
        EditorWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        EditorWebView.CoreWebView2.NavigationCompleted += OnEditorNavigationCompleted;
        EditorWebView.CoreWebView2.ProcessFailed += OnEditorProcessFailed;
        EditorWebView.CoreWebView2.DownloadStarting += OnWebViewDownloadStarting;

        EditorWebView.CoreWebView2.Navigate(new Uri(editorPath).AbsoluteUri);
    }

    private async void OnEditorNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        await RunUiEventAsync("WebView2.NavigationCompleted", async () =>
        {
            Diagnostics.Info("WebView2.NavigationCompleted", $"Success={args.IsSuccess}; Status={args.WebErrorStatus}");
            if (!args.IsSuccess)
            {
                var message = $"WebView2 konnte den Editor nicht laden. Status: {args.WebErrorStatus}. Pfad: {editorPath}";
                ShowEditorLoadingError(message);
                WriteStartupLog(new InvalidOperationException(message));
                await ShowMessageAsync("Editor konnte nicht geladen werden", message);
                return;
            }

            _ = RunUiEventAsync(
                "WebView2.BridgeStartup",
                () => WaitForEditorBridgeAndCompleteStartupAsync("navigation-completed"));
        });
    }

    private void OnWebViewDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
    {
        args.Handled = true;
    }

    private async void OnEditorProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        await RunUiEventAsync("WebView2.ProcessFailed", async () =>
        {
            Diagnostics.Warning("WebView2.ProcessFailed", args.ProcessFailedKind.ToString());
            var message = $"WebView2-Prozessfehler: {args.ProcessFailedKind}";
            ShowEditorLoadingError(message);
            WriteStartupLog(new InvalidOperationException(message));
            await ShowMessageAsync("WebView2-Fehler", message);
        });
    }

    private async Task WaitForEditorBridgeAndCompleteStartupAsync(string source)
    {
        if (EditorWebView.CoreWebView2 is null || initialDocumentLoaded || startupCompleting)
        {
            return;
        }

        AppendStartupTrace($"Waiting for editor bridge after {source}.");

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && !shutdownLifetime.Token.IsCancellationRequested)
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

            try
            {
                await Task.Delay(200, shutdownLifetime.Token);
            }
            catch (OperationCanceledException) when (shutdownLifetime.Token.IsCancellationRequested)
            {
                return;
            }
        }

        if (shutdownLifetime.Token.IsCancellationRequested)
        {
            return;
        }

        var message = "Der Editor wurde geladen, aber die JavaScript-Bridge wurde nicht rechtzeitig bereit. Der Start wurde abgebrochen statt endlos zu laden.";
        ShowEditorLoadingError(message);
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
            await SendSettingsAsync();
            await LoadInitialDocumentAsync();
            if (currentFilePath is null)
            {
                await FocusInitialHeadingAsync();
            }
            initialDocumentLoaded = true;
            startupSkeletonPulse?.Stop();
            StartupSkeletonLayer.Visibility = Visibility.Collapsed;
            EditorLoadingOverlay.Visibility = Visibility.Collapsed;
            AppendStartupTrace("Editor startup completed.");
        }
        catch (Exception ex)
        {
            editorReady = false;
            var message = "Dokumentvorbereitung fehlgeschlagen: " + ex.Message;
            ShowEditorLoadingError(message);
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
        await RunUiEventAsync("WebView2.WebMessageReceived", () => HandleWebMessageAsync(args));
    }

    private async Task HandleWebMessageAsync(CoreWebView2WebMessageReceivedEventArgs args)
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
                case "presentationState":
                    ApplyPresentationState(root);
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
                case "openSettings":
                    await ShowSettingsAsync();
                    break;
                case "nativeNotify":
                    ShowNativeToast(ReadString(root, "title") ?? string.Empty, ReadString(root, "message") ?? string.Empty);
                    break;
                case "showAbout":
                    await ShowAboutAsync();
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
        documentSession.ReplaceDocument();
        await SetMarkdownAsync(CreateNewDocumentMarkdown(), markClean: true, focusHeading: true, showStartPlaceholder: true);
    }

    private async Task NewDocumentAsync()
    {
        if (!await ConfirmDiscardIfNeededAsync())
        {
            return;
        }

        documentSession.ReplaceDocument();
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
        await RunUiEventAsync("EditorSurface.DragEnter", () => HandleEditorSurfaceDragEnterAsync(e));
    }

    private async Task HandleEditorSurfaceDragEnterAsync(DragEventArgs e)
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
        await RunUiEventAsync("EditorSurface.Drop", () => HandleEditorSurfaceDropAsync(e));
    }

    private async Task HandleEditorSurfaceDropAsync(DragEventArgs e)
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
        var previousMarkdown = currentMarkdown;
        var previousFilePath = currentFilePath;
        var previousLastSavedMarkdown = lastSavedMarkdown;
        var previousIsDirty = isDirty;
        documentSession.ReplaceDocument();

        try
        {
            await SetMarkdownAsync(markdown, markClean: false, focusWritingArea: true);
        }
        catch
        {
            currentMarkdown = previousMarkdown;
            currentFilePath = previousFilePath;
            lastSavedMarkdown = previousLastSavedMarkdown;
            isDirty = previousIsDirty;
            UpdateDocumentStats(currentMarkdown);
            UpdateTitle();
            UpdateStatus();
            throw;
        }

        currentMarkdown = markdown;
        currentFilePath = path;
        lastSavedMarkdown = markdown;
        isDirty = false;
        UpdateTitle();
        UpdateStatus();

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
        var markdownToSave = NormalizeLineEndings(markdown);
        if (!string.Equals(currentMarkdown, markdownToSave, StringComparison.Ordinal))
        {
            currentMarkdown = markdownToSave;
            documentSession.ContentChanged();
            UpdateDocumentStats(currentMarkdown);
            isDirty = !string.Equals(
                NormalizeLineEndings(currentMarkdown),
                NormalizeLineEndings(lastSavedMarkdown),
                StringComparison.Ordinal);
        }

        var operation = documentSession.BeginSave(markdownToSave, targetPath);
        var writeGateHeld = false;
        var operationCompleted = false;
        string? savedPath = null;
        UpdateTitle();
        UpdateStatus();

        try
        {
            if (forceSaveAs || string.IsNullOrWhiteSpace(targetPath))
            {
                var picker = new FileSavePicker
                {
                    SuggestedFileName = Path.GetFileNameWithoutExtension(BuildSuggestedMarkdownFileName(markdownToSave, targetPath))
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
                operation = operation.WithTargetPath(targetPath);
            }

            await saveWriteGate.WaitAsync();
            writeGateHeld = true;
            if (!documentSession.ShouldWrite(operation) || string.IsNullOrWhiteSpace(operation.TargetPath))
            {
                return;
            }

            await File.WriteAllTextAsync(operation.TargetPath, operation.MarkdownSnapshot, Utf8NoBom);
            var completion = documentSession.CompleteSave(operation);
            operationCompleted = true;
            var state = documentSession.ApplyCompletion(
                completion,
                currentMarkdown,
                currentFilePath,
                lastSavedMarkdown);
            currentMarkdown = state.CurrentMarkdown;
            currentFilePath = state.CurrentFilePath;
            lastSavedMarkdown = state.LastSavedMarkdown;
            isDirty = state.IsDirty;

            if (completion.ShouldApply && completion.TargetPath is not null)
            {
                savedPath = completion.TargetPath;
                AddRecentFile(savedPath);
            }
        }
        finally
        {
            if (!operationCompleted)
            {
                documentSession.CancelSave(operation);
            }

            if (writeGateHeld)
            {
                saveWriteGate.Release();
            }

            UpdateTitle();
            UpdateStatus();
        }

        if (!silent && savedPath is not null)
        {
            await NotifyAsync("Gespeichert", Path.GetFileName(savedPath));
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

        if (markClean)
        {
            var editorCanonicalMarkdown = await GetMarkdownFromEditorAsync();
            var cleanTransfer = MarkdownTransferPolicy.CompleteCleanTransfer(markdown, editorCanonicalMarkdown);
            currentMarkdown = cleanTransfer.CurrentMarkdown;
            lastSavedMarkdown = cleanTransfer.LastSavedMarkdown;
            isDirty = cleanTransfer.IsDirty;
            UpdateTitle();
            UpdateStatus();
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
            if (!string.Equals(currentMarkdown, markdown, StringComparison.Ordinal))
            {
                documentSession.ContentChanged();
            }

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
        if (!string.Equals(currentMarkdown, markdown, StringComparison.Ordinal))
        {
            documentSession.ContentChanged();
        }

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
        UpdatePaperMetadata();
    }

    private void ApplyPresentationState(JsonElement state)
    {
        currentWordCount = ReadInt(state, "words") ?? currentWordCount;
        currentCharacterCount = ReadInt(state, "chars") ?? currentCharacterCount;
        currentReadingMinutes = ReadInt(state, "readMinutes") ?? currentReadingMinutes;
        currentHeadingCount = ReadInt(state, "headings") ?? currentHeadingCount;
        hasSelectionStats = ReadBool(state, "hasSelection") ?? false;
        currentSelectionWordCount = hasSelectionStats ? ReadInt(state, "selectionWords") ?? 0 : 0;
        currentSelectionCharacterCount = hasSelectionStats ? ReadInt(state, "selectionChars") ?? 0 : 0;
        UpdatePaperMetadata();
    }

    private void UpdatePaperMetadata()
    {
        FooterWordCountText.Text = currentWordCount == 1 ? "1 Wort" : $"{currentWordCount} Wörter";

        if (hasSelectionStats)
        {
            var selectionWords = currentSelectionWordCount == 1 ? "1 Wort" : $"{currentSelectionWordCount} Wörter";
            var selectionCharacters = currentSelectionCharacterCount == 1 ? "1 Zeichen" : $"{currentSelectionCharacterCount} Zeichen";
            SelectionStatusText.Text = $"Auswahl: {selectionWords} · {selectionCharacters}";
            SelectionStatusText.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionStatusText.Text = string.Empty;
            SelectionStatusText.Visibility = Visibility.Collapsed;
        }
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

    private Task PrintPdfAsync()
    {
        if (EditorWebView.CoreWebView2 is null)
        {
            return Task.CompletedTask;
        }

        EditorWebView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        return Task.CompletedTask;
    }

    private async Task ShowSourceAsync(string markdown)
    {
        var textBox = new TextBox
        {
            Text = markdown,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            MinHeight = 220,
            Width = GetDialogContentWidth(720),
            MaxHeight = GetDialogContentHeight(520),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(textBox, "Markdown-Quelle");

        var dialog = CreateDialog("Markdown-Quelle bearbeiten", textBox);
        dialog.PrimaryButtonText = "Anwenden";
        dialog.CloseButtonText = "Schließen";
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
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

        // WINDOWS V2: RECENT FILES SURFACE
        // Keep the existing recent-file behavior, but present filename and location as
        // separate hierarchy levels inside the same restrained surface language as the shell.
        var lightDialog = IsLightShellTheme(currentSettings.Theme);
        var textBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 35, 37, 39)
            : Color.FromArgb(255, 238, 241, 243));
        var mutedBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 105, 110, 114)
            : Color.FromArgb(255, 162, 168, 174));
        var controlBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 255, 255, 255)
            : Color.FromArgb(255, 27, 29, 32));
        var borderBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 222, 226, 229)
            : Color.FromArgb(255, 52, 56, 59));
        var selectionBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 231, 239, 246)
            : Color.FromArgb(255, 30, 52, 69));
        var hoverBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 243, 246, 248)
            : Color.FromArgb(255, 37, 41, 45));
        var pressedBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 235, 240, 244)
            : Color.FromArgb(255, 43, 48, 53));
        var accentBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 79, 132, 179)
            : Color.FromArgb(255, 114, 168, 209));
        var accentHoverBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 69, 119, 162)
            : Color.FromArgb(255, 132, 181, 218));
        var accentPressedBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 58, 102, 140)
            : Color.FromArgb(255, 94, 147, 188));
        var separatorBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 235, 238, 240)
            : Color.FromArgb(255, 45, 49, 53));
        var scrollThumbBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(120, 112, 119, 124)
            : Color.FromArgb(135, 136, 143, 149));
        var scrollThumbHoverBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(175, 96, 104, 110)
            : Color.FromArgb(185, 164, 171, 177));
        var onAccentTextBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        var transparentBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

        var dialogWidth = GetDialogContentWidth(650);
        var dialogHeight = GetDialogContentHeight(430);
        var dialogHostBrush = controlBrush;

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = transparentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 4, 16, 4),
            IsItemClickEnabled = false,
            UseSystemFocusVisuals = true
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Zuletzt verwendete Dokumente");
        list.Resources["ListViewItemBackground"] = transparentBrush;
        list.Resources["ListViewItemBackgroundPointerOver"] = hoverBrush;
        list.Resources["ListViewItemBackgroundPressed"] = pressedBrush;
        list.Resources["ListViewItemBackgroundSelected"] = selectionBrush;
        list.Resources["ListViewItemBackgroundSelectedPointerOver"] = selectionBrush;
        list.Resources["ListViewItemBackgroundSelectedPressed"] = selectionBrush;
        list.Resources["ScrollBarThumbBackground"] = scrollThumbBrush;
        list.Resources["ScrollBarThumbBackgroundPointerOver"] = scrollThumbHoverBrush;
        list.Resources["ScrollBarThumbBackgroundPressed"] = scrollThumbHoverBrush;
        list.Resources["ScrollBarPanningThumbBackground"] = scrollThumbBrush;
        list.Resources["ScrollBarMinWidth"] = 4.0;
        list.Resources["ScrollBarMinHeight"] = 4.0;

        foreach (var path in recentFiles)
        {
            var recent = new RecentFileItem(path, Path.GetFileName(path));
            list.Items.Add(CreateRecentFileListItem(
                recent,
                textBrush,
                mutedBrush,
                accentBrush,
                separatorBrush,
                transparentBrush));
        }
        list.SelectedIndex = 0;

        var headerIcon = new FontIcon
        {
            Glyph = "\uE72B",
            FontSize = 18,
            Foreground = accentBrush,
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerCopy = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerCopy.Children.Add(new TextBlock
        {
            Text = "Zuletzt verwendet",
            Foreground = textBrush,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Variable Display"),
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        headerCopy.Children.Add(new TextBlock
        {
            Text = "Wähle ein Dokument aus, um dort weiterzuarbeiten.",
            Foreground = mutedBrush,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Variable Text"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Thickness(20, 16, 20, 12)
        };
        header.Children.Add(headerIcon);
        header.Children.Add(headerCopy);

        var recentOpenButton = new Button
        {
            Content = "Öffnen",
            MinWidth = 118,
            MinHeight = 36,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = accentBrush,
            Foreground = onAccentTextBrush,
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        recentOpenButton.Resources["ButtonBackgroundPointerOver"] = accentHoverBrush;
        recentOpenButton.Resources["ButtonBackgroundPressed"] = accentPressedBrush;
        recentOpenButton.Resources["ButtonForegroundPointerOver"] = onAccentTextBrush;
        recentOpenButton.Resources["ButtonForegroundPressed"] = onAccentTextBrush;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(recentOpenButton, "Ausgewählte Datei öffnen");

        var recentCancelButton = new Button
        {
            Content = "Abbrechen",
            MinWidth = 112,
            MinHeight = 36,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = transparentBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        recentCancelButton.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
        recentCancelButton.Resources["ButtonBackgroundPressed"] = pressedBrush;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(recentCancelButton, "Zuletzt verwendet schließen");

        var recentRemoveButton = new Button
        {
            Content = "Entfernen",
            MinWidth = 104,
            MinHeight = 36,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = transparentBrush,
            Foreground = mutedBrush,
            BorderBrush = transparentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        recentRemoveButton.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
        recentRemoveButton.Resources["ButtonBackgroundPressed"] = pressedBrush;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(recentRemoveButton, "Ausgewählten Eintrag entfernen");

        var rightActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        rightActions.Children.Add(recentCancelButton);
        rightActions.Children.Add(recentOpenButton);

        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.Children.Add(recentRemoveButton);
        Grid.SetColumn(rightActions, 1);
        footerGrid.Children.Add(rightActions);

        var footer = new Border
        {
            Padding = new Thickness(16, 12, 16, 14),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footerGrid
        };

        var recentShell = new Grid
        {
            Width = dialogWidth,
            Height = dialogHeight,
            MaxHeight = dialogHeight,
            Background = transparentBrush
        };
        recentShell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        recentShell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        recentShell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        recentShell.Children.Add(header);
        Grid.SetRow(list, 1);
        recentShell.Children.Add(list);
        Grid.SetRow(footer, 2);
        recentShell.Children.Add(footer);

        var requestedResult = ContentDialogResult.None;
        var dialog = CreateDialog(string.Empty, recentShell);
        dialog.Title = null;
        dialog.Background = dialogHostBrush;
        dialog.BorderBrush = borderBrush;
        dialog.BorderThickness = new Thickness(1);
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth + 56;
        dialog.Resources["ContentDialogBackground"] = dialogHostBrush;
        dialog.Resources["ContentDialogBorderBrush"] = borderBrush;

        recentOpenButton.Click += (_, _) =>
        {
            requestedResult = ContentDialogResult.Primary;
            dialog.Hide();
        };
        recentRemoveButton.Click += (_, _) =>
        {
            requestedResult = ContentDialogResult.Secondary;
            dialog.Hide();
        };
        recentCancelButton.Click += (_, _) => dialog.Hide();

        await ShowDialogAsync(dialog);
        if (list.SelectedItem is not ListViewItem selectedItem ||
            selectedItem.DataContext is not RecentFileItem selected)
        {
            return;
        }

        if (requestedResult == ContentDialogResult.Secondary)
        {
            recentFiles.Remove(selected.Path);
            SaveRecentFiles();
            await NotifyAsync("Eintrag entfernt", selected.DisplayText);
            return;
        }

        if (requestedResult != ContentDialogResult.Primary)
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

    private ListViewItem CreateRecentFileListItem(
        RecentFileItem recent,
        Brush textBrush,
        Brush mutedBrush,
        Brush accentBrush,
        Brush separatorBrush,
        Brush transparentBrush)
    {
        var fileName = string.IsNullOrWhiteSpace(recent.Name) ? recent.Path : recent.Name;

        var fileIcon = new FontIcon
        {
            Glyph = "\uE8A5",
            FontSize = 15,
            Foreground = accentBrush,
            Width = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var copy = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        copy.Children.Add(new TextBlock
        {
            Text = fileName,
            Foreground = textBrush,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Variable Text"),
            FontSize = 13.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        });
        copy.Children.Add(new TextBlock
        {
            Text = recent.Path,
            Foreground = mutedBrush,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Variable Text"),
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        });

        var row = new Grid
        {
            ColumnSpacing = 10
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(fileIcon);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);

        var item = new ListViewItem
        {
            Content = row,
            DataContext = recent,
            Margin = new Thickness(0),
            Padding = new Thickness(10, 8, 12, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = transparentBrush,
            BorderBrush = separatorBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(0),
            UseSystemFocusVisuals = true
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, $"{fileName}, {recent.Path}");
        return item;
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
        await RefreshDirtyStateFromEditorAsync();

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

        var result = await ShowDialogAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            await SaveDocumentAsync(currentMarkdown, forceSaveAs: false);
            return !isDirty;
        }

        return result == ContentDialogResult.Secondary;
    }

    private async Task SendSettingsAsync()
    {
        ApplyNativePaperWidth(currentSettings.EditorWidth);

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
        ApplyNativePaperWidth(editorWidth);

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
        // WINDOWS V2 PHASE 6: NAVIGATED SETTINGS
        var originalTheme = NormalizeTheme(currentSettings.Theme);
        var originalEditorWidth = currentSettings.EditorWidth;
        var originalFontSize = currentSettings.FontSize;
        var originalLineHeight = currentSettings.LineHeight;
        var previewSession = new SettingsPreviewSession<SettingsAppearanceState>(new SettingsAppearanceState(
            originalTheme,
            originalEditorWidth,
            originalFontSize,
            originalLineHeight));
        var lightDialog = IsLightShellTheme(currentSettings.Theme);

        var textBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 35, 37, 39)
            : Color.FromArgb(255, 238, 241, 243));
        var mutedBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 105, 110, 114)
            : Color.FromArgb(255, 162, 168, 174));
        var sidebarBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 250, 250, 249)
            : Color.FromArgb(255, 25, 27, 30));
        var controlBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 255, 255, 255)
            : Color.FromArgb(255, 27, 29, 32));
        var borderBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 222, 226, 229)
            : Color.FromArgb(255, 52, 56, 59));
        var selectionBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 231, 239, 246)
            : Color.FromArgb(255, 30, 52, 69));
        var hoverBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 243, 246, 248)
            : Color.FromArgb(255, 37, 41, 45));
        var accentBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 79, 132, 179)
            : Color.FromArgb(255, 114, 168, 209));
        var accentHoverBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 69, 119, 162)
            : Color.FromArgb(255, 132, 181, 218));
        var accentPressedBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 58, 102, 140)
            : Color.FromArgb(255, 94, 147, 188));
        var onAccentTextBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        var transparentBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

        // WINDOWS V2 SECONDARY SURFACES: one dialog surface in both themes.
        // Keep the sidebar/content hierarchy, but avoid a second framed card inside
        // the ContentDialog so Settings follows the flatter Recent Files language.
        var dialogHostBrush = controlBrush;
        var settingsInnerSurfaceBrush = transparentBrush;
        var settingsInnerBorderBrush = transparentBrush;

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
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 150,
            Background = controlBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(8),
            IsEnabled = currentSettings.AutoSaveEnabled
        };
        var widthBox = new NumberBox
        {
            Value = currentSettings.EditorWidth,
            Minimum = 680,
            Maximum = 940,
            SmallChange = 20,
            LargeChange = 80,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 150,
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
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 150,
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
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 150,
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
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 150,
            Background = controlBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(8)
        };

        autoSaveSwitch.Toggled += (_, _) => autoSaveBox.IsEnabled = autoSaveSwitch.IsOn;

        var appearancePage = new StackPanel { Spacing = 0 };
        appearancePage.Children.Add(CreateSettingsGroupLabel("SCHREIBBILD", mutedBrush, borderBrush));
        appearancePage.Children.Add(CreateSettingsValueRow(
            "Editorbreite",
            "Breite der Schreibfläche in Pixeln.",
            widthBox,
            textBrush,
            mutedBrush,
            borderBrush));
        appearancePage.Children.Add(CreateSettingsValueRow(
            "Schriftgröße",
            "Größe des Editor-Texts in Pixeln.",
            fontBox,
            textBrush,
            mutedBrush,
            borderBrush));
        appearancePage.Children.Add(CreateSettingsValueRow(
            "Zeilenhöhe",
            "Abstand zwischen den Textzeilen.",
            lineHeightBox,
            textBrush,
            mutedBrush,
            borderBrush,
            showBottomDivider: false));

        var editorPage = new StackPanel { Spacing = 0 };
        editorPage.Children.Add(CreateSettingsGroupLabel("EDITOR", mutedBrush, borderBrush));
        editorPage.Children.Add(CreateSettingsToggleRowFlat(
            "Rechtschreibprüfung",
            "Markiert mögliche Schreibfehler direkt im Editor.",
            spellcheckSwitch,
            textBrush,
            mutedBrush,
            borderBrush));
        editorPage.Children.Add(CreateSettingsValueRow(
            "Schreibziel",
            "Ziel in Wörtern; 0 deaktiviert das Schreibziel.",
            goalBox,
            textBrush,
            mutedBrush,
            borderBrush,
            showBottomDivider: false));

        var behaviorPage = new StackPanel { Spacing = 0 };
        behaviorPage.Children.Add(CreateSettingsGroupLabel("VERHALTEN", mutedBrush, borderBrush));
        behaviorPage.Children.Add(CreateSettingsToggleRowFlat(
            "Fokusmodus beim Start",
            "Öffnet neue Sitzungen direkt in der reduzierten Schreibansicht.",
            focusSwitch,
            textBrush,
            mutedBrush,
            borderBrush,
            showBottomDivider: false));

        var filesPage = new StackPanel { Spacing = 0 };
        filesPage.Children.Add(CreateSettingsGroupLabel("DATEIEN", mutedBrush, borderBrush));
        filesPage.Children.Add(CreateSettingsToggleRowFlat(
            "Auto-Save",
            "Speichert bereits angelegte Dateien automatisch.",
            autoSaveSwitch,
            textBrush,
            mutedBrush,
            borderBrush));
        filesPage.Children.Add(CreateSettingsValueRow(
            "Intervall",
            "Sekunden zwischen automatischen Speicherungen.",
            autoSaveBox,
            textBrush,
            mutedBrush,
            borderBrush,
            leftIndent: 18));
        filesPage.Children.Add(CreateSettingsToggleRowFlat(
            "Letztes Dokument beim Start öffnen",
            "Öffnet die zuletzt aktive Datei erneut, wenn sie noch vorhanden ist.",
            reopenLastDocumentSwitch,
            textBrush,
            mutedBrush,
            borderBrush,
            showBottomDivider: false));

        var settingsDialogWidth = GetDialogContentWidth(800);
        var settingsDialogHeight = GetDialogContentHeight(500);
        var compactSettingsNavigation = settingsDialogWidth < 700;

        var settingsShell = new Grid
        {
            Width = settingsDialogWidth,
            Height = settingsDialogHeight,
            MaxHeight = settingsDialogHeight,
            Background = settingsInnerSurfaceBrush
        };
        settingsShell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        settingsShell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        settingsShell.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(compactSettingsNavigation ? 68 : 184)
        });
        settingsShell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new Border
        {
            Background = sidebarBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(compactSettingsNavigation ? 8 : 12, 22, compactSettingsNavigation ? 8 : 12, 18)
        };
        var sidebarStack = new StackPanel { Spacing = 6 };
        if (!compactSettingsNavigation)
        {
            sidebarStack.Children.Add(new TextBlock
            {
                Text = "Einstellungen",
                Foreground = textBrush,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 8, 18)
            });
        }

        var appearanceNav = CreateSettingsNavigationButton("\uE706", "Darstellung", compactSettingsNavigation, textBrush, hoverBrush);
        var editorNav = CreateSettingsNavigationButton("\uE70F", "Editor", compactSettingsNavigation, textBrush, hoverBrush);
        var behaviorNav = CreateSettingsNavigationButton("\uE713", "Verhalten", compactSettingsNavigation, textBrush, hoverBrush);
        var filesNav = CreateSettingsNavigationButton("\uE8B7", "Dateien", compactSettingsNavigation, textBrush, hoverBrush);
        var navigationButtons = new[] { appearanceNav, editorNav, behaviorNav, filesNav };

        sidebarStack.Children.Add(appearanceNav);
        sidebarStack.Children.Add(editorNav);
        sidebarStack.Children.Add(behaviorNav);
        sidebarStack.Children.Add(filesNav);
        sidebar.Child = sidebarStack;
        settingsShell.Children.Add(sidebar);

        var pageTitle = new TextBlock
        {
            Foreground = textBrush,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var pageSubtitle = new TextBlock
        {
            Foreground = mutedBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var closeSettingsButton = new Button
        {
            Width = 30,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Foreground = mutedBrush,
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(closeSettingsButton, "Einstellungen schließen");
        closeSettingsButton.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
        closeSettingsButton.Resources["ButtonBackgroundPressed"] = selectionBrush;

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerCopy = new StackPanel();
        headerCopy.Children.Add(pageTitle);
        headerCopy.Children.Add(pageSubtitle);
        headerGrid.Children.Add(headerCopy);
        Grid.SetColumn(closeSettingsButton, 1);
        headerGrid.Children.Add(closeSettingsButton);

        var settingsContentHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        var settingsContentScroll = new ScrollViewer
        {
            Content = settingsContentHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var rightGrid = new Grid
        {
            Padding = new Thickness(compactSettingsNavigation ? 20 : 26, 22, compactSettingsNavigation ? 20 : 26, 14)
        };
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightGrid.Children.Add(headerGrid);
        Grid.SetRow(settingsContentScroll, 1);
        rightGrid.Children.Add(settingsContentScroll);
        Grid.SetColumn(rightGrid, 1);
        settingsShell.Children.Add(rightGrid);

        var settingsApplyButton = new Button
        {
            Content = "Übernehmen",
            Width = 156,
            MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = accentBrush,
            Foreground = onAccentTextBrush,
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7)
        };
        settingsApplyButton.Resources["ButtonBackgroundPointerOver"] = accentHoverBrush;
        settingsApplyButton.Resources["ButtonBackgroundPressed"] = accentPressedBrush;
        settingsApplyButton.Resources["ButtonForegroundPointerOver"] = onAccentTextBrush;
        settingsApplyButton.Resources["ButtonForegroundPressed"] = onAccentTextBrush;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(settingsApplyButton, "Einstellungen übernehmen");

        var settingsCancelButton = new Button
        {
            Content = "Abbrechen",
            Width = 132,
            MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = controlBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7)
        };
        settingsCancelButton.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
        settingsCancelButton.Resources["ButtonBackgroundPressed"] = selectionBrush;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(settingsCancelButton, "Einstellungen abbrechen");

        var settingsActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        settingsActions.Children.Add(settingsCancelButton);
        settingsActions.Children.Add(settingsApplyButton);

        var settingsFooter = new Grid
        {
            Padding = new Thickness(18, 12, 18, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        settingsFooter.Children.Add(settingsActions);

        var settingsFooterBorder = new Border
        {
            Background = transparentBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = settingsFooter
        };
        Grid.SetRow(settingsFooterBorder, 1);
        Grid.SetColumnSpan(settingsFooterBorder, 2);
        settingsShell.Children.Add(settingsFooterBorder);

        void SelectSettingsPage(Button selectedButton, string title, string subtitle, UIElement content)
        {
            foreach (var button in navigationButtons)
            {
                var selected = ReferenceEquals(button, selectedButton);
                button.Background = selected
                    ? selectionBrush
                    : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                button.Foreground = selected ? accentBrush : textBrush;
                button.FontWeight = selected
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal;
            }

            pageTitle.Text = title;
            pageSubtitle.Text = subtitle;
            settingsContentHost.Content = content;
        }

        appearanceNav.Click += (_, _) => SelectSettingsPage(
            appearanceNav,
            "Darstellung",
            "Anpassungen für Oberfläche und Schreibbild.",
            appearancePage);
        editorNav.Click += (_, _) => SelectSettingsPage(
            editorNav,
            "Editor",
            "Schreibunterstützung und Zielvorgaben.",
            editorPage);
        behaviorNav.Click += (_, _) => SelectSettingsPage(
            behaviorNav,
            "Verhalten",
            "Steuert den Start und die Arbeitsweise der App.",
            behaviorPage);
        filesNav.Click += (_, _) => SelectSettingsPage(
            filesNav,
            "Dateien",
            "Automatische Speicherung und Sitzungswiederherstellung.",
            filesPage);

        SelectSettingsPage(
            appearanceNav,
            "Darstellung",
            "Anpassungen für Oberfläche und Schreibbild.",
            appearancePage);

        var settingsSurface = new Border
        {
            Background = settingsInnerSurfaceBrush,
            BorderBrush = settingsInnerBorderBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Child = settingsShell
        };

        var settingsAccepted = false;
        var dialog = CreateDialog(string.Empty, settingsSurface);
        dialog.Title = null;
        dialog.Background = dialogHostBrush;
        dialog.BorderBrush = borderBrush;
        dialog.BorderThickness = new Thickness(1);
        dialog.Resources["ContentDialogMaxWidth"] = settingsDialogWidth + 56;
        dialog.Resources["ContentDialogBackground"] = dialogHostBrush;
        dialog.Resources["ContentDialogBorderBrush"] = borderBrush;

        closeSettingsButton.Click += (_, _) => dialog.Hide();
        settingsCancelButton.Click += (_, _) => dialog.Hide();
        settingsApplyButton.Click += (_, _) =>
        {
            settingsAccepted = true;
            dialog.Hide();
        };

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
            var previewTheme = NormalizeTheme(currentSettings.Theme);
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

        widthBox.ValueChanged += (_, _) => QueueSettingsPreview();
        fontBox.ValueChanged += (_, _) => QueueSettingsPreview();
        lineHeightBox.ValueChanged += (_, _) => QueueSettingsPreview();

        await ShowDialogAsync(dialog);
        await previewSession.CompleteAsync(
            settingsAccepted,
            latestSettingsPreviewTask,
            state => RestoreSettingsPreviewAsync(
                state.Theme,
                state.EditorWidth,
                state.FontSize,
                state.LineHeight),
            async () =>
            {
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
            });
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

    private static Button CreateSettingsNavigationButton(
        string glyph,
        string label,
        bool compact,
        Brush textBrush,
        Brush hoverBrush)
    {
        var content = new Grid { ColumnSpacing = compact ? 0 : 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        if (!compact)
        {
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(icon);

        if (!compact)
        {
            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelBlock, 1);
            content.Children.Add(labelBlock);
        }

        var button = new Button
        {
            Content = content,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Padding = compact ? new Thickness(10) : new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Foreground = textBrush
        };
        button.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        if (compact)
        {
            ToolTipService.SetToolTip(button, label);
        }

        return button;
    }

    private static Grid CreateSettingsGroupLabel(string label, Brush mutedBrush, Brush borderBrush)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = mutedBrush,
            FontSize = 10.5,
            CharacterSpacing = 90,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var divider = new Border
        {
            Height = 1,
            Background = borderBrush,
            Opacity = 0.72
        };
        Grid.SetRow(divider, 1);
        grid.Children.Add(divider);
        return grid;
    }

    private static Border CreateSettingsValueRow(
        string title,
        string description,
        FrameworkElement control,
        Brush textBrush,
        Brush mutedBrush,
        Brush borderBrush,
        double leftIndent = 0,
        bool showBottomDivider = true)
    {
        var grid = new Grid
        {
            ColumnSpacing = 24,
            Margin = new Thickness(leftIndent, 0, 0, 0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = textBrush,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        copy.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = mutedBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        });
        grid.Children.Add(copy);

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, title);

        return new Border
        {
            Padding = new Thickness(2, 13, 2, 13),
            BorderBrush = borderBrush,
            BorderThickness = showBottomDivider ? new Thickness(0, 0, 0, 1) : new Thickness(0),
            Child = grid
        };
    }

    private static Border CreateSettingsToggleRowFlat(
        string title,
        string description,
        ToggleSwitch toggle,
        Brush textBrush,
        Brush mutedBrush,
        Brush borderBrush,
        bool showBottomDivider = true)
    {
        return CreateSettingsValueRow(
            title,
            description,
            toggle,
            textBrush,
            mutedBrush,
            borderBrush,
            showBottomDivider: showBottomDivider);
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
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
        var accentBrush = new SolidColorBrush(Color.FromArgb(255, 79, 132, 179));
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
        await ShowDialogAsync(dialog);
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

    private Task NotifyAsync(string title, string message)
    {
        ShowNativeToast(title ?? string.Empty, message ?? string.Empty);
        return Task.CompletedTask;
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
            AppendStartupTrace($"ExecuteScript done: {operation} (result length={result?.Length ?? 0})");
            return result;
        }
        catch (TimeoutException ex)
        {
            var wrapped = new TimeoutException($"WebView2 Script-Operation '{operation}' hat nach {timeout.TotalSeconds:0.#} Sekunden nicht geantwortet.", ex);
            AppendStartupTrace(wrapped.Message);
            throw wrapped;
        }
    }

    private static void AppendStartupTrace(string message)
    {
        Diagnostics.Info("Startup", message);
    }

    private string BuildStartupStateMessage()
    {
        var rootLoaderPath = Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll");
        var runtimeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application");
        return string.Join(Environment.NewLine, new[]
        {
            "Diagnose:",
            "Editor: " + editorPath,
            "Ausgabeordner: " + AppContext.BaseDirectory,
            "Root-WebView2Loader.dll: " + (File.Exists(rootLoaderPath) ? "vorhanden" : "fehlt") + " (" + rootLoaderPath + ")",
            "WebView2 Runtime: " + (Directory.Exists(runtimeRoot) ? "gefunden" : "nicht gefunden") + " (" + runtimeRoot + ")",
            "Diagnoseprotokoll: %LOCALAPPDATA%\\Markdown Studio Pro\\Diagnostics"
        });
    }

    private double GetDialogContentWidth(double preferredWidth)
    {
        var viewportWidth = Root.XamlRoot?.Size.Width ?? Root.ActualWidth;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
        {
            viewportWidth = 820;
        }

        return Math.Min(preferredWidth, Math.Max(120, viewportWidth - 64));
    }

    private double GetDialogContentHeight(double preferredHeight)
    {
        var viewportHeight = Root.XamlRoot?.Size.Height ?? Root.ActualHeight;
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0)
        {
            viewportHeight = 720;
        }

        return Math.Min(preferredHeight, Math.Max(120, viewportHeight - 96));
    }

    private void ConfigureDialogBounds(ContentDialog dialog)
    {
        // Let the ContentDialog template own its popup size and placement.
        // Constraining the dialog control itself can break WinUI centering;
        // individual dialog contents are already bounded responsively.
        dialog.HorizontalAlignment = HorizontalAlignment.Center;
        dialog.VerticalAlignment = VerticalAlignment.Center;
        dialog.CornerRadius = new CornerRadius(14);
        dialog.UseSystemFocusVisuals = true;
    }

    private void ApplyEditorialDialogAppearance(ContentDialog dialog)
    {
        var lightDialog = IsLightShellTheme(currentSettings.Theme);
        var surfaceBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 254, 254, 254)
            : Color.FromArgb(255, 25, 27, 30));
        var borderBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 220, 224, 226)
            : Color.FromArgb(255, 52, 56, 59));
        var textBrush = new SolidColorBrush(lightDialog
            ? Color.FromArgb(255, 35, 37, 39)
            : Color.FromArgb(255, 238, 241, 243));

        dialog.Background = surfaceBrush;
        dialog.BorderBrush = borderBrush;
        dialog.BorderThickness = new Thickness(1);
        dialog.Foreground = textBrush;
        dialog.Resources["ContentDialogBackground"] = surfaceBrush;
        dialog.Resources["ContentDialogBorderBrush"] = borderBrush;
    }

    private ContentDialog CreateDialog(string title, object content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ThemeToElementTheme(currentSettings.Theme),
            Title = title,
            Content = content
        };
        ConfigureDialogBounds(dialog);
        ApplyEditorialDialogAppearance(dialog);
        return dialog;
    }

    private Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        return dialogQueue.RunAsync(async () => await dialog.ShowAsync());
    }

    private Task RunUiEventAsync(string context, Func<Task> action) =>
        uiErrorBoundary.RunAsync(context, action);

    private Task ReportUiErrorAsync(string context, Exception ex)
    {
        if (shutdownLifetime.Token.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        return ShowMessageAsync("Unerwarteter Fehler", $"{context}: {ex.Message}");
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var scrollViewer = new ScrollViewer
        {
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            MaxWidth = GetDialogContentWidth(620),
            MaxHeight = GetDialogContentHeight(520)
        };

        var dialog = CreateDialog(title, scrollViewer);
        dialog.CloseButtonText = "OK";
        await ShowDialogAsync(dialog);
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
        Diagnostics.LogException("Startup", ex);
    }

    private void LoadSettings()
    {
        var path = Path.Combine(AppDataDirectory, "settings.json");
        var hadStoredSettings = File.Exists(path);
        var loadedStoredSettings = false;

        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            if (hadStoredSettings)
            {
                currentSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path, Utf8NoBom), jsonOptions) ?? new AppSettings();
                loadedStoredSettings = true;
            }
            else
            {
                currentSettings = CreateFreshV2Settings();
            }
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("Settings.Load", ex);
            currentSettings = CreateFreshV2Settings();
        }

        if (hadStoredSettings && loadedStoredSettings && currentSettings.AppearanceVersion < V2AppearanceVersion)
        {
            TryMigrateV2Appearance(path);
        }

        NormalizeSettings();
    }

    private static AppSettings CreateFreshV2Settings()
    {
        return new AppSettings
        {
            Theme = V2DefaultTheme,
            EditorWidth = V2DefaultEditorWidth,
            FontSize = V2DefaultFontSize,
            LineHeight = V2DefaultLineHeight,
            AppearanceVersion = V2AppearanceVersion
        };
    }

    private bool TryMigrateV2Appearance(string settingsPath)
    {
        try
        {
            if (!TryEnsureAppearanceBackup())
            {
                Diagnostics.Warning("AppearanceMigration", "V2 appearance backup could not be created; stored settings were left unchanged.");
                return false;
            }

            var migrated = CloneSettings(currentSettings);
            migrated.Theme = V2DefaultTheme;
            migrated.EditorWidth = V2DefaultEditorWidth;
            migrated.FontSize = V2DefaultFontSize;
            migrated.LineHeight = V2DefaultLineHeight;
            migrated.AppearanceVersion = V2AppearanceVersion;

            var tempPath = settingsPath + ".v2-migration.tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(migrated, jsonOptions), Utf8NoBom);
            File.Move(tempPath, settingsPath, overwrite: true);
            currentSettings = migrated;
            Diagnostics.Info("AppearanceMigration", "Applied Windows V2 appearance defaults once; previous appearance values are backed up.");
            return true;
        }
        catch (Exception ex)
        {
            TryDeleteMigrationTemp(settingsPath);
            Diagnostics.LogException("AppearanceMigration", ex);
            return false;
        }
    }

    private bool TryEnsureAppearanceBackup()
    {
        var backupPath = Path.Combine(AppDataDirectory, AppearanceBackupFileName);
        if (File.Exists(backupPath))
        {
            try
            {
                var backup = JsonSerializer.Deserialize<AppearanceSettingsBackup>(File.ReadAllText(backupPath, Utf8NoBom), jsonOptions)
                    ?? throw new InvalidDataException("Appearance backup is empty.");
                if (backup.CapturedUtc == default)
                {
                    throw new InvalidDataException("Appearance backup is incomplete.");
                }
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("AppearanceMigration.BackupRead", ex);
                return false;
            }
        }

        try
        {
            var backup = new AppearanceSettingsBackup
            {
                Theme = currentSettings.Theme,
                EditorWidth = currentSettings.EditorWidth,
                FontSize = currentSettings.FontSize,
                LineHeight = currentSettings.LineHeight,
                CapturedUtc = DateTimeOffset.UtcNow
            };
            File.WriteAllText(backupPath, JsonSerializer.Serialize(backup, jsonOptions), Utf8NoBom);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("AppearanceMigration.BackupWrite", ex);
            return false;
        }
    }

    private AppSettings CloneSettings(AppSettings source)
    {
        var json = JsonSerializer.Serialize(source, jsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, jsonOptions) ?? new AppSettings();
    }

    private static void TryDeleteMigrationTemp(string settingsPath)
    {
        try
        {
            var tempPath = settingsPath + ".v2-migration.tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Cleanup must not hide the migration failure that was already reported.
        }
    }

    private void NormalizeSettings()
    {
        currentSettings.Theme = NormalizeTheme(currentSettings.Theme);
        currentSettings.AutoSaveIntervalSeconds = Math.Clamp(currentSettings.AutoSaveIntervalSeconds, 10, 600);
        currentSettings.EditorWidth = Math.Clamp(currentSettings.EditorWidth, 680, 940);
        currentSettings.FontSize = Math.Clamp(currentSettings.FontSize, 13, 22);
        currentSettings.LineHeight = Math.Clamp(currentSettings.LineHeight, 1.35, 2.0);
        currentSettings.WordGoal = Math.Clamp(currentSettings.WordGoal, 0, 100000);
        currentSettings.WindowWidth = Math.Clamp(currentSettings.WindowWidth, MinWindowWidthLogical, 2560);
        currentSettings.WindowHeight = Math.Clamp(currentSettings.WindowHeight, MinWindowHeightLogical, 1600);
        currentSettings.LastDocumentPath = NormalizeInitialPath(currentSettings.LastDocumentPath);
    }

    private static string NormalizeTheme(string? theme)
    {
        return theme?.Trim().ToLowerInvariant() switch
        {
            "light" or "clean" or "sepia" => "light",
            "dark" or "midnight" => "dark",
            _ => "light"
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

    private static bool? ReadBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
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

    private string ResolveCommandBandTitle()
    {
        if (!string.IsNullOrWhiteSpace(currentFilePath))
        {
            return Path.GetFileName(currentFilePath);
        }

        if (!editorReady && !string.IsNullOrWhiteSpace(initialFilePath))
        {
            return Path.GetFileName(initialFilePath);
        }

        var markdownTitle = TryExtractMarkdownTitle(currentMarkdown);
        if (!string.IsNullOrWhiteSpace(markdownTitle))
        {
            return markdownTitle;
        }

        return AppName;
    }

    private void UpdateTitle()
    {
        var displayTitle = ResolveDisplayTitle();
        Title = isDirty ? $"{displayTitle} * - {AppName}" : $"{displayTitle} - {AppName}";
        CommandBandTitleText.Text = ResolveCommandBandTitle();
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

        var saveStateBrush = new SolidColorBrush(isSaving || isDirty
            ? Color.FromArgb(255, 79, 132, 179)
            : Color.FromArgb(255, 160, 157, 150));
        SaveStateText.Foreground = saveStateBrush;
        PaperSaveStateIcon.Foreground = saveStateBrush;
        UpdatePaperMetadata();

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

    private void SaveStatusButton_Click(object sender, RoutedEventArgs e)
    {
        var stateText = isSaving ? "Speichert…" : isDirty ? "Ungespeichert" : "Gespeichert";
        var locationText = string.IsNullOrWhiteSpace(currentFilePath)
            ? "Noch nicht als lokale Datei gespeichert."
            : $"Lokale Datei: {Path.GetFileName(currentFilePath)}";
        var autoSaveText = currentSettings.AutoSaveEnabled
            ? $"Auto-Save: aktiv ({currentSettings.AutoSaveIntervalSeconds} s)"
            : "Auto-Save: inaktiv";

        var panel = new StackPanel { Spacing = 8, Width = 280 };
        panel.Children.Add(new TextBlock { Text = stateText, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = locationText, TextWrapping = TextWrapping.Wrap, Opacity = 0.78 });
        panel.Children.Add(new TextBlock { Text = autoSaveText, Opacity = 0.72 });

        var flyout = new Flyout { Content = panel };
        flyout.ShowAt(PaperSaveStatusButton);
    }

    private async void StatisticsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiEventAsync("Statistics", () => ShowStatisticsFlyoutAsync(StatisticsButton));
    }

    private async Task ShowStatisticsFlyoutAsync(FrameworkElement anchor)
    {
        var snapshot = await GetStatisticsSnapshotAsync();
        if (snapshot is null)
        {
            return;
        }

        currentWordCount = snapshot.Words;
        currentCharacterCount = snapshot.Chars;
        currentReadingMinutes = snapshot.ReadMinutes;
        currentHeadingCount = snapshot.Headings;
        hasSelectionStats = snapshot.HasSelection;
        currentSelectionWordCount = snapshot.SelectionWords;
        currentSelectionCharacterCount = snapshot.SelectionChars;
        UpdatePaperMetadata();

        var compactStatisticsSurface = Root.ActualWidth <= 720 || Root.ActualHeight <= 560;
        var statisticsPanelWidth = compactStatisticsSurface
            ? Math.Clamp(Root.ActualWidth - 96d, 240d, 300d)
            : 320d;
        var statisticsOutlineHeight = compactStatisticsSurface ? 120d : 190d;

        var panel = new StackPanel { Spacing = 10, Width = statisticsPanelWidth };
        panel.Children.Add(new TextBlock { Text = "Dokumentstatistik", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(CreateStatisticsLine("Wörter", snapshot.Words.ToString()));
        panel.Children.Add(CreateStatisticsLine("Zeichen", snapshot.Chars.ToString()));
        panel.Children.Add(CreateStatisticsLine("Lesezeit", $"~{snapshot.ReadMinutes} min"));
        panel.Children.Add(CreateStatisticsLine("Überschriften", snapshot.Headings.ToString()));

        if (snapshot.Goal > 0)
        {
            panel.Children.Add(CreateStatisticsLine("Wortziel", $"{snapshot.Words} / {snapshot.Goal} ({snapshot.GoalPercent}%)"));
        }
        else
        {
            panel.Children.Add(CreateStatisticsLine("Wortziel", "Nicht gesetzt"));
        }

        if (snapshot.HasSelection)
        {
            panel.Children.Add(CreateStatisticsLine(
                "Auswahl",
                $"{snapshot.SelectionWords} Wörter · {snapshot.SelectionChars} Zeichen"));
        }

        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 2, 0, 0),
            Opacity = 0.18,
            Background = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128))
        });
        panel.Children.Add(new TextBlock { Text = "Gliederung", FontSize = 12, Opacity = 0.68 });

        var outlinePanel = new StackPanel { Spacing = 2 };
        if (snapshot.Outline.Count == 0)
        {
            outlinePanel.Children.Add(new TextBlock { Text = "Keine Überschriften", Opacity = 0.62 });
        }
        else
        {
            foreach (var heading in snapshot.Outline)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var levelText = new TextBlock
                {
                    Text = $"H{heading.Level}",
                    MinWidth = 22,
                    FontSize = 10,
                    Opacity = 0.55,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var headingText = new TextBlock
                {
                    Text = heading.Text,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    MaxLines = 1,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(headingText, 1);
                row.Children.Add(levelText);
                row.Children.Add(headingText);

                var button = new Button
                {
                    Content = row,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(Math.Clamp((heading.Level - 1) * 10, 0, 40), 6, 8, 6),
                    Tag = heading.Index
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"H{heading.Level} {heading.Text}");
                ToolTipService.SetToolTip(button, heading.Text);
                button.Click += StatisticsOutlineButton_Click;
                outlinePanel.Children.Add(button);
            }
        }

        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = statisticsOutlineHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = outlinePanel
        });

        var flyout = new Flyout { Content = panel };
        statisticsFlyout = flyout;
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(statisticsFlyout, flyout))
            {
                statisticsFlyout = null;
            }
        };
        flyout.ShowAt(anchor);
    }

    private static Grid CreateStatisticsLine(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock { Text = label, Opacity = 0.68 };
        var valueText = new TextBlock { Text = value };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
        return grid;
    }

    private async Task<StatisticsSnapshot?> GetStatisticsSnapshotAsync()
    {
        if (!editorReady || EditorWebView.CoreWebView2 is null)
        {
            return null;
        }

        var result = await ExecuteScriptWithTimeoutAsync(
            "typeof getStatisticsSnapshot === 'function' ? getStatisticsSnapshot() : null;",
            TimeSpan.FromSeconds(4),
            "get statistics snapshot");

        if (string.IsNullOrWhiteSpace(result) || string.Equals(result, "null", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StatisticsSnapshot>(result, jsonOptions);
        }
        catch (JsonException ex)
        {
            Diagnostics.LogException("StatisticsSnapshot", ex);
            return null;
        }
    }

    private async void StatisticsOutlineButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiEventAsync("Statistics.NavigateOutline", async () =>
        {
            if (sender is not Button button || button.Tag is not int index || EditorWebView.CoreWebView2 is null)
            {
                return;
            }

            statisticsFlyout?.Hide();
            var script = $"typeof scrollToStatisticsHeading === 'function' && scrollToStatisticsHeading({index});";
            await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(4), "navigate outline heading");
        });
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Undo", () => RunEditorCommandAsync("undo"));
    private async void RedoButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Redo", () => RunEditorCommandAsync("redo"));
    private async void NewButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("NewDocument", NewDocumentAsync);
    private async void OpenButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("OpenDocument", OpenDocumentAsync);
    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("SaveDocument", async () => await SaveDocumentAsync(await GetMarkdownFromEditorAsync(), forceSaveAs: false));
    private async void SaveAsButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("SaveDocumentAs", async () => await SaveDocumentAsync(await GetMarkdownFromEditorAsync(), forceSaveAs: true));
    private async void RecentFilesButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("RecentFiles", OpenRecentAsync);
    private async void CopyMarkdownButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("CopyMarkdown", async () => CopyMarkdown(await GetMarkdownFromEditorAsync()));
    private async void ExportButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Export", () => RunEditorCommandAsync("exportHtml"));
    private async void ExportMarkdownFileButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("ExportMarkdown", async () => await ExportMarkdownFileAsync(await GetMarkdownFromEditorAsync()));
    private async void ExportHtmlButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("ExportHtml", () => RunEditorCommandAsync("exportHtml"));
    private async void PrintPdfButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("PrintPdf", PrintPdfAsync);
    private async void PrintKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await RunUiEventAsync("PrintPdf", PrintPdfAsync);
    }
    private async void ShowSourceButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("ShowSource", async () => await ShowSourceAsync(await GetMarkdownFromEditorAsync()));
    private async void HeadingOneButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.H1", () => RunEditorFormatAsync("h1"));
    private async void HeadingTwoButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.H2", () => RunEditorFormatAsync("h2"));
    private async void HeadingThreeButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.H3", () => RunEditorFormatAsync("h3"));
    private async void BoldButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.Bold", () => RunEditorFormatAsync("bold"));
    private async void ItalicButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.Italic", () => RunEditorFormatAsync("italic"));
    private async void StrikeButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.Strike", () => RunEditorFormatAsync("strike"));
    private async void QuoteButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.Quote", () => RunEditorFormatAsync("quote"));
    private async void CodeButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.Code", () => RunEditorFormatAsync("code"));
    private async void InlineCodeButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.InlineCode", () => RunEditorFormatAsync("inlineCode"));
    private async void TableButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.Table", () => RunEditorFormatAsync("table"));
    private async void TaskListButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.TaskList", () => RunEditorFormatAsync("task"));
    private async void HorizontalRuleButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.HorizontalRule", () => RunEditorFormatAsync("hr"));
    private async void LinkButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Format.Link", () => RunEditorFormatAsync("link"));
    private async void TabHelpButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("TabHelp", () => RunEditorCommandAsync("showTabHelp"));
    private async void PaletteButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("CommandPalette", () => RunEditorCommandAsync("palette"));
    private async void AddTableRowButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Table.AddRow", () => RunEditorCommandAsync("addTableRow"));
    private async void AddTableColumnButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Table.AddColumn", () => RunEditorCommandAsync("addTableCol"));
    private async void DeleteTableRowButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Table.DeleteRow", () => RunEditorCommandAsync("delTableRow"));
    private async void DeleteTableColumnButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Table.DeleteColumn", () => RunEditorCommandAsync("delTableCol"));
    private async void FindReplaceButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("FindReplace", () => RunEditorCommandAsync("findReplace"));
    private async void FocusModeButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("FocusMode", () => RunEditorCommandAsync("toggleFocus"));
    private async void SettingsButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Settings", ShowSettingsAsync);
    private async void AboutButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("About", ShowAboutAsync);

    private async void ThemeLightButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Theme.Light", () => ApplyEditorThemeAsync("light", ElementTheme.Light));
    private async void ThemeDarkButton_Click(object sender, RoutedEventArgs e) => await RunUiEventAsync("Theme.Dark", () => ApplyEditorThemeAsync("dark", ElementTheme.Dark));

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

public sealed class StatisticsSnapshot
{
    public int Words { get; set; }
    public int Chars { get; set; }
    public int ReadMinutes { get; set; } = 1;
    public int Headings { get; set; }
    public int Goal { get; set; }
    public int GoalPercent { get; set; }
    public bool HasSelection { get; set; }
    public int SelectionWords { get; set; }
    public int SelectionChars { get; set; }
    public List<StatisticsHeadingSnapshot> Outline { get; set; } = new();
}

public sealed class StatisticsHeadingSnapshot
{
    public int Index { get; set; }
    public int Level { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class AppSettings
{
    public int AppearanceVersion { get; set; }
    public string Theme { get; set; } = "light";
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
    public int EditorWidth { get; set; } = 848;
    public int FontSize { get; set; } = 20;
    public double LineHeight { get; set; } = 1.45;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 860;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool ReopenLastDocument { get; set; }
    public string? LastDocumentPath { get; set; }
}

public sealed class AppearanceSettingsBackup
{
    public string Theme { get; set; } = "light";
    public int EditorWidth { get; set; } = 848;
    public int FontSize { get; set; } = 20;
    public double LineHeight { get; set; } = 1.45;
    public DateTimeOffset CapturedUtc { get; set; }
}

public sealed record RecentFileItem(string Path, string Name)
{
    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? Path : $"{Name} - {Path}";
}
