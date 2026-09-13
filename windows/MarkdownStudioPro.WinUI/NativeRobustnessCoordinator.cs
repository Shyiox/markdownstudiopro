using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownStudioPro.WinUI;

internal readonly record struct CleanDocumentTransfer(
    string CurrentMarkdown,
    string LastSavedMarkdown,
    bool IsDirty);

internal readonly record struct SettingsAppearanceState(
    string Theme,
    int EditorWidth,
    int FontSize,
    double LineHeight);

internal static class MarkdownTransferPolicy
{
    public static CleanDocumentTransfer CompleteCleanTransfer(
        string requestedMarkdown,
        string editorCanonicalMarkdown)
    {
        _ = requestedMarkdown;
        return new CleanDocumentTransfer(
            editorCanonicalMarkdown,
            editorCanonicalMarkdown,
            false);
    }
}

internal sealed class SettingsPreviewSession<TState>
{
    public SettingsPreviewSession(TState originalState)
    {
        OriginalState = originalState;
    }

    public TState OriginalState { get; }
    public bool IsCommitted { get; private set; }

    public async Task CompleteAsync(
        bool accepted,
        Task latestPreviewTask,
        Func<TState, Task> restoreAsync,
        Func<Task> commitAsync)
    {
        Exception? previewException = null;
        try
        {
            await latestPreviewTask;
        }
        catch (Exception ex)
        {
            previewException = ex;
        }

        if (!accepted || previewException is not null)
        {
            await restoreAsync(OriginalState);
        }
        else
        {
            await commitAsync();
            IsCommitted = true;
        }

        if (previewException is not null)
        {
            ExceptionDispatchInfo.Capture(previewException).Throw();
        }
    }
}

internal sealed class UiAsyncErrorBoundary
{
    private readonly Action<string, Exception> diagnose;
    private readonly Func<string, Exception, Task> reportAsync;

    public UiAsyncErrorBoundary(
        Action<string, Exception> diagnose,
        Func<string, Exception, Task> reportAsync)
    {
        this.diagnose = diagnose;
        this.reportAsync = reportAsync;
    }

    public async Task RunAsync(string context, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            diagnose(context, ex);
            try
            {
                await reportAsync(context, ex);
            }
            catch (Exception reportingException)
            {
                diagnose(context + "/ErrorReporting", reportingException);
            }
        }
    }
}

internal sealed class AsyncOperationQueue
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task RunAsync(Func<Task> operation)
    {
        await gate.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        await gate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            gate.Release();
        }
    }
}

internal sealed class ShutdownLifetime : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private int shutdownStarted;

    public CancellationToken Token => cancellation.Token;

    public bool TryBeginShutdown()
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
        {
            return false;
        }

        cancellation.Cancel();
        return true;
    }

    public void Dispose() => cancellation.Dispose();
}

internal static class ShutdownCleanup
{
    public static void Run(
        IEnumerable<(string Name, Action Action)> steps,
        Action<string, Exception> diagnose)
    {
        foreach (var step in steps)
        {
            try
            {
                step.Action();
            }
            catch (Exception ex)
            {
                diagnose(step.Name, ex);
            }
        }
    }
}
