namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// One stacked row of a weekly chart the sessions part draws out of the session
/// records themselves — a model, or a repository band — rather than out of the
/// activity sweeps <see cref="RepositoryWeekly"/> is cut from.
/// </summary>
/// <param name="Name">The model as the assistant named it, or the band's name on
/// <see cref="RepositoryWeekly.Name"/>'s terms.</param>
/// <param name="Kind">The band's kind for a repository row, so a surface can give a
/// configured repository its hue and leave the two folded rows without one; null for
/// a row that is not a repository.</param>
/// <param name="PerWeek">One point per week of the window, oldest first, on the
/// buckets every other weekly series of the part is drawn on.</param>
/// <param name="Total">The row's figure over the window — the columns added up.</param>
public sealed record WeeklyBand(
    string Name,
    RepositoryBandKind? Kind,
    IReadOnlyList<InsightPoint> PerWeek,
    decimal Total);

/// <summary>
/// Tokens summed over the sessions that recorded any, by kind of token. The assistant's
/// own counts, the owner sessions' only — the agents a session spawned are not in them.
/// </summary>
public sealed record TokenTotals(long Output, long Input, long CacheRead, long CacheWrite);

/// <summary>
/// How many refusals were walls for one reason overage was not available, spelled as
/// the assistant spelled it.
/// </summary>
/// <param name="Reason">The assistant's reason, such as <c>org_spend_cap_reached</c>,
/// or its overage status where it gave no reason.</param>
/// <param name="Count">How many refusals in the window gave it.</param>
public sealed record LimitWall(string Reason, int Count);
