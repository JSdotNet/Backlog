using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.Services;

/// <summary>
/// Reads a configured repository's <c>.backlog</c> Markdown as ordinary backlog
/// entries, so repository-authored work sits in the one list with everything
/// else instead of in a separate pane beside it.
/// <para>
/// The text is handed to the same <see cref="EntryTextParser"/> the quick-edit
/// list is built on — a <c>#</c> heading is the title, a second one starts
/// another entry, <c>##</c> headings are sub-items. The only thing taken out is
/// the <c>meta</c> fence the knowledge folders carry: it is bookkeeping for the
/// knowledge tooling, not prose, and its <c>status</c> is worth more as the
/// entry's status than as a code block nobody asked to read.
/// </para>
/// <para>
/// Nothing is written back. These files are committed to somebody's repository,
/// and quietly rewriting them from a backlog list is not an edit anyone asked
/// for — see <see cref="EntryRow.IsReadOnly"/>.
/// </para>
/// </summary>
public sealed class RepositoryBacklogSource(KnowledgeFolderSource source)
{
    public event Action? Changed
    {
        add => source.Changed += value;
        remove => source.Changed -= value;
    }

    /// <summary>Every entry authored in one repository's <c>.backlog</c> folder.
    /// An unconfigured, disabled, or missing folder is simply nothing to
    /// show — it is a setting somebody has not made yet, not a failure.</summary>
    public IReadOnlyList<RepositoryBacklogDocument> Load(string? repositoryAlias = null)
    {
        var location = source.Resolve(".backlog", repositoryAlias);
        if (!location.Available || location.FullPath is null) return [];

        var repository = location.Repository;
        var root = repository?.CloneDirectory is { Length: > 0 } clone ? clone : location.FullPath;
        var documents = new List<RepositoryBacklogDocument>();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(location.FullPath, "*.md", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var file in files)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var relativePath = RelativePath(file, root);
            var entries = RepositoryBacklogText.ToEntries(text);
            for (var segmentIndex = 0; segmentIndex < entries.Count; segmentIndex++)
            {
                var entry = entries[segmentIndex];
                documents.Add(new RepositoryBacklogDocument(
                    entry.RawText,
                    entry.Status,
                    repository?.FullName ?? "Configured repository",
                    relativePath,
                    repository?.Alias,
                    file,
                    segmentIndex));
            }
        }

        return documents;
    }

    private static string RelativePath(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}

/// <summary>One repository-authored entry: the Markdown it reads as, the status
/// its <c>meta</c> block claimed, and where it came from.</summary>
public sealed record RepositoryBacklogDocument(
    string RawText,
    EntryStatus? Status,
    string RepositoryFullName,
    string RelativePath,
    string? Area,
    string FilePath,
    int SegmentIndex);

/// <summary>Where a row came from when it is not the local store. Carries enough
/// identity to write the edited text back to the correct segment in the correct
/// <c>.backlog</c> file.</summary>
public sealed record RepositoryBacklogOrigin(
    string RepositoryFullName,
    string RelativePath,
    string FilePath,
    int SegmentIndex);

/// <summary>Turns knowledge-folder Markdown into the plain entry text the
/// quick-edit list already understands.</summary>
internal static class RepositoryBacklogText
{
    internal sealed record Entry(string RawText, EntryStatus? Status);

    public static IReadOnlyList<Entry> ToEntries(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var entries = new List<Entry>();

        foreach (var segment in EntryTextParser.SplitSegments(normalized))
        {
            var text = StripMetaBlocks(segment, out var status).Trim();
            if (text.Length == 0) continue;

            entries.Add(new Entry(text, ParseStatus(status)));
        }

        return entries;
    }

    /// <summary>Removes every <c>meta</c> fence, handing back the status the
    /// first one declared. Later blocks belong to sub-items, which are open or
    /// done rather than staged, so their status has nowhere to go.</summary>
    private static string StripMetaBlocks(string segment, out string? status)
    {
        status = null;

        var lines = segment.Split('\n');
        var kept = new List<string>(lines.Length);
        var inMeta = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (inMeta)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    inMeta = false;
                    continue;
                }

                if (status is null)
                {
                    var separator = trimmed.IndexOf(':');
                    if (separator > 0 && trimmed[..separator].Trim().Equals("status", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = trimmed[(separator + 1)..].Trim();
                        if (value.Length > 0) status = value;
                    }
                }

                continue;
            }

            if (trimmed.Equals("```meta", StringComparison.OrdinalIgnoreCase))
            {
                inMeta = true;
                continue;
            }

            kept.Add(line);
        }

        return CollapseBlankRuns(kept);
    }

    /// <summary>A removed fence leaves the blank lines that surrounded it behind;
    /// two of them in a row would render as a gap where the block used to be.</summary>
    private static string CollapseBlankRuns(List<string> lines)
    {
        var builder = new System.Text.StringBuilder();
        var blankRun = 0;

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                blankRun++;
                if (blankRun > 1) continue;
            }
            else
            {
                blankRun = 0;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Knowledge chapters and backlog entries name their states with
    /// overlapping but not identical words. Anything unrecognized simply has no
    /// opinion, and the entry falls back to its default.</summary>
    private static EntryStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        var normalized = new string([.. status.Trim().ToLowerInvariant().Where(char.IsLetter)]);

        return normalized switch
        {
            "draft" or "proposed" or "idea" => EntryStatus.Draft,
            "ready" or "accepted" or "approved" or "planned" => EntryStatus.Ready,
            "active" or "inprogress" or "doing" => EntryStatus.InProgress,
            "done" or "complete" or "completed" or "implemented" => EntryStatus.Done,
            "archived" or "superseded" or "deprecated" or "rejected" => EntryStatus.Archived,
            _ => null
        };
    }

    /// <summary>Maps a backlog <see cref="EntryStatus"/> back to the knowledge
    /// meta vocabulary used in <c>```meta</c> blocks.</summary>
    public static string ToKnowledgeStatus(EntryStatus status) => status switch
    {
        EntryStatus.Draft => "draft",
        EntryStatus.Ready => "accepted",
        EntryStatus.InProgress => "active",
        EntryStatus.Done => "done",
        EntryStatus.Archived => "archived",
        _ => status.ToString().ToLowerInvariant()
    };
}
