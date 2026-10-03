using System.IO.Compression;
using MarkdownStudioPro.WinUI;

var failures = 0;
var tests = new List<(string Name, Func<Task> Run)>
{
    ("clipboard image uses draft assets before first save", ClipboardImageUsesDraftAsync),
    ("first save materializes only referenced draft assets", FirstSaveMaterializesReferencedAsync),
    ("generated image names never collide", GeneratedNamesDoNotCollideAsync),
    ("unsupported image formats are rejected", UnsupportedImagesRejectedAsync),
    ("path traversal is rejected for local image resolution", TraversalRejectedAsync),
    ("save as copies referenced managed assets to the new document", SaveAsCopiesManagedAssetsAsync),
    ("remote image sources remain remote", RemoteSourcesRemainRemoteAsync),
    ("share package contains markdown and only referenced local images", SharePackageContainsReferencedImagesAsync),
};

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

Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed");
return failures == 0 ? 0 : 1;

static string TempDir()
{
    var path = Path.Combine(Path.GetTempPath(), "msp-assets-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static DocumentAssetManager Manager(string draftRoot) => new(draftRoot);

static async Task ClipboardImageUsesDraftAsync()
{
    var root = TempDir();
    try
    {
        var manager = Manager(Path.Combine(root, "draft"));
        var result = await manager.ImportDataUrlAsync(
            "data:image/png;base64,iVBORw0KGgo=",
            "image/png",
            documentPath: null);

        True(result.MarkdownSource.StartsWith("./assets/", StringComparison.Ordinal), "markdown path");
        True(File.Exists(result.LocalPath), "draft file missing");
        Equal(new Uri(result.LocalPath).AbsoluteUri, result.RuntimeSource, "runtime URI");
        Equal(result.RuntimeSource, manager.ResolveRuntimeSource(result.MarkdownSource, null), "draft resolution");
    }
    finally { Directory.Delete(root, true); }
}

static async Task FirstSaveMaterializesReferencedAsync()
{
    var root = TempDir();
    try
    {
        var manager = Manager(Path.Combine(root, "draft"));
        var keep = await manager.ImportDataUrlAsync("data:image/png;base64,iVBORw0KGgo=", "image/png", null);
        var orphan = await manager.ImportDataUrlAsync("data:image/png;base64,iVBORw0KGgo=", "image/png", null);
        var target = Path.Combine(root, "saved", "notes.md");

        await manager.MaterializeReferencedAssetsAsync($"![Keep]({keep.MarkdownSource})", null, target);

        var keepDest = Path.Combine(Path.GetDirectoryName(target)!, keep.MarkdownSource[2..].Replace('/', Path.DirectorySeparatorChar));
        var orphanDest = Path.Combine(Path.GetDirectoryName(target)!, orphan.MarkdownSource[2..].Replace('/', Path.DirectorySeparatorChar));
        True(File.Exists(keepDest), "referenced asset not materialized");
        True(!File.Exists(orphanDest), "unreferenced draft asset was copied");
    }
    finally { Directory.Delete(root, true); }
}

static async Task GeneratedNamesDoNotCollideAsync()
{
    var root = TempDir();
    try
    {
        var manager = Manager(Path.Combine(root, "draft"));
        var a = await manager.ImportDataUrlAsync("data:image/png;base64,iVBORw0KGgo=", "image/png", null);
        var b = await manager.ImportDataUrlAsync("data:image/png;base64,iVBORw0KGgo=", "image/png", null);
        True(!string.Equals(a.MarkdownSource, b.MarkdownSource, StringComparison.OrdinalIgnoreCase), "duplicate markdown asset name");
        True(!string.Equals(a.LocalPath, b.LocalPath, StringComparison.OrdinalIgnoreCase), "duplicate local asset path");
    }
    finally { Directory.Delete(root, true); }
}

static async Task UnsupportedImagesRejectedAsync()
{
    var root = TempDir();
    try
    {
        var manager = Manager(Path.Combine(root, "draft"));
        await ThrowsAsync<InvalidDataException>(() => manager.ImportDataUrlAsync("data:image/svg+xml;base64,PHN2Zz4=", "image/svg+xml", null));
        var txt = Path.Combine(root, "not-image.txt");
        await File.WriteAllTextAsync(txt, "x");
        await ThrowsAsync<InvalidDataException>(() => manager.ImportFileAsync(txt, null));
    }
    finally { Directory.Delete(root, true); }
}

static Task TraversalRejectedAsync()
{
    var root = TempDir();
    try
    {
        var doc = Path.Combine(root, "docs", "notes.md");
        Directory.CreateDirectory(Path.GetDirectoryName(doc)!);
        var manager = Manager(Path.Combine(root, "draft"));
        Equal(null, manager.ResolveRuntimeSource("../secret.png", doc), "parent traversal");
        Equal(null, manager.ResolveRuntimeSource("C:/outside.png", doc), "absolute path");
        return Task.CompletedTask;
    }
    finally { Directory.Delete(root, true); }
}

static async Task SaveAsCopiesManagedAssetsAsync()
{
    var root = TempDir();
    try
    {
        var original = Path.Combine(root, "original", "one.md");
        Directory.CreateDirectory(Path.Combine(root, "original", "assets"));
        var sourceAsset = Path.Combine(root, "original", "assets", "image.png");
        await File.WriteAllBytesAsync(sourceAsset, new byte[] { 1, 2, 3 });
        var target = Path.Combine(root, "copy", "two.md");
        var manager = Manager(Path.Combine(root, "draft"));

        await manager.MaterializeReferencedAssetsAsync("![X](./assets/image.png)", original, target);

        var copied = Path.Combine(root, "copy", "assets", "image.png");
        True(File.Exists(copied), "managed asset not copied");
        Equal("010203", Convert.ToHexString(await File.ReadAllBytesAsync(copied)), "copied bytes");
    }
    finally { Directory.Delete(root, true); }
}

static Task RemoteSourcesRemainRemoteAsync()
{
    var root = TempDir();
    try
    {
        var manager = Manager(Path.Combine(root, "draft"));
        Equal("https://example.com/a.png", manager.ResolveRuntimeSource("https://example.com/a.png", null), "https source");
        Equal("http://example.com/a.jpg", manager.ResolveRuntimeSource("http://example.com/a.jpg", null), "http source");
        return Task.CompletedTask;
    }
    finally { Directory.Delete(root, true); }
}

static async Task SharePackageContainsReferencedImagesAsync()
{
    var root = TempDir();
    try
    {
        var documentPath = Path.Combine(root, "source", "notes.md");
        var assets = Path.Combine(root, "source", "assets");
        Directory.CreateDirectory(assets);
        await File.WriteAllBytesAsync(Path.Combine(assets, "used.png"), new byte[] { 4, 5, 6 });
        await File.WriteAllBytesAsync(Path.Combine(assets, "unused.png"), new byte[] { 7, 8, 9 });
        var markdown = "# Notes\n\n![Used](./assets/used.png)\n\n![Remote](https://example.com/remote.png)";
        var zipPath = Path.Combine(root, "notes.zip");
        var manager = Manager(Path.Combine(root, "draft"));

        await manager.CreateSharePackageAsync(markdown, documentPath, zipPath, "notes.md");

        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Select(entry => entry.FullName).OrderBy(x => x).ToArray();
        True(entries.Contains("notes/notes.md"), "markdown entry missing");
        True(entries.Contains("notes/assets/used.png"), "referenced image entry missing");
        True(!entries.Contains("notes/assets/unused.png"), "unreferenced image was packaged");
        Equal(2, entries.Length, "unexpected package entry count");

        using var reader = new StreamReader(archive.GetEntry("notes/notes.md")!.Open());
        Equal(markdown, await reader.ReadToEndAsync(), "packaged markdown changed");
    }
    finally { Directory.Delete(root, true); }
}

static void True(bool value, string label)
{
    if (!value) throw new Exception(label);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{label}: expected {expected}, got {actual}");
}

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
