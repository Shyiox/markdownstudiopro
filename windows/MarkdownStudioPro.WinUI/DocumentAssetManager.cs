using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MarkdownStudioPro.WinUI;

public sealed record AssetImportResult(string MarkdownSource, string LocalPath, string RuntimeSource);

public sealed class DocumentAssetManager
{
    private static readonly Regex MarkdownImageRegex = new(
        @"!\[[^\]]*\]\(([^)]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> MimeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
        ["image/bmp"] = ".bmp",
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp",
    };

    private readonly string draftRoot;

    public DocumentAssetManager(string draftRoot)
    {
        if (string.IsNullOrWhiteSpace(draftRoot))
        {
            throw new ArgumentException("Draft root is required.", nameof(draftRoot));
        }

        this.draftRoot = Path.GetFullPath(draftRoot);
    }

    public static DocumentAssetManager CreateDefault()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Markdown Studio Pro",
            "DraftAssets",
            Guid.NewGuid().ToString("N"));
        return new DocumentAssetManager(root);
    }

    public async Task<AssetImportResult> ImportDataUrlAsync(string dataUrl, string mimeType, string? documentPath)
    {
        if (!MimeExtensions.TryGetValue(mimeType ?? string.Empty, out var extension))
        {
            throw new InvalidDataException("Nicht unterstütztes Bildformat.");
        }

        var marker = ";base64,";
        var index = dataUrl?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) ?? -1;
        if (index < 0)
        {
            throw new InvalidDataException("Ungültige Bilddaten.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dataUrl![(index + marker.Length)..]);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Ungültige Base64-Bilddaten.", ex);
        }

        return await WriteImportedAssetAsync(bytes, extension, documentPath);
    }

    public async Task<AssetImportResult> ImportFileAsync(string filePath, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Bilddatei wurde nicht gefunden.", filePath);
        }

        var extension = Path.GetExtension(filePath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidDataException("Nicht unterstütztes Bildformat.");
        }

        var bytes = await File.ReadAllBytesAsync(filePath);
        return await WriteImportedAssetAsync(bytes, NormalizeExtension(extension), documentPath);
    }

    public string? ResolveRuntimeSource(string source, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        source = source.Trim();
        if (Uri.TryCreate(source, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme is "http" or "https" or "data")
            {
                return source;
            }

            return null;
        }

        if (Path.IsPathRooted(source))
        {
            return null;
        }

        var normalized = NormalizeRelativeSource(source);
        if (normalized is null)
        {
            return null;
        }

        string candidate;
        if (!string.IsNullOrWhiteSpace(documentPath))
        {
            var documentDirectory = Path.GetDirectoryName(Path.GetFullPath(documentPath));
            if (string.IsNullOrWhiteSpace(documentDirectory))
            {
                return null;
            }

            candidate = Path.GetFullPath(Path.Combine(
                documentDirectory,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithinDirectory(candidate, documentDirectory))
            {
                return null;
            }
        }
        else
        {
            if (!IsManagedAssetSource(normalized))
            {
                return null;
            }

            candidate = Path.GetFullPath(Path.Combine(
                draftRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithinDirectory(candidate, draftRoot))
            {
                return null;
            }
        }

        return File.Exists(candidate) ? new Uri(candidate).AbsoluteUri : null;
    }

    public async Task MaterializeReferencedAssetsAsync(string markdown, string? sourceDocumentPath, string targetDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(targetDocumentPath))
        {
            throw new ArgumentException("Target document path is required.", nameof(targetDocumentPath));
        }

        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetDocumentPath))
            ?? throw new InvalidOperationException("Target directory could not be resolved.");

        foreach (var source in GetMarkdownImageSources(markdown).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalized = NormalizeRelativeSource(source);
            if (normalized is null || !IsManagedAssetSource(normalized))
            {
                continue;
            }

            var sourcePath = ResolveManagedSourcePath(normalized, sourceDocumentPath);
            if (sourcePath is null || !File.Exists(sourcePath))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(
                targetDirectory,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithinDirectory(destination, targetDirectory))
            {
                throw new InvalidDataException("Bildpfad verlässt den Dokumentordner.");
            }

            if (string.Equals(sourcePath, destination, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(sourcePath, destination);
        }
    }

    public async Task CreateSharePackageAsync(
        string markdown,
        string? documentPath,
        string zipPath,
        string documentFileName)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new ArgumentException("ZIP path is required.", nameof(zipPath));
        }

        var normalizedMarkdown = markdown ?? string.Empty;
        var safeDocumentName = SanitizeFileName(documentFileName);
        if (!safeDocumentName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            safeDocumentName += ".md";
        }

        var rootName = Path.GetFileNameWithoutExtension(safeDocumentName);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var markdownEntry = archive.CreateEntry($"{rootName}/{safeDocumentName}", CompressionLevel.Optimal);
        await using (var entryStream = markdownEntry.Open())
        await using (var writer = new StreamWriter(entryStream, new UTF8Encoding(false)))
        {
            await writer.WriteAsync(normalizedMarkdown);
        }

        foreach (var source in GetMarkdownImageSources(normalizedMarkdown).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var absolute)
                && absolute.Scheme is "http" or "https" or "data")
            {
                continue;
            }

            var normalized = NormalizeRelativeSource(source)
                ?? throw new InvalidDataException($"Nicht portabler Bildpfad: {source}");
            var localPath = ResolveLocalSourcePath(normalized, documentPath);
            if (localPath is null || !File.Exists(localPath))
            {
                throw new InvalidDataException($"Lokales Bild fehlt: {source}");
            }

            var entryName = $"{rootName}/{normalized.Replace('\\', '/')}";
            archive.CreateEntryFromFile(localPath, entryName, CompressionLevel.Optimal);
        }
    }

    public IReadOnlyList<string> GetMarkdownImageSources(string markdown)
    {
        return MarkdownImageRegex.Matches(markdown ?? string.Empty)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(source => source.Length > 0)
            .ToArray();
    }

    public void ResetDraft()
    {
        try
        {
            if (Directory.Exists(draftRoot))
            {
                Directory.Delete(draftRoot, recursive: true);
            }
        }
        catch
        {
            // Draft cleanup is best-effort. A stale draft must never block editing.
        }
    }

    private async Task<AssetImportResult> WriteImportedAssetAsync(byte[] bytes, string extension, string? documentPath)
    {
        var fileName = $"image-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}".Substring(0, 36) + extension;
        var markdownSource = "./assets/" + fileName;

        var root = string.IsNullOrWhiteSpace(documentPath)
            ? draftRoot
            : Path.GetDirectoryName(Path.GetFullPath(documentPath))
                ?? throw new InvalidOperationException("Document directory could not be resolved.");

        var localPath = Path.Combine(root, "assets", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllBytesAsync(localPath, bytes);
        return new AssetImportResult(markdownSource, localPath, new Uri(localPath).AbsoluteUri);
    }

    private string? ResolveLocalSourcePath(string normalizedSource, string? documentPath)
    {
        var relative = normalizedSource.Replace('/', Path.DirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(documentPath))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(documentPath));
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            var candidate = Path.GetFullPath(Path.Combine(directory, relative));
            return IsWithinDirectory(candidate, directory) ? candidate : null;
        }

        if (!IsManagedAssetSource(normalizedSource))
        {
            return null;
        }

        var draftCandidate = Path.GetFullPath(Path.Combine(draftRoot, relative));
        return IsWithinDirectory(draftCandidate, draftRoot) ? draftCandidate : null;
    }

    private static string SanitizeFileName(string value)
    {
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(value) ? "dokument.md" : value);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(name) ? "dokument.md" : name;
    }

    private string? ResolveManagedSourcePath(string normalizedSource, string? sourceDocumentPath)
    {
        var relative = normalizedSource.Replace('/', Path.DirectorySeparatorChar);

        if (!string.IsNullOrWhiteSpace(sourceDocumentPath))
        {
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceDocumentPath));
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                var fromDocument = Path.GetFullPath(Path.Combine(sourceDirectory, relative));
                if (IsWithinDirectory(fromDocument, sourceDirectory) && File.Exists(fromDocument))
                {
                    return fromDocument;
                }
            }
        }

        var fromDraft = Path.GetFullPath(Path.Combine(draftRoot, relative));
        return IsWithinDirectory(fromDraft, draftRoot) ? fromDraft : null;
    }

    private static string? NormalizeRelativeSource(string source)
    {
        source = source.Replace('\\', '/').Trim();
        while (source.StartsWith("./", StringComparison.Ordinal))
        {
            source = source[2..];
        }

        if (source.Length == 0 || source.StartsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        var segments = source.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            return null;
        }

        return string.Join('/', segments);
    }

    private static bool IsManagedAssetSource(string normalizedSource)
    {
        return normalizedSource.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
            && AllowedExtensions.Contains(Path.GetExtension(normalizedSource));
    }

    private static bool IsWithinDirectory(string candidate, string directory)
    {
        var root = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string extension)
    {
        return extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : extension.ToLowerInvariant();
    }

    private static async Task CopyFileAsync(string source, string destination)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await input.CopyToAsync(output);
    }
}
