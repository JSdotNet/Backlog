namespace Backlog.Infrastructure.Devbook.Scenarios;

/// <summary>
/// The disk reads behind the Devbook pane's scenario catalog: the pages, their runs
/// and their screenshots. Here rather than in the screen that draws them, because a
/// module's UI asks and an adapter opens the file (<c>ModuleUiIoTests</c>).
/// </summary>
public static class ScenarioFiles
{
    public static bool Exists(string path) => File.Exists(path);

    /// <summary>Every Markdown file under the domain folder, as parse.mjs walks it:
    /// <c>_meta</c>, <c>_tools</c> and dot folders are not the corpus.</summary>
    public static IReadOnlyList<string> MarkdownFiles(string domainRoot)
    {
        if (!Directory.Exists(domainRoot)) return [];

        var found = new List<string>();
        var pending = new Stack<string>([domainRoot]);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(folder))
            {
                var name = Path.GetFileName(child);
                if (name is "_meta" or "_tools" || name.StartsWith('.')) continue;
                pending.Push(child);
            }

            found.AddRange(Directory.EnumerateFiles(folder, "*.md"));
        }

        return [.. found.Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A page read and parsed, its path given repository-relative.</summary>
    public static ScenarioPage ReadPage(string fullPath, string relativePath) =>
        ScenarioPageParser.Parse(File.ReadAllText(fullPath), relativePath);

    /// <summary>The run in a run folder's <c>run.json</c>, or null.</summary>
    public static ScenarioRun? ReadRun(string runFolder)
    {
        var file = Path.Combine(runFolder, "run.json");
        return File.Exists(file) ? ScenarioRunReader.Read(File.ReadAllText(file)) : null;
    }

    /// <summary>A screenshot as a <c>data:</c> URI, or null when the file is not there
    /// or is larger than <paramref name="maxBytes"/>.</summary>
    public static async Task<string?> ReadImageAsync(string fullPath, long maxBytes, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(fullPath);
        if (!file.Exists || file.Length > maxBytes) return null;

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return $"data:{MediaType(fullPath)};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "image/png"
    };
}
