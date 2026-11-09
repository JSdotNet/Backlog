namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The files a C4 catalog was read from, each by its size and write time — so a
/// cached catalog can tell whether the disk still says what it was read from.
/// <para>
/// Needed because nothing tells the app about a file changed outside it: a
/// <c>git pull</c> or an editor adds, changes or removes a building block, an ADR or
/// the workspace without the folder source or the feature settings moving. The area
/// stores already take this trade per file (<c>DevbookFileCache</c>): a stat each,
/// on each load, and a touched file is read again whether or not a byte moved.
/// </para>
/// <para>
/// Kept in two halves because a change to each costs differently. The
/// <see cref="Model"/> — <c>workspace.dsl</c>, what it includes from <c>c4/</c>, and
/// <c>_c4/references.json</c> — moving means the workspace is parsed again. Only the
/// <see cref="Chapters"/> moving means the links are read again over the workspace
/// already parsed, the way a chapter saved in the app is, so the explorers keep their
/// layout.
/// </para>
/// </summary>
/// <remarks>A class, not a record: a record's equality would compare the two lists by
/// reference, and <see cref="Same"/> is the comparison meant.</remarks>
internal sealed class C4DevbookInputs(
    string? folder,
    string? domainFolder,
    IReadOnlyList<string> model,
    IReadOnlyList<string> chapters)
{
    public string? Folder { get; } = folder;

    public string? DomainFolder { get; } = domainFolder;

    public IReadOnlyList<string> Model { get; } = model;

    public IReadOnlyList<string> Chapters { get; } = chapters;

    /// <summary>The inputs as they are on disk now, for the architecture folder and
    /// the domain folder when the catalog reads it. A folder that is null or absent
    /// contributes nothing, so its appearing later is a change too.</summary>
    public static C4DevbookInputs Take(string? folder, string? domainFolder)
    {
        List<string> model = [];
        List<string> chapters = [];

        if (folder is not null && Directory.Exists(folder))
        {
            Add(model, folder, Path.Combine(folder, C4DevbookStore.WorkspaceFile));
            Add(model, folder, Path.Combine(folder, C4DevbookStore.ReferenceDirectory, C4DevbookStore.ReferenceFile));
            AddAll(model, folder, Path.Combine(folder, C4DevbookStore.IncludeDirectory), "*");
            AddAll(chapters, folder, folder, "*.md");
        }

        if (domainFolder is not null && Directory.Exists(domainFolder))
        {
            AddAll(chapters, domainFolder, domainFolder, "*.md");
        }

        return new C4DevbookInputs(folder, domainFolder, model, chapters);
    }

    /// <summary>The same inputs, read again now.</summary>
    public C4DevbookInputs Again() => Take(Folder, DomainFolder);

    /// <summary>Whether the workspace and what it is read with are as they were.</summary>
    public bool SameModel(C4DevbookInputs other) => Model.SequenceEqual(other.Model, StringComparer.Ordinal);

    /// <summary>Whether every input is as it was.</summary>
    public bool Same(C4DevbookInputs other) =>
        SameModel(other) && Chapters.SequenceEqual(other.Chapters, StringComparer.Ordinal);

    /// <summary>Every file under <paramref name="directory"/> matching
    /// <paramref name="pattern"/>, found by the rule the catalog reads the chapters
    /// by: recursive, skipping any underscored segment below <paramref name="root"/>.</summary>
    private static void AddAll(List<string> into, string root, string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return;

        IEnumerable<string> files;
        try
        {
            files = [.. Directory.EnumerateFiles(directory, pattern, Arc42DevbookReader.Recursive)
                .Where(file => !Arc42DevbookReader.IsUnderscored(Path.GetRelativePath(root, file)))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder that will not list now is a change from one that did, and the
            // read that follows reports it.
            into.Add($"{Path.GetRelativePath(root, directory)}|unlisted");
            return;
        }

        foreach (var file in files) Add(into, root, file);
    }

    private static void Add(List<string> into, string root, string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists) return;

            into.Add($"{Path.GetRelativePath(root, file)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            into.Add($"{Path.GetRelativePath(root, file)}|unreadable");
        }
    }
}
