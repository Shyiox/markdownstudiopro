using System;
using System.IO;
using System.Text;
using Microsoft.UI.Xaml;

namespace MarkdownStudioPro.WinUI;

public partial class App : Application
{
    private readonly string[] launchArgs;
    private Window? window;

    public App() : this(System.Array.Empty<string>())
    {
    }

    public App(string[] args)
    {
        launchArgs = args;
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "winui-unhandled-error.log");
            File.WriteAllText(logPath, e.Exception.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Avoid recursive startup failures while logging.
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(ResolveInitialFilePath());
        window.Activate();
    }

    private string? ResolveInitialFilePath()
    {
        if (launchArgs.Length == 0)
        {
            return null;
        }

        var candidate = launchArgs[0].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (System.Uri.TryCreate(candidate, System.UriKind.Absolute, out var uri) && uri.IsFile)
        {
            candidate = uri.LocalPath;
        }

        return System.IO.File.Exists(candidate) ? System.IO.Path.GetFullPath(candidate) : null;
    }
}
