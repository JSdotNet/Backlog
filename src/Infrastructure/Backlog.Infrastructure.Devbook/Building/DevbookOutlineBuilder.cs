using System.Globalization;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// A scope's reading outline, derived from the folder convention.
///
/// <para>A port of <c>buildOutlineDocument</c> and <c>readDirectory</c> in the
/// devbook generator's <c>outline.mjs</c>, which is what
/// <c>tools/devbook/build-database.mjs</c> imports. The name-only rules — the
/// convention table, the root it names, filename numbers and the convention's
/// slots — are <see cref="DevbookReadingConvention"/>, shared with the panels;
/// this layers on what only a parse can say: a document's own
/// <c>index: root</c>, which wins over the convention root, its
/// <c>index: exclude</c>, which keeps it out of the outline, and its
/// <c>number</c> field, which wins over the number in its filename. Nothing is
/// authored per repository — local ADR 0016 retired <c>_reading-order.json</c>,
/// and one left in a repository is ignored.</para>
///
/// <para>The areas of the repository scope come in the layout's fixed folder
/// order (<see cref="DevbookBuildLayout.Folders"/>), as the generator's
/// discovered folders do.</para>
/// </summary>
internal static class DevbookOutlineBuilder
{
    public static IReadOnlyList<DevbookOutlineEntry> Resolve(
        string repositoryRoot,
        string scope,
        DevbookBuildLayout layout,
        List<DevbookBuildProblem> problems)
    {
        if (scope != DevbookBuildLayout.RepositoryScope)
        {
            return ReadDirectory(repositoryRoot, scope, scope, scope, layout, problems);
        }

        var entries = new List<DevbookOutlineEntry>();
        foreach (var folder in layout.Folders)
        {
            var children = ReadDirectory(repositoryRoot, folder, folder, scope, layout, problems);
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

    private static List<DevbookOutlineEntry> ReadDirectory(
        string repositoryRoot,
        string relativeDirectory,
        string folderRoot,
        string scope,
        DevbookBuildLayout layout,
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

        var parsed = new Dictionary<string, ParsedFile>(StringComparer.Ordinal);
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in files)
        {
            var relativePath = $"{relativeDirectory}/{name}";
            var document = DevbookMarkdown.Parse(DevbookBuildFiles.ReadText(repositoryRoot, relativePath));
            var meta = document.FileMeta ?? new Dictionary<string, object?>(StringComparer.Ordinal);

            // Dropped here rather than filtered later, so an excluded document can
            // never be picked as the root or occupy a number.
            if (IndexRole(meta) == "exclude")
            {
                excluded.Add(name);
                continue;
            }

            parsed[name] = new ParsedFile(relativePath, document.FileTitle, meta, DocumentNumber(name, meta));
        }

        var folderKind = layout.FolderKindForPath($"{relativeDirectory}/x.md");
        var convention = DevbookReadingConvention.For(folderKind, Depth(relativeDirectory, folderRoot));
        var rootName = ResolveRoot(relativeDirectory, parsed, excluded, convention, scope, problems);

        // A subdirectory has no metadata block, so its number can only come from
        // its name.
        var numbers = new Dictionary<string, long?>(StringComparer.Ordinal);
        foreach (var (name, file) in parsed) numbers[name] = file.Number;
        foreach (var name in directories) numbers[name] = DevbookReadingConvention.FileNumber(name);

        ReportDuplicateNumbers(relativeDirectory, numbers, rootName, scope, problems);

        var sequence = DevbookReadingConvention.Order(
            convention,
            parsed.Keys.Concat(directories),
            rootName,
            name => numbers.GetValueOrDefault(name));

        var outline = new List<DevbookOutlineEntry>();
        foreach (var name in sequence)
        {
            if (parsed.TryGetValue(name, out var document))
            {
                var fileFolder = layout.FolderKindForPath(document.Path);
                var type = DevbookBuildLayout.ResolveType(fileFolder, document.Meta);

                outline.Add(new DevbookOutlineEntry(
                    "file",
                    name,
                    document.Path,
                    document.Title ?? Path.GetFileNameWithoutExtension(name),
                    ResolveStatus(fileFolder, document.Meta),
                    // The file's resolved `type` where it has one, as the
                    // generator's entry carries it; otherwise the row derives its
                    // folder kind from the path when it is written.
                    type is null ? null : Scalar(type),
                    name == rootName,
                    []));
            }
            else
            {
                var child = $"{relativeDirectory}/{name}";
                var children = ReadDirectory(repositoryRoot, child, folderRoot, scope, layout, problems);
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

    /// <summary>
    /// The directory's root document: the one declaring <c>index: root</c>, or
    /// failing that the convention's root when it is there. Two declarations is
    /// an error — the first by name wins — and a convention root that is missing
    /// or excluded is a warning.
    /// </summary>
    private static string? ResolveRoot(
        string relativeDirectory,
        Dictionary<string, ParsedFile> parsed,
        HashSet<string> excluded,
        DevbookDirectoryConvention? convention,
        string scope,
        List<DevbookBuildProblem> problems)
    {
        var declared = parsed.Where(entry => IndexRole(entry.Value.Meta) == "root").Select(entry => entry.Key).ToList();
        if (declared.Count > 1)
        {
            problems.Add(new DevbookBuildProblem(scope, "error", relativeDirectory,
                $"{relativeDirectory} has more than one document declaring `index: root` ({string.Join(", ", declared)}); a directory has exactly one entry point."));
        }

        if (declared.Count > 0) return declared[0];
        if (convention is null) return null;
        if (parsed.ContainsKey(convention.Root)) return convention.Root;

        problems.Add(excluded.Contains(convention.Root)
            ? new DevbookBuildProblem(scope, "warning", $"{relativeDirectory}/{convention.Root}",
                $"{relativeDirectory}/{convention.Root} is the directory's root document by convention but declares `index: exclude`, so the directory now has no entry point. Drop the field, or mark another file `index: root`.")
            : new DevbookBuildProblem(scope, "warning", relativeDirectory,
                $"{relativeDirectory} has no {convention.Root}; the convention makes it this directory's root document and the first thing read. Declare `index: root` on another file to name a different entry point."));

        return null;
    }

    private static void ReportDuplicateNumbers(
        string relativeDirectory,
        Dictionary<string, long?> numbers,
        string? rootName,
        string scope,
        List<DevbookBuildProblem> problems)
    {
        var byNumber = new Dictionary<long, string>();
        foreach (var name in numbers.Keys.Where(name => name != rootName).Order(StringComparer.Ordinal))
        {
            if (numbers[name] is not { } number) continue;

            if (byNumber.TryGetValue(number, out var first))
            {
                problems.Add(new DevbookBuildProblem(scope, "error", $"{relativeDirectory}/{name}",
                    $"{relativeDirectory}/{name} and {relativeDirectory}/{first} are both numbered {number.ToString(CultureInfo.InvariantCulture)}; a number identifies one document in its directory."));
            }
            else
            {
                byNumber[number] = name;
            }
        }
    }

    /// <summary>How many levels <paramref name="relativeDirectory"/> sits below
    /// its folder's root — the <c>*</c> count of the generator's convention key.</summary>
    private static int Depth(string relativeDirectory, string folderRoot) =>
        relativeDirectory.Length <= folderRoot.Length
            ? 0
            : relativeDirectory[(folderRoot.Length + 1)..].Split('/').Length;

    /// <summary>The generator's <c>indexRole</c>: <c>root</c>, <c>exclude</c>, or
    /// <see langword="null"/> for an ordinary listed document.</summary>
    private static string? IndexRole(IReadOnlyDictionary<string, object?> meta) =>
        meta.GetValueOrDefault("index") is string value && value is "root" or "exclude" ? value : null;

    /// <summary>The generator's <c>documentNumber</c>: an all-digit <c>number</c>
    /// field, else the number in the filename.</summary>
    private static long? DocumentNumber(string name, IReadOnlyDictionary<string, object?> meta)
    {
        if (meta.GetValueOrDefault("number") is string declared
            && declared.Length > 0
            && declared.All(char.IsAsciiDigit)
            && long.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return DevbookReadingConvention.FileNumber(name);
    }

    /// <summary>The generator's <c>resolveStatus</c>: the declared status, else the
    /// folder's resting one.</summary>
    private static string? ResolveStatus(string? folder, IReadOnlyDictionary<string, object?> meta) =>
        meta.GetValueOrDefault("status") is { } declared ? Scalar(declared) : DevbookBuildLayout.RestingStatusFor(folder);

    private static string Scalar(object value) =>
        value is IReadOnlyList<string> list ? string.Join(',', list) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    private sealed record ParsedFile(string Path, string? Title, IReadOnlyDictionary<string, object?> Meta, long? Number);
}

/// <summary>One outline row before it is numbered. <see cref="Kind"/> is set
/// for an area (its folder kind) and for a file with a resolved <c>type</c>;
/// every other row's kind is derived from its path when written.</summary>
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
