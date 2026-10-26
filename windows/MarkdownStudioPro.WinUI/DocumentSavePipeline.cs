using System;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownStudioPro.WinUI;

/// <summary>Coordinates a guarded document save with its local image assets.
/// Check the document's expected on-disk version before copying any assets;
/// the writer checks the file again immediately before replacing it.</summary>
public static class DocumentSavePipeline
{
    public static async Task<string> SaveAsync(
        string markdown,
        string? sourceDocumentPath,
        string targetDocumentPath,
        string? expectedSha256,
        DocumentAssetManager assetManager,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDocumentPath);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(assetManager);

        await DocumentFileWriter.ValidateExpectedVersionAsync(targetDocumentPath, expectedSha256, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await assetManager.MaterializeReferencedAssetsAsync(markdown, sourceDocumentPath, targetDocumentPath);
        return await DocumentFileWriter.WriteAsync(targetDocumentPath, markdown, expectedSha256, cancellationToken);
    }
}
