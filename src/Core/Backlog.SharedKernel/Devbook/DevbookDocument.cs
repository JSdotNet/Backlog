using Backlog.SharedKernel.Markdown;

namespace Backlog.SharedKernel.Devbook;

public sealed record DevbookDocument(
    string Path,
    string Title,
    DevbookMeta Metadata,
    IReadOnlyList<DevbookBlock> Blocks,
    IReadOnlyList<DevbookHeadingSummary> Headings,
    int DiagramCount)
{
    public string? Status => Metadata.Status;

    /// <summary>The rows of the <c>## History</c> table — headed
    /// <c>| Date | Change | Why |</c>, as <c>devbook-arc42.md</c> fixes it — newest
    /// first. Empty for a record with no such table, or another header: the check
    /// reports those as a warning, and there is no column to read a date from.</summary>
    public IReadOnlyList<DevbookHistoryRow> History { get; init; } = [];

    /// <summary>The concern's standing choice: the newest history row.</summary>
    public DevbookHistoryRow? StandingChoice => History.Count > 0 ? History[0] : null;

    /// <summary>Every earlier choice, each superseded by the row above it, newest
    /// first.</summary>
    public IEnumerable<DevbookHistoryRow> Superseded => History.Skip(1);

    /// <summary>Where this is a debt record, its place on the debt ladder;
    /// otherwise null, because no other record's status means a rung.</summary>
    public DevbookDebtLifecycle? DebtLifecycle =>
        IsDebtRecordPath(Path) ? Metadata.DebtLifecycle : null;

    /// <summary>Where this is a debt record, its severity; otherwise null.</summary>
    public DevbookDebtSeverity? Severity =>
        IsDebtRecordPath(Path) ? Metadata.DebtSeverity : null;

    /// <summary>The body without its title heading. Computed once: the panel
    /// hands this to the file view on every render, and a fresh list per render
    /// is a fresh parameter per render, which re-rendered every block below
    /// it.</summary>
    public IReadOnlyList<DevbookBlock> ContentBlocks => _contentBlocks ??=
        Blocks.FirstOrDefault() is DevbookHeadingBlock { Level: 1 } heading
        && string.Equals(heading.Text, Title, StringComparison.Ordinal)
            ? Blocks.Skip(1).ToList()
            : Blocks;

    private IReadOnlyList<DevbookBlock>? _contentBlocks;

    /// <summary>Whether the path is a technical debt record, <c>arc42/tdr/&lt;slug&gt;.md</c>,
    /// in either devbook layout; the folder's index is not one.</summary>
    public static bool IsDebtRecordPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var devbook = DevbookPaths.DevbookRoot + "/";
        var within = normalized.StartsWith(devbook, StringComparison.OrdinalIgnoreCase)
            ? "." + normalized[devbook.Length..]
            : normalized;
        return within.StartsWith(".arc42/tdr/", StringComparison.OrdinalIgnoreCase)
            && !within.EndsWith("/README.md", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// One block's <c>meta</c> fields, as far as the panels read them.
/// <para>
/// Beyond <see cref="Status"/> and <see cref="Related"/>, the fields devbook 1.28.0
/// added for the C4 model (<c>devbook-chapter-metadata.md</c>): <see cref="C4"/> and
/// <see cref="C4Touches"/>, the workspace elements a block is about and the ones its
/// consequences name; <see cref="Code"/>, a building block's folders; and
/// <see cref="Severity"/>, a debt record's cost. Each is kept as written — an
/// identifier is resolved against the workspace by <c>C4DevbookStore</c>, which
/// is the only place that knows the model.
/// </para>
/// </summary>
public sealed record DevbookMeta(string? Status, IReadOnlyList<string> Related)
{
    public static DevbookMeta Empty { get; } = new(null, []);

    /// <summary>The <c>c4</c> field: the elements this block is about.</summary>
    public IReadOnlyList<string> C4 { get; init; } = [];

    /// <summary>The <c>c4-touches</c> field: the elements this block's consequences
    /// name but its decision is not about.</summary>
    public IReadOnlyList<string> C4Touches { get; init; } = [];

    /// <summary>The <c>code</c> field: the repository-relative folders a building
    /// block's code sits in. Read on any block; only a
    /// <c>building-blocks/&lt;slug&gt;.md</c> file's is the model's.</summary>
    public IReadOnlyList<string> Code { get; init; } = [];

    /// <summary>The <c>severity</c> field, as written. Required on a debt record and
    /// defined nowhere else.</summary>
    public string? Severity { get; init; }

    /// <summary><see cref="Severity"/> on the convention's scale, or null when it is
    /// missing or off it.</summary>
    public DevbookDebtSeverity? DebtSeverity => Severity?.Trim().ToLowerInvariant() switch
    {
        "low" => DevbookDebtSeverity.Low,
        "medium" => DevbookDebtSeverity.Medium,
        "high" => DevbookDebtSeverity.High,
        "critical" => DevbookDebtSeverity.Critical,
        _ => null
    };

    /// <summary>
    /// <see cref="Status"/> read as a rung of the debt ladder — what a debt record's
    /// status means, and nothing else's. A missing status is
    /// <see cref="DevbookDebtLifecycle.Identified"/>, the ladder's resting value, which
    /// the convention writes by omitting the field. A value off the ladder is null
    /// rather than a guess: a repository on an older contract may configure its own.
    /// </summary>
    public DevbookDebtLifecycle? DebtLifecycle => Status?.Trim().ToLowerInvariant() switch
    {
        null or "" or "identified" => DevbookDebtLifecycle.Identified,
        "planned" => DevbookDebtLifecycle.Planned,
        "in-progress" => DevbookDebtLifecycle.InProgress,
        "resolved" => DevbookDebtLifecycle.Resolved,
        _ => null
    };
}

/// <summary>What a debt costs while it stands, lowest first.</summary>
public enum DevbookDebtSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>The debt ladder, in the order a debt climbs it.</summary>
public enum DevbookDebtLifecycle
{
    Identified,
    Planned,
    InProgress,
    Resolved
}

/// <summary>One row of a decision record's <c>## History</c> table, cells as
/// written. The rows run newest first, so the top row is the standing choice and every
/// row below it a choice it superseded.</summary>
public sealed record DevbookHistoryRow(string Date, string Change, string Why);

public sealed record DevbookHeadingSummary(int Level, string Text, DevbookMeta Metadata);

public abstract record DevbookBlock;

public sealed record DevbookHeadingBlock(int Level, string Text, DevbookMeta Metadata) : DevbookBlock;

public sealed record DevbookParagraphBlock(IReadOnlyList<MdInline> Content) : DevbookBlock;

public sealed record DevbookListBlock(bool Ordered, IReadOnlyList<IReadOnlyList<MdInline>> Items) : DevbookBlock;

public sealed record DevbookQuoteBlock(IReadOnlyList<MdInline> Content) : DevbookBlock;

public sealed record DevbookCodeBlock(string Language, string Text) : DevbookBlock;

public sealed record DevbookDiagramBlock(string Language, string Text, string Title) : DevbookBlock;

public sealed record DevbookTableBlock(IReadOnlyList<IReadOnlyList<MdInline>> Rows) : DevbookBlock;

public sealed record DevbookDividerBlock : DevbookBlock;
