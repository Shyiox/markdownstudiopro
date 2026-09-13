using MarkdownStudioPro.WinUI;

var tests = new (string Name, Func<Task> Run)[]
{
    ("settings preview cancel restores committed native and web state", PreviewCancelRestoresOriginalAsync),
    ("failed partial settings preview still restores committed state", FailedPreviewStillRestoresOriginalAsync),
    ("multiple settings previews cancel to the dialog-open state", MultiplePreviewsRestoreOriginalAsync),
    ("successful settings apply commits preview and suppresses rollback", SuccessfulPreviewCommitsAsync),
    ("awaited UI failure is diagnosed and contained", AwaitedUiFailureIsContainedAsync),
    ("failed modal error reporting is diagnosed and contained", FailedErrorReporterIsContainedAsync),
    ("fire-and-forget UI failure remains observed", FireAndForgetFailureIsObservedAsync),
    ("active dialog defers a reported error without overlap", ActiveDialogDefersErrorAsync),
    ("two rapid errors are serialized and both diagnosed", RapidErrorsAreSerializedAsync),
    ("dialog closing race does not overlap the next error dialog", ClosingDialogDefersErrorAsync),
    ("clean WebView transfer adopts the editor canonical baseline", CleanTransferUsesEditorCanonicalBaselineAsync),
    ("shutdown cleanup continues after an individual resource failure", ShutdownCleanupContinuesAfterFailureAsync),
    ("shutdown begins once and cancels pending lifetime work", ShutdownIsIdempotentAsync)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static async Task PreviewCancelRestoresOriginalAsync()
{
    var original = new AppearanceState("dark", 848, 20, 1.45);
    var visible = original;
    var session = new SettingsPreviewSession<AppearanceState>(original);
    visible = new AppearanceState("light", 900, 21, 1.6);

    await session.CompleteAsync(
        accepted: false,
        Task.CompletedTask,
        state => { visible = state; return Task.CompletedTask; },
        () => throw new InvalidOperationException("cancel must not commit"));

    Equal(original, visible);
    Equal(false, session.IsCommitted);
}

static async Task FailedPreviewStillRestoresOriginalAsync()
{
    var original = new AppearanceState("dark", 848, 20, 1.45);
    var native = original;
    var web = original;
    var session = new SettingsPreviewSession<AppearanceState>(original);
    native = new AppearanceState("light", 900, 21, 1.6);
    var previewFailure = Task.FromException(new InvalidOperationException("web preview failed after native apply"));

    await ThrowsAsync<InvalidOperationException>(() => session.CompleteAsync(
        accepted: false,
        previewFailure,
        state => { native = state; web = state; return Task.CompletedTask; },
        () => throw new InvalidOperationException("cancel must not commit")));

    Equal(original, native);
    Equal(original, web);
    Equal(false, session.IsCommitted);
}

static async Task MultiplePreviewsRestoreOriginalAsync()
{
    var original = new AppearanceState("dark", 848, 20, 1.45);
    var visible = original;
    var session = new SettingsPreviewSession<AppearanceState>(original);
    visible = new AppearanceState("light", 880, 19, 1.5);
    visible = new AppearanceState("dark", 920, 22, 1.8);
    visible = new AppearanceState("light", 700, 13, 1.35);

    await session.CompleteAsync(false, Task.CompletedTask, state => { visible = state; return Task.CompletedTask; }, () => Task.CompletedTask);

    Equal(original, visible);
}

static async Task SuccessfulPreviewCommitsAsync()
{
    var original = new AppearanceState("dark", 848, 20, 1.45);
    var preview = new AppearanceState("light", 900, 21, 1.6);
    var committed = original;
    var rollbackCount = 0;
    var session = new SettingsPreviewSession<AppearanceState>(original);

    await session.CompleteAsync(
        true,
        Task.CompletedTask,
        _ => { rollbackCount++; return Task.CompletedTask; },
        () => { committed = preview; return Task.CompletedTask; });

    Equal(preview, committed);
    Equal(0, rollbackCount);
    Equal(true, session.IsCommitted);
}

static async Task AwaitedUiFailureIsContainedAsync()
{
    var diagnostics = new List<string>();
    var reported = new List<string>();
    var boundary = new UiAsyncErrorBoundary(
        (context, _) => diagnostics.Add(context),
        (context, _) => { reported.Add(context); return Task.CompletedTask; });

    await boundary.RunAsync("Open", () => Task.FromException(new InvalidOperationException("boom")));

    SequenceEqual(new[] { "Open" }, diagnostics);
    SequenceEqual(new[] { "Open" }, reported);
}

static async Task FailedErrorReporterIsContainedAsync()
{
    var diagnostics = new List<string>();
    var boundary = new UiAsyncErrorBoundary(
        (context, _) => diagnostics.Add(context),
        (_, _) => Task.FromException(new InvalidOperationException("dialog already active")));

    await boundary.RunAsync("Settings", () => Task.FromException(new InvalidOperationException("preview failed")));

    SequenceEqual(new[] { "Settings", "Settings/ErrorReporting" }, diagnostics);
}

static async Task FireAndForgetFailureIsObservedAsync()
{
    var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var boundary = new UiAsyncErrorBoundary(
        (_, _) => { },
        (context, _) => { reported.TrySetResult(context); return Task.CompletedTask; });

    _ = boundary.RunAsync("StartupBridge", async () =>
    {
        await Task.Yield();
        throw new InvalidOperationException("late failure");
    });

    Equal("StartupBridge", await reported.Task.WaitAsync(TimeSpan.FromSeconds(2)));
}

static async Task ActiveDialogDefersErrorAsync()
{
    var queue = new AsyncOperationQueue();
    var releaseSettings = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var settingsEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var errorEntered = false;
    var settings = queue.RunAsync(async () => { settingsEntered.SetResult(); await releaseSettings.Task; });
    await settingsEntered.Task;
    var error = queue.RunAsync(() => { errorEntered = true; return Task.CompletedTask; });

    await Task.Delay(50);
    Equal(false, errorEntered);
    releaseSettings.SetResult();
    await Task.WhenAll(settings, error);
    Equal(true, errorEntered);
}

static async Task RapidErrorsAreSerializedAsync()
{
    var queue = new AsyncOperationQueue();
    var active = 0;
    var maximumActive = 0;
    var contexts = new List<string>();

    async Task ShowAsync(string context)
    {
        await queue.RunAsync(async () =>
        {
            active++;
            maximumActive = Math.Max(maximumActive, active);
            contexts.Add(context);
            await Task.Delay(20);
            active--;
        });
    }

    await Task.WhenAll(ShowAsync("GpuProcessExited"), ShowAsync("RenderProcessExited"));
    Equal(1, maximumActive);
    SequenceEqual(new[] { "GpuProcessExited", "RenderProcessExited" }, contexts);
}

static async Task ClosingDialogDefersErrorAsync()
{
    var queue = new AsyncOperationQueue();
    var releaseClosing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var closingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var overlap = false;
    var closing = true;
    var first = queue.RunAsync(async () => { closingEntered.SetResult(); await releaseClosing.Task; closing = false; });
    await closingEntered.Task;
    var second = queue.RunAsync(() => { overlap = closing; return Task.CompletedTask; });

    await Task.Delay(50);
    releaseClosing.SetResult();
    await Task.WhenAll(first, second);
    Equal(false, overlap);
}

static Task ShutdownIsIdempotentAsync()
{
    using var lifetime = new ShutdownLifetime();
    Equal(false, lifetime.Token.IsCancellationRequested);
    Equal(true, lifetime.TryBeginShutdown());
    Equal(true, lifetime.Token.IsCancellationRequested);
    Equal(false, lifetime.TryBeginShutdown());
    return Task.CompletedTask;
}

static Task CleanTransferUsesEditorCanonicalBaselineAsync()
{
    var state = MarkdownTransferPolicy.CompleteCleanTransfer(
        "# Neues Dokument\n\n",
        "# Neues Dokument\n");

    Equal("# Neues Dokument\n", state.CurrentMarkdown);
    Equal("# Neues Dokument\n", state.LastSavedMarkdown);
    Equal(false, state.IsDirty);
    return Task.CompletedTask;
}

static Task ShutdownCleanupContinuesAfterFailureAsync()
{
    var completed = new List<string>();
    var diagnosed = new List<string>();

    ShutdownCleanup.Run(
        new (string Name, Action Action)[]
        {
            ("WebView2", () => throw new InvalidOperationException("close failed")),
            ("Timers", () => completed.Add("Timers")),
            ("Backdrop", () => completed.Add("Backdrop"))
        },
        (name, _) => diagnosed.Add(name));

    SequenceEqual(new[] { "Timers", "Backdrop" }, completed);
    SequenceEqual(new[] { "WebView2" }, diagnosed);
    return Task.CompletedTask;
}

static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"expected {typeof(TException).Name}");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, got {actual}");
    }
}

static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException($"expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }
}

internal readonly record struct AppearanceState(string Theme, int EditorWidth, int FontSize, double LineHeight);
