using System;
using System.IO;

namespace MarkdownStudioPro.WinUI;

/// <summary>Only the packaged editor document is permitted to control the native host.</summary>
public static class WebViewSecurityPolicy
{
    public static bool IsTrustedDocumentUri(string expectedEditorUri, string? candidateUri)
    {
        if (!Uri.TryCreate(expectedEditorUri, UriKind.Absolute, out var expected) ||
            !Uri.TryCreate(candidateUri, UriKind.Absolute, out var candidate) ||
            !expected.IsFile || !candidate.IsFile ||
            !string.IsNullOrEmpty(candidate.Query))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(expected.LocalPath),
                Path.GetFullPath(candidate.LocalPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
