using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Devbook;

namespace Backlog.Infrastructure.Devbook.Documents;

/// <summary>
/// The <c>##</c> headings the chapter references point at, as their files write
/// them — <c>Model Context Protocol Servers</c>, where the reference carries
/// only the slug <c>model-context-protocol-servers</c>. Read here, beside the
/// folder readers, because a module's screen opens no file itself.
/// <para>
/// A reference whose file cannot be read, or whose slug matches no heading, is
/// simply absent from the answer: the caller falls back to the slug, which is
/// what it showed before any heading was read.
/// </para>
/// </summary>
public static class DevbookChapterHeadings
{
    /// <summary>The key a chapter reference is answered under: its path and its
    /// slug, both lower-cased, so two spellings of one chapter meet.</summary>
    public static string KeyOf(DevbookReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return reference.Slug is null
            ? reference.Path.ToLowerInvariant()
            : $"{reference.Path.ToLowerInvariant()}#{reference.Slug.ToLowerInvariant()}";
    }

    /// <summary>
    /// The headings of <paramref name="references"/> in the repository the folder
    /// <paramref name="folderKey"/> resolves in. Answers offline and from disk, as
    /// <see cref="DevbookStackStamps.Load"/> does; a branch snapshot has no
    /// repository root to read from, so it answers none.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Load(
        IDevbookFolderSource? folders,
        string folderKey,
        string? repositoryAlias,
        IEnumerable<DevbookReference> references)
    {
        if (folders is null) return Empty;

        try
        {
            return Read(folders.Resolve(folderKey, repositoryAlias).RootPath, references);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return Empty;
        }
    }

    /// <summary>The headings of the chapter references among
    /// <paramref name="references"/>, each file read once, keyed by
    /// <see cref="KeyOf"/>.</summary>
    public static IReadOnlyDictionary<string, string> Read(string? repositoryRoot, IEnumerable<DevbookReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return Empty;

        var root = Path.GetFullPath(repositoryRoot);
        var wanted = references.Where(reference => reference.IsChapter).ToList();
        var headings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in wanted.GroupBy(reference => reference.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (Headings(root, file.Key) is not { } inFile) continue;

            foreach (var reference in file)
            {
                if (inFile.TryGetValue(reference.Slug!, out var heading)) headings[KeyOf(reference)] = heading;
            }
        }

        return headings;
    }

    /// <summary>The <c>##</c> headings of one Markdown text by their slug, the
    /// first of two that share one kept; headings inside a code fence, or
    /// indented as code, are not headings.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var headings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? fence = null;

        foreach (var raw in markdown.Split('\n'))
        {
            var untrimmed = raw.TrimEnd('\r');
            var line = untrimmed.TrimStart(' ');

            // Four spaces in is an indented code block, never a heading or a fence.
            if (untrimmed.Length - line.Length > 3) continue;

            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                var marker = new string(line[0], line.TakeWhile(ch => ch == line[0]).Count());
                var bare = line.TrimEnd();

                // A fence closes on a line of its own character alone, at least as
                // long as the one that opened it; a shorter one inside is content.
                if (fence is null) fence = marker;
                else if (bare.Length >= fence.Length && bare.All(ch => ch == fence[0])) fence = null;
                continue;
            }

            if (fence is not null || !line.StartsWith("## ", StringComparison.Ordinal)) continue;

            var heading = line[3..].Trim().TrimEnd('#').Trim();
            if (heading.Length == 0) continue;

            // The GitHub rule the devbook checker resolves a reference by first —
            // `## Node.js` is `nodejs` — then the looser hyphenated one a hand-written
            // anchor sometimes uses, `node-js`.
            headings.TryAdd(Building.DevbookMarkdown.Slugify(heading), heading);
            headings.TryAdd(HyphenatedSlug(heading), heading);
        }

        return headings;
    }

    /// <summary>Every run of characters other than letters and digits read as
    /// one hyphen.</summary>
    private static string HyphenatedSlug(string heading)
    {
        var chars = heading.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>One file's headings, or null when it is outside the repository,
    /// missing or unreadable.</summary>
    private static IReadOnlyDictionary<string, string>? Headings(string root, string relativePath)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            var inside = path.StartsWith(root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            return inside && File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
