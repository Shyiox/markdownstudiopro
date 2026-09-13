using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace MarkdownStudioPro.WinUI;

internal static class Diagnostics
{
    private const int MaxSessionLogs = 12;
    private static readonly object Gate = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static bool initialized;
    private static string diagnosticsDirectory = string.Empty;
    private static string? currentLogPath;

    public static string DiagnosticsDirectory
    {
        get
        {
            EnsureInitialized();
            return diagnosticsDirectory;
        }
    }

    public static string? CurrentLogPath
    {
        get
        {
            EnsureInitialized();
            return currentLogPath;
        }
    }

    public static void Initialize()
    {
        lock (Gate)
        {
            if (initialized)
            {
                return;
            }

            diagnosticsDirectory = ResolveDiagnosticsDirectory();

            try
            {
                Directory.CreateDirectory(diagnosticsDirectory);
                RotateLogs(diagnosticsDirectory);
                currentLogPath = Path.Combine(
                    diagnosticsDirectory,
                    $"startup-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
                initialized = true;

                WriteCore("SESSION START");
                Info("Timestamp", DateTimeOffset.Now.ToString("O"));
                Info("ProcessId", Environment.ProcessId.ToString());
                Info("App.Version", typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? "unknown");
                Info("WindowsAppSDK.Assembly", typeof(Application).Assembly.GetName().Version?.ToString() ?? "unknown");
                Info("OS", RuntimeInformation.OSDescription);
                Info("Framework", RuntimeInformation.FrameworkDescription);
                Info("ProcessArchitecture", RuntimeInformation.ProcessArchitecture.ToString());
                Info("OSArchitecture", RuntimeInformation.OSArchitecture.ToString());
            }
            catch
            {
                diagnosticsDirectory = Path.Combine(Path.GetTempPath(), "Markdown Studio Pro", "Diagnostics");
                try
                {
                    Directory.CreateDirectory(diagnosticsDirectory);
                    currentLogPath = Path.Combine(
                        diagnosticsDirectory,
                        $"startup-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
                    initialized = true;
                    WriteCore("SESSION START (temporary fallback directory)");
                }
                catch
                {
                    initialized = true;
                    currentLogPath = null;
                }
            }
        }
    }

    public static void StepStart(string name) => WriteCore($"START {name}");

    public static void StepOk(string name) => WriteCore($"OK {name}");

    public static void StepFailed(string name, Exception ex)
    {
        WriteCore($"FAILED {name}");
        LogException(name, ex);
    }

    public static void Info(string name, string? value) =>
        WriteCore($"INFO {name}: {value ?? "<null>"}");

    public static void Warning(string name, string? value) =>
        WriteCore($"WARN {name}: {value ?? "<null>"}");

    public static void LogException(string source, Exception ex)
    {
        if (ex is null)
        {
            WriteCore($"EXCEPTION {source}: <null>");
            return;
        }

        var builder = new StringBuilder();
        builder.Append("EXCEPTION ").Append(source);

        var current = ex;
        var depth = 0;
        while (current is not null && depth < 8)
        {
            builder.AppendLine();
            builder.Append("  [").Append(depth).Append("] ")
                .Append(current.GetType().FullName)
                .Append(" HResult=0x")
                .Append(current.HResult.ToString("X8"))
                .Append(": ")
                .Append(current.Message);

            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                builder.AppendLine();
                builder.Append(current.StackTrace);
            }

            current = current.InnerException;
            depth++;
        }

        WriteCore(builder.ToString());
    }

    public static void RunStep(string name, Action action)
    {
        StepStart(name);
        try
        {
            action();
            StepOk(name);
        }
        catch (Exception ex)
        {
            StepFailed(name, ex);
            throw;
        }
    }

    public static T RunStep<T>(string name, Func<T> action)
    {
        StepStart(name);
        try
        {
            var result = action();
            StepOk(name);
            return result;
        }
        catch (Exception ex)
        {
            StepFailed(name, ex);
            throw;
        }
    }

    public static async Task RunStepAsync(string name, Func<Task> action)
    {
        StepStart(name);
        try
        {
            await action();
            StepOk(name);
        }
        catch (Exception ex)
        {
            StepFailed(name, ex);
            throw;
        }
    }

    private static void EnsureInitialized()
    {
        if (!initialized)
        {
            Initialize();
        }
    }

    private static string ResolveDiagnosticsDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "Markdown Studio Pro", "Diagnostics");
        }

        return Path.Combine(Path.GetTempPath(), "Markdown Studio Pro", "Diagnostics");
    }

    private static void RotateLogs(string directory)
    {
        try
        {
            var staleLogs = Directory
                .EnumerateFiles(directory, "startup-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(MaxSessionLogs - 1)
                .ToArray();

            foreach (var staleLog in staleLogs)
            {
                try
                {
                    File.Delete(staleLog);
                }
                catch
                {
                    // Rotation must never interfere with application startup.
                }
            }
        }
        catch
        {
            // Rotation is best-effort only.
        }
    }

    private static void WriteCore(string message)
    {
        EnsureInitialized();

        var logPath = currentLogPath;
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        var sanitized = Sanitize(message ?? string.Empty);
        var line = $"{DateTimeOffset.Now:O} [{Environment.CurrentManagedThreadId}] {sanitized}{Environment.NewLine}";

        lock (Gate)
        {
            try
            {
                // Open/append/close on every write so each startup marker is flushed even if WinUI terminates abruptly.
                File.AppendAllText(logPath, line, Utf8NoBom);
            }
            catch
            {
                // Diagnostics must never become a startup dependency.
            }
        }
    }

    private static string Sanitize(string value)
    {
        var sanitized = value;
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                sanitized = sanitized.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            }

            var userName = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(userName))
            {
                sanitized = sanitized.Replace(userName, "%USERNAME%", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Keep the original diagnostic line if redaction itself fails.
        }

        return sanitized;
    }
}
