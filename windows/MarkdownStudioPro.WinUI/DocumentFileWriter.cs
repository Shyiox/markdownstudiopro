using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownStudioPro.WinUI;

/// <summary>Save Markdown to a sibling temporary file and atomically replace the destination.
/// An expected SHA-256 protects a document opened earlier against silent external overwrites.</summary>
public static class DocumentFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static async Task<string?> ReadFingerprintAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(input, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public static async Task<string> WriteAsync(
        string path,
        string markdown,
        string? expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(markdown);
        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("Ein Zielordner konnte nicht ermittelt werden.");
        var tempPath = Path.Combine(folder, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var bytes = Utf8NoBom.GetBytes(markdown);

        await ValidateExpectedVersionAsync(fullPath, expectedSha256, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await output.WriteAsync(bytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            // Detect normal external edits before commit, not only before writing the scratch file.
            await ValidateExpectedVersionAsync(fullPath, expectedSha256, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }

            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static async Task ValidateExpectedVersionAsync(
        string fullPath, string? expectedSha256, CancellationToken cancellationToken)
    {
        if (expectedSha256 is null)
        {
            return;
        }

        var diskHash = await ReadFingerprintAsync(fullPath, cancellationToken);
        if (!string.Equals(expectedSha256, diskHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentWriteConflictException(
                "Die Datei wurde außerhalb von Markdown Studio Pro geändert oder entfernt. " +
                "Um die fremden Änderungen nicht zu überschreiben, wurde nicht gespeichert. " +
                "Bitte prüfen Sie die Datei oder verwenden Sie 'Speichern unter'.");
        }
    }
}

public sealed class DocumentWriteConflictException : IOException
{
    public DocumentWriteConflictException(string message) : base(message) { }
}
