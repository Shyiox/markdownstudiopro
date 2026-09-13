using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        Diagnostics.Initialize();
        Diagnostics.StepStart("App.ctor");

        try
        {
            launchArgs = args;

            Diagnostics.StepStart("App.InitializeComponent");
            try
            {
                InitializeComponent();
                Diagnostics.StepOk("App.InitializeComponent");
            }
            catch (Exception ex)
            {
                Diagnostics.StepFailed("App.InitializeComponent", ex);
                throw;
            }

            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            Diagnostics.StepOk("App.ctor");
        }
        catch (Exception ex)
        {
            Diagnostics.StepFailed("App.ctor", ex);
            throw;
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Diagnostics.LogException("Application.UnhandledException", e.Exception);
    }

    private static void OnAppDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Diagnostics.LogException("AppDomain.CurrentDomain.UnhandledException", ex);
        }
        else
        {
            Diagnostics.Warning(
                "AppDomain.CurrentDomain.UnhandledException",
                $"Non-Exception payload; terminating={e.IsTerminating}");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Diagnostics.LogException("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Diagnostics.StepStart("App.OnLaunched");

        try
        {
            var initialRequest = ResolveInitialFileRequest(args.Arguments);
            Diagnostics.Info("Launch.HasExplicitFile", initialRequest.HasExplicitIntent.ToString());

            Diagnostics.StepStart("App.MainWindowConstruct");
            window = new MainWindow(initialRequest.FilePath, initialRequest.HasExplicitIntent);
            Diagnostics.StepOk("App.MainWindowConstruct");

            Diagnostics.StepStart("App.WindowActivate");
            window.Activate();
            Diagnostics.StepOk("App.WindowActivate");

            Diagnostics.StepOk("App.OnLaunched");
        }
        catch (Exception ex)
        {
            Diagnostics.StepFailed("App.OnLaunched", ex);
            throw;
        }
    }

    private LaunchFileRequest ResolveInitialFileRequest(string? activationArguments)
    {
        var activationCandidates = string.IsNullOrWhiteSpace(activationArguments)
            ? Array.Empty<string>()
            : ExpandCommandLineCandidate(activationArguments);

        var candidates = activationCandidates
            .Concat(launchArgs)
            .Concat(Environment.GetCommandLineArgs().Skip(1))
            .SelectMany(ExpandCommandLineCandidate)
            .ToList();

        return StartupDocumentPolicy.ResolveLaunchRequest(candidates, ResolveFilePathCandidate);
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

        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
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
