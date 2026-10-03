namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The last thing a devbook sync sweep said about one chapter.
/// <para>
/// A sweep checks each sync unit's chapters against the code and closes its run with a
/// verdict per chapter and per unit. This is one of those verdicts, filed against the
/// chapter it is about, so a reader of the chapter sees it without knowing which run
/// produced it. Every word is the sweep's, verbatim: the sweep owns the report's shape,
/// and a reader mapping its verdicts onto fewer members would be deciding what it
/// meant.
/// </para>
/// </summary>
/// <param name="Repository">The repository, as <c>owner/name</c>.</param>
/// <param name="ChapterPath">The chapter's file as <see cref="DevbookChapterKey.Canonical"/>
/// spells it.</param>
/// <param name="Anchor">The heading inside that file, lower-cased, or empty for the
/// file itself.</param>
/// <param name="Verdict">The chapter's own verdict: <c>aligned</c>, <c>code-ahead</c>,
/// <c>spec-ahead</c>, <c>conflict</c> or <c>unresolved</c>.</param>
/// <param name="Evidence">The sweep's one line for it.</param>
/// <param name="Unit">The id of the sync unit the chapter belongs to — its root chapter,
/// <c>path#anchor</c>, as the sweep wrote it.</param>
/// <param name="UnitKind">The unit's kind.</param>
/// <param name="UnitVerdict">The unit's rolled-up verdict.</param>
/// <param name="Action">What the sweep did about the unit.</param>
/// <param name="Link">The pull request or drift issue that action produced, or
/// null.</param>
/// <param name="RecordedAt">When the run that said it finished.</param>
/// <param name="RunId">The run that said it.</param>
public sealed record DevbookSyncVerdict(
    string Repository,
    string ChapterPath,
    string Anchor,
    string? Verdict,
    string? Evidence,
    string Unit,
    string? UnitKind,
    string? UnitVerdict,
    string? Action,
    string? Link,
    DateTimeOffset RecordedAt,
    string? RunId)
{
    /// <summary>Whether this chapter is its unit's root — the chapter the unit is
    /// named for, which carries the unit's own badge beside its chapter's.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUnitRoot =>
        DevbookSyncChapters.Same(Unit, ChapterPath, Anchor);
}

/// <summary>
/// The latest devbook sync verdict of every chapter a sweep has spoken about.
/// <para>
/// The latest and only the latest: a sweep runs weekly and re-verifies the units it
/// picks up, so the verdict worth showing beside a chapter is the most recent one, and
/// a run replaces exactly the chapters it names — a pull sweep that verified the Tasks
/// aggregate says nothing about the Inbox one, and leaves its verdict standing.
/// </para>
/// <para>
/// A port of this context because the verdict is about a chapter: the run that carried
/// it is the Sessions context's, but what a reader opening a chapter wants to know is
/// whether the code still agrees with it, which is this context's question.
/// </para>
/// </summary>
public interface IDevbookSyncVerdicts
{
    /// <summary>Raised after a record changed what is stored.</summary>
    event Action? Changed;

    /// <summary>Every verdict recorded for one file of one repository, any heading in
    /// it. Empty for a repository or a file no sweep has spoken about.</summary>
    /// <param name="repository"><c>owner/name</c>, matched without regard to case.</param>
    /// <param name="chapterPath">The file, in any spelling
    /// <see cref="DevbookChapterKey.Canonical"/> accepts.</param>
    /// <param name="folders">The folders configured for the repository, so a relocated
    /// folder keys like the conventional one.</param>
    IReadOnlyList<DevbookSyncVerdict> For(
        string repository,
        string chapterPath,
        IReadOnlyList<DevbookFolderSetting>? folders = null);

    /// <summary>Records one run's verdicts, replacing whatever was recorded for the
    /// chapters they name and leaving every other chapter's.</summary>
    Task RecordAsync(IReadOnlyList<DevbookSyncVerdict> verdicts, CancellationToken cancellationToken = default);
}

/// <summary>
/// The chapter ids a devbook sweep writes — <c>path#anchor</c> — split into the two
/// halves a verdict is filed under.
/// </summary>
public static class DevbookSyncChapters
{
    /// <summary>The file, canonical, and the anchor, lower-cased, of one chapter id.</summary>
    public static (string Path, string Anchor) Split(string? chapter, IReadOnlyList<DevbookFolderSetting>? folders = null)
    {
        if (string.IsNullOrWhiteSpace(chapter)) return (string.Empty, string.Empty);

        var hash = chapter.IndexOf('#', StringComparison.Ordinal);
        var anchor = hash < 0 ? string.Empty : chapter[(hash + 1)..].Trim().ToLowerInvariant();

        return (DevbookChapterKey.Canonical(chapter, folders), anchor);
    }

    /// <summary>Whether a chapter id names the file and heading given.</summary>
    public static bool Same(string? chapter, string path, string anchor)
    {
        var (otherPath, otherAnchor) = Split(chapter);

        return string.Equals(otherPath, DevbookChapterKey.Canonical(path), StringComparison.OrdinalIgnoreCase)
            && string.Equals(otherAnchor, anchor, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The anchor a heading is addressed by, the way the devbook's own tools derive it:
    /// lower-cased and trimmed, everything but letters, digits, underscores, spaces and
    /// hyphens dropped, and each space a hyphen. <c>Delivery Run Recording</c> is
    /// <c>delivery-run-recording</c>.
    /// </summary>
    public static string AnchorOf(string? heading)
    {
        if (string.IsNullOrWhiteSpace(heading)) return string.Empty;

        var kept = heading.Trim().ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' || char.IsWhiteSpace(ch))
            .Select(ch => char.IsWhiteSpace(ch) ? '-' : ch);

        return new string([.. kept]);
    }
}
