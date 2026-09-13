using System;
using System.Collections.Generic;

namespace MarkdownStudioPro.WinUI;

internal readonly record struct LaunchFileRequest(bool HasExplicitIntent, string? FilePath);

internal enum StartupDocumentKind
{
    NewDocument,
    ExplicitFile,
    RestoredFile
}

internal readonly record struct StartupDocumentChoice(StartupDocumentKind Kind, string? FilePath);

internal static class StartupDocumentPolicy
{
    public static LaunchFileRequest ResolveLaunchRequest(
        IEnumerable<string> candidates,
        Func<string, string?> resolveExistingPath)
    {
        var hasExplicitIntent = false;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            hasExplicitIntent = true;
            var resolved = resolveExistingPath(candidate);
            if (resolved is not null)
            {
                return new LaunchFileRequest(true, resolved);
            }
        }

        return new LaunchFileRequest(hasExplicitIntent, null);
    }

    public static StartupDocumentChoice Choose(
        LaunchFileRequest request,
        bool reopenLastDocument,
        string? lastDocumentPath,
        Func<string, bool> fileExists)
    {
        if (request.HasExplicitIntent)
        {
            return request.FilePath is not null && fileExists(request.FilePath)
                ? new StartupDocumentChoice(StartupDocumentKind.ExplicitFile, request.FilePath)
                : new StartupDocumentChoice(StartupDocumentKind.NewDocument, null);
        }

        if (reopenLastDocument && lastDocumentPath is not null && fileExists(lastDocumentPath))
        {
            return new StartupDocumentChoice(StartupDocumentKind.RestoredFile, lastDocumentPath);
        }

        return new StartupDocumentChoice(StartupDocumentKind.NewDocument, null);
    }
}

internal readonly record struct SaveOperation(
    long Id,
    long DocumentGeneration,
    long ContentRevision,
    string MarkdownSnapshot,
    string? TargetPath)
{
    public SaveOperation WithTargetPath(string targetPath) => this with { TargetPath = targetPath };
}

internal readonly record struct SaveCompletion(
    bool ShouldApply,
    bool MatchesCurrentRevision,
    string MarkdownSnapshot,
    string? TargetPath);

internal readonly record struct DocumentSaveState(
    string CurrentMarkdown,
    string? CurrentFilePath,
    string LastSavedMarkdown,
    bool IsDirty);

internal sealed class DocumentSessionCoordinator
{
    private readonly HashSet<long> activeSaveIds = new();
    private long nextSaveId;
    private long latestAppliedSaveId;

    public long DocumentGeneration { get; private set; } = 1;
    public long ContentRevision { get; private set; }
    public bool IsSaving => activeSaveIds.Count > 0;

    public void ContentChanged() => ContentRevision++;

    public void ReplaceDocument()
    {
        DocumentGeneration++;
        ContentRevision = 0;
    }

    public SaveOperation BeginSave(string markdownSnapshot, string? targetPath)
    {
        var operation = new SaveOperation(
            ++nextSaveId,
            DocumentGeneration,
            ContentRevision,
            markdownSnapshot,
            targetPath);
        activeSaveIds.Add(operation.Id);
        return operation;
    }

    public bool ShouldWrite(SaveOperation operation) =>
        activeSaveIds.Contains(operation.Id)
        && operation.DocumentGeneration == DocumentGeneration
        && operation.Id > latestAppliedSaveId;

    public SaveCompletion CompleteSave(SaveOperation operation)
    {
        var shouldApply = activeSaveIds.Remove(operation.Id)
            && operation.DocumentGeneration == DocumentGeneration
            && operation.Id > latestAppliedSaveId;
        if (shouldApply)
        {
            latestAppliedSaveId = operation.Id;
        }

        return new SaveCompletion(
            shouldApply,
            operation.ContentRevision == ContentRevision,
            operation.MarkdownSnapshot,
            operation.TargetPath);
    }

    public void CancelSave(SaveOperation operation) => activeSaveIds.Remove(operation.Id);

    public DocumentSaveState ApplyCompletion(
        SaveCompletion completion,
        string currentMarkdown,
        string? currentFilePath,
        string lastSavedMarkdown)
    {
        if (!completion.ShouldApply)
        {
            return new DocumentSaveState(currentMarkdown, currentFilePath, lastSavedMarkdown, currentMarkdown != lastSavedMarkdown);
        }

        return new DocumentSaveState(
            currentMarkdown,
            completion.TargetPath,
            completion.MarkdownSnapshot,
            currentMarkdown != completion.MarkdownSnapshot);
    }
}
