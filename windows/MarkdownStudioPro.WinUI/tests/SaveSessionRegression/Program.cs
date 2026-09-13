using MarkdownStudioPro.WinUI;

var tests = new (string Name, Action Run)[]
{
    ("existing explicit launch path wins over session restore", ExistingExplicitPathWins),
    ("missing explicit launch path does not restore the last document", MissingExplicitPathOpensNewDocument),
    ("no explicit launch path preserves session restore", NoExplicitPathRestoresLastDocument),
    ("session restore off opens a new document", RestoreOffOpensNewDocument),
    ("older save completion cannot replace a newer completion", OlderSaveCompletionIsIgnored),
    ("save completion from a replaced document is ignored", ReplacedDocumentIgnoresOldSave),
    ("saving state remains active while another save is running", SavingStateTracksAllOperations),
    ("Save As completion keeps a newer editor revision dirty", SaveAsKeepsNewerRevisionDirty),
    ("Save As cancel preserves revision and ends saving state", SaveAsCancelPreservesState)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
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

static void ExistingExplicitPathWins()
{
    var choice = StartupDocumentPolicy.Choose(
        new LaunchFileRequest(true, "A.md"), true, "B.md", _ => true);
    Equal(StartupDocumentKind.ExplicitFile, choice.Kind);
    Equal("A.md", choice.FilePath);
}

static void MissingExplicitPathOpensNewDocument()
{
    var request = StartupDocumentPolicy.ResolveLaunchRequest(
        new[] { "missing-A.md" }, _ => null);
    var choice = StartupDocumentPolicy.Choose(request, true, "B.md", path => path == "B.md");
    Equal(true, request.HasExplicitIntent);
    Equal(StartupDocumentKind.NewDocument, choice.Kind);
    Equal(null, choice.FilePath);
}

static void NoExplicitPathRestoresLastDocument()
{
    var choice = StartupDocumentPolicy.Choose(
        new LaunchFileRequest(false, null), true, "B.md", path => path == "B.md");
    Equal(StartupDocumentKind.RestoredFile, choice.Kind);
    Equal("B.md", choice.FilePath);
}

static void RestoreOffOpensNewDocument()
{
    var choice = StartupDocumentPolicy.Choose(
        new LaunchFileRequest(false, null), false, "B.md", _ => true);
    Equal(StartupDocumentKind.NewDocument, choice.Kind);
}

static void OlderSaveCompletionIsIgnored()
{
    var state = new DocumentSessionCoordinator();
    state.ContentChanged();
    var saveA = state.BeginSave("A", "document.md");
    state.ContentChanged();
    var saveB = state.BeginSave("B", "document.md");

    var completionB = state.CompleteSave(saveB);
    Equal(false, state.ShouldWrite(saveA));
    var completionA = state.CompleteSave(saveA);

    Equal(true, completionB.ShouldApply);
    Equal(false, completionA.ShouldApply);
}

static void ReplacedDocumentIgnoresOldSave()
{
    var state = new DocumentSessionCoordinator();
    var saveA = state.BeginSave("A", "A.md");
    state.ReplaceDocument();

    var completionA = state.CompleteSave(saveA);

    Equal(false, completionA.ShouldApply);
}

static void SavingStateTracksAllOperations()
{
    var state = new DocumentSessionCoordinator();
    var saveA = state.BeginSave("A", "document.md");
    var saveB = state.BeginSave("B", "document.md");
    state.CompleteSave(saveA);
    Equal(true, state.IsSaving);
    state.CompleteSave(saveB);
    Equal(false, state.IsSaving);
}

static void SaveAsKeepsNewerRevisionDirty()
{
    var state = new DocumentSessionCoordinator();
    state.ContentChanged();
    var saveAs = state.BeginSave("A", null).WithTargetPath("new.md");
    state.ContentChanged();

    var completion = state.CompleteSave(saveAs);
    var applied = state.ApplyCompletion(completion, "B", "old.md", "old baseline");

    Equal(true, completion.ShouldApply);
    Equal(false, completion.MatchesCurrentRevision);
    Equal(2L, state.ContentRevision);
    Equal("A", completion.MarkdownSnapshot);
    Equal("new.md", completion.TargetPath);
    Equal("B", applied.CurrentMarkdown);
    Equal("new.md", applied.CurrentFilePath);
    Equal("A", applied.LastSavedMarkdown);
    Equal(true, applied.IsDirty);
}

static void SaveAsCancelPreservesState()
{
    var state = new DocumentSessionCoordinator();
    state.ContentChanged();
    var revision = state.ContentRevision;
    var saveAs = state.BeginSave("A", null);
    state.ContentChanged();
    state.CancelSave(saveAs);

    Equal(revision + 1, state.ContentRevision);
    Equal(false, state.IsSaving);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected ?? (object)"<null>"}, got {actual ?? (object)"<null>"}");
    }
}
