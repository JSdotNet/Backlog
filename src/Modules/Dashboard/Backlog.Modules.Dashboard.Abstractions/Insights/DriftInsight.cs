namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// One devbook sync unit as the last sweep that spoke about it left it.
/// <para>
/// Every word is the sweep's, verbatim, for the reason the Devbook context keeps them
/// so: the sweep owns the report's vocabulary, and a dashboard that mapped its verdicts
/// onto fewer words would be deciding what it meant.
/// </para>
/// </summary>
/// <param name="RepositoryAlias">The repository the unit is in, as the filter names it.</param>
/// <param name="Unit">The unit's id — its root chapter, <c>path#anchor</c>.</param>
/// <param name="Kind">The unit's kind: aggregate, feature, setting, and so on.</param>
/// <param name="Direction">The unit's effective sync direction when the sweep read it,
/// or null for a verdict recorded before directions were kept.</param>
/// <param name="DirectionFrom">The file the direction was inherited from, or null.</param>
/// <param name="Verdict">The unit's rolled-up verdict.</param>
/// <param name="Action">What the sweep did about the unit.</param>
/// <param name="Link">The pull request or drift issue that action produced, or null.</param>
/// <param name="RecordedAt">When the run that said it finished.</param>
public sealed record DriftUnit(
    string RepositoryAlias,
    string Unit,
    string? Kind,
    string? Direction,
    string? DirectionFrom,
    string? Verdict,
    string? Action,
    string? Link,
    DateTimeOffset RecordedAt)
{
    /// <summary>Whether the unit's drift issue carries <c>sync-failed</c>: a sweep tried
    /// it, did not finish, and will not try again until a person clears the label.
    /// Set by the derivation from the open issues, never by the source.</summary>
    public bool SyncFailed { get; init; }
}

/// <summary>The units one sync direction holds, in the order they read.</summary>
/// <param name="Direction">The direction — <c>push</c>, <c>pull</c>, <c>sync</c>,
/// <c>report</c>, <c>off</c> — or null for the units whose direction no sweep
/// recorded.</param>
public sealed record DriftDirection(string? Direction, IReadOnlyList<DriftUnit> Units);

/// <summary>One open <c>devbook-drift</c> issue.</summary>
/// <param name="RepositoryAlias">The repository it is filed in, as the filter names it.</param>
/// <param name="Number">The issue number.</param>
/// <param name="Title">The title the sweep gave it: <c>[Devbook drift] &lt;unit or chapter&gt;</c>.</param>
/// <param name="Url">Where it opens.</param>
/// <param name="SyncFailed">Whether it carries <c>sync-failed</c>.</param>
public sealed record DriftIssue(string RepositoryAlias, int Number, string Title, string Url, bool SyncFailed);

/// <summary>
/// The devbook's drift at a glance: every unit a sweep has spoken about, by the
/// direction it goes, and the drift issues still open.
/// <para>
/// Two sources, and either may answer alone. The units are local — the sweeps' verdicts
/// as this machine recorded them — and the issues are GitHub's; a GitHub that cannot be
/// read leaves the units standing and says so in <see cref="IssuesNote"/>, rather than
/// taking the part down with it.
/// </para>
/// </summary>
/// <param name="Directions">The units, one group per direction that has any, in the
/// order push, pull, sync, report, off, then the ones with no direction recorded.</param>
/// <param name="Issues">The open <c>devbook-drift</c> issues in the repositories in
/// scope, oldest first.</param>
/// <param name="IssuesNote">Null when the issues were read whole; otherwise why they were
/// not, or not all of them.</param>
public sealed record DriftInsight(
    IReadOnlyList<DriftDirection> Directions,
    IReadOnlyList<DriftIssue> Issues,
    string? IssuesNote)
{
    /// <summary>Every unit, whatever its direction.</summary>
    public int UnitCount => Directions.Sum(direction => direction.Units.Count);

    /// <summary>The open drift issues carrying <c>sync-failed</c>.</summary>
    public IReadOnlyList<DriftIssue> SyncFailed => [.. Issues.Where(issue => issue.SyncFailed)];
}
