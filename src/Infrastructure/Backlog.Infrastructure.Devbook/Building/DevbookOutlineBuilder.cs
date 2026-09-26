using System.Text.Json;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// A scope's reading outline, resolved from the committed
/// <c>_reading-order.json</c> files.
///
/// <para>A port of <c>tools/devbook/reading-order.mjs</c>, whose header states
/// the five ordering rules; they are applied here unchanged. In short: a
/// directory's declared root document sorts first, the declared order follows,
/// anything on disk the declaration does not name is appended in filename order
/// with a warning, anything declared but gone is dropped, and a directory with no
/// root document is sorted by filename.</para>
/// </summary>
internal static class DevbookOutlineBuilder
{
    private const int ReadingOrderVersion = 1;

    public static IReadOnlyList<DevbookOutlineEntry> Resolve(
        string repositoryRoot,
        string scope,
        DevbookBuildLayout layout,
        List<DevbookBuildProblem> problems)
    {
        var declarations = scope == DevbookBuildLayout.RepositoryScope
            ? RepositoryDeclarations(repositoryRoot, layout)
            : Load(repositoryRoot, $"{scope}/_reading-order.json");

        if (scope != DevbookBuildLayout.RepositoryScope)
        {
            return ReadDirectory(repositoryRoot, scope, scope, declarations, problems);
        }

        var entries = new List<DevbookOutlineEntry>();
        foreach (var folder in OrderAreas(layout.Folders, declarations, problems))
        {
            var children = ReadDirectory(repositoryRoot, folder, scope, declarations, problems);
            entries.Add(new DevbookOutlineEntry(
                "area",
                folder,
                folder,
                children.FirstOrDefault(child => child.IsRoot)?.Title ?? folder,
                null,
                layout.FolderKindForPath($"{folder}/x.md"),
                false,
                children));
        }

        return entries;
    }

    private static Dictionary<string, Declaration> RepositoryDeclarations(string repositoryRoot, DevbookBuildLayout layout)
    {
        var declarations = Load(repositoryRoot, layout.RepositoryReadingOrderPath);
        foreach (var folder in layout.Folders)
        {
            foreach (var (directory, declared) in Load(repositoryRoot, $"{folder}/_reading-order.json"))
            {
                declarations[directory] = declared;
            }
        }

        return declarations;
    }

    /// <summary>One committed file's declarations by directory. Absent,
    /// unreadable, malformed or another version: none at all.</summary>
    private static Dictionary<string, Declaration> Load(string repositoryRoot, string relativePath)
    {
        var declarations = new Dictionary<string, Declaration>(StringComparer.Ordinal);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(DevbookBuildFiles.ReadText(repositoryRoot, relativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return declarations;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetDouble(out var number)
                || number != ReadingOrderVersion
                || !root.TryGetProperty("directories", out var directories)
                || directories.ValueKind != JsonValueKind.Object)
            {
                return declarations;
            }

            foreach (var directory in directories.EnumerateObject())
            {
                if (directory.Value.ValueKind != JsonValueKind.Object) continue;

                var rootName = directory.Value.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                var order = directory.Value.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Array
                    ? o.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                    : [];

                declarations[directory.Name] = new Declaration(rootName, order, relativePath);
            }
        }

        return declarations;
    }

    private static List<string> OrderAreas(IReadOnlyList<string> folders, Dictionary<string, Declaration> declarations, List<DevbookBuildProblem> problems)
    {
        declarations.TryGetValue(DevbookBuildLayout.RepositoryScope, out var declared);
        var remaining = new List<string>(folders);
        var sequence = new List<string>();

        foreach (var name in declared?.Order ?? [])
        {
            if (remaining.Remove(name)) sequence.Add(name);
        }

        foreach (var name in remaining.Order(StringComparer.Ordinal))
        {
            if (declared is not null)
            {
                problems.Add(new DevbookBuildProblem(
                    DevbookBuildLayout.RepositoryScope,
                    "warning",
                    name,
                    $"{name} is not listed in the area order declared in `{declared.DeclaredIn}`; appended alphabetically. Move it there to pin its position."));
            }

            sequence.Add(name);
        }

        return sequence;
    }

