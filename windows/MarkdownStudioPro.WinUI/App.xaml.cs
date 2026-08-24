using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        window = new MainWindow(ResolveInitialFilePath(args.Arguments));
        window.Activate();
    }

    private string? ResolveInitialFilePath(string? activationArguments)
    {
        var activationCandidates = string.IsNullOrWhiteSpace(activationArguments)
            ? Array.Empty<string>()
            : ExpandCommandLineCandidate(activationArguments);

        var candidates = activationCandidates
            .Concat(launchArgs)
            .Concat(Environment.GetCommandLineArgs().Skip(1))
            .SelectMany(ExpandCommandLineCandidate)
            .ToList();

        foreach (var candidate in candidates)
        {
            var resolved = ResolveFilePathCandidate(candidate);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? ResolveFilePathCandidate(string? value)
    {
        var candidate = value?.Trim().Trim('"');
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

    private static string[] ExpandCommandLineCandidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var direct = ResolveFilePathCandidate(value);
        if (direct is not null)
        {
            return new[] { direct };
        }

        return Regex.Matches(value, "\"([^\"]+)\"|([^\\s]+)")
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }
}