    private static List<DevbookOutlineEntry> ReadDirectory(
        string repositoryRoot,
        string relativeDirectory,
        string scope,
        Dictionary<string, Declaration> declarations,
        List<DevbookBuildProblem> problems)
    {
        var files = new List<string>();
        var directories = new List<string>();

        foreach (var (name, isDirectory) in DevbookBuildFiles.Entries(repositoryRoot, relativeDirectory))
        {
            // `_`-prefixed folders hold tooling artifacts, not readable content.
            if (name.StartsWith('_') || name.StartsWith('.')) continue;
            if (isDirectory) directories.Add(name);
            else if (name.EndsWith(".md", StringComparison.Ordinal)) files.Add(name);
        }

        files.Sort(StringComparer.Ordinal);

        var parsed = new Dictionary<string, (string Path, string? Title, IReadOnlyDictionary<string, object?>? Meta)>(StringComparer.Ordinal);
        foreach (var name in files)
        {
            var relativePath = $"{relativeDirectory}/{name}";
            var document = DevbookMarkdown.Parse(DevbookBuildFiles.ReadText(repositoryRoot, relativePath));
            parsed[name] = (relativePath, document.FileTitle, document.FileMeta);
        }

        declarations.TryGetValue(relativeDirectory, out var declared);
        var rootName = declared?.Root is { } root && parsed.ContainsKey(root) ? root : null;
        var declaredOrder = rootName is not null ? declared!.Order : [];

        // Insertion order matters to nothing below (it is sorted), but the set is
        // the generator's: every file, then every directory, less the root.
        var remaining = new List<string>(files.Concat(directories).Where(name => name != rootName).Distinct(StringComparer.Ordinal));

        var sequence = new List<string>();
        foreach (var name in declaredOrder)
        {
            if (remaining.Remove(name)) sequence.Add(name);
        }

        foreach (var name in remaining.Order(StringComparer.Ordinal))
        {
            if (rootName is not null)
            {
                problems.Add(new DevbookBuildProblem(
                    scope,
                    "warning",
                    $"{relativeDirectory}/{name}",
                    $"{relativeDirectory}/{name} is not listed in the reading order declared in `{declared!.DeclaredIn}`; appended alphabetically. Move it there to pin its position."));
            }

            sequence.Add(name);
        }

        if (rootName is not null) sequence.Insert(0, rootName);

        var outline = new List<DevbookOutlineEntry>();
        foreach (var name in sequence)
        {
            if (parsed.TryGetValue(name, out var document))
            {
                outline.Add(new DevbookOutlineEntry(
                    "file",
                    name,
                    document.Path,
                    document.Title ?? Path.GetFileNameWithoutExtension(name),
                    document.Meta?.GetValueOrDefault("status") as string,
                    null,
                    name == rootName,
                    []));
            }
            else
            {
                var child = $"{relativeDirectory}/{name}";
                var children = ReadDirectory(repositoryRoot, child, scope, declarations, problems);
                outline.Add(new DevbookOutlineEntry(
                    "directory",
                    name,
                    child,
                    children.FirstOrDefault(entry => entry.IsRoot)?.Title ?? name,
                    null,
                    null,
                    false,
                    children));
            }
        }

        return outline;
    }

    private sealed record Declaration(string? Root, IReadOnlyList<string> Order, string DeclaredIn);
}

/// <summary>One outline row before it is numbered. <see cref="Kind"/> is set
/// for an area; every other row's kind is derived from its path when written.</summary>
internal sealed record DevbookOutlineEntry(
    string Type,
    string Name,
    string Path,
    string? Title,
    string? Status,
    string? Kind,
    bool IsRoot,
    IReadOnlyList<DevbookOutlineEntry> Children);

/// <summary>Something the build could not use, recorded rather than thrown.</summary>
internal sealed record DevbookBuildProblem(string Scope, string Severity, string? Path, string Message);
