namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>One contiguous stretch of time the baseline is asked about.</summary>
/// <param name="From">When the block opens, inclusive.</param>
/// <param name="To">When it closes, exclusive of the next block's start.</param>
public sealed record ActivityWindow(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// How much work one block held: merged pull requests and closed issues, counted
/// across every repository the block was asked about.
/// <para>
/// Counts and nothing else. There is no pull request here to look inside, and that
/// is the point of the whole port — see <see cref="IActivityBaselineSource"/>.
/// </para>
/// </summary>
public sealed record ActivityVolume(
    DateTimeOffset From,
    DateTimeOffset To,
    int MergedPullRequests,
    int ClosedIssues);

/// <summary>
/// A whole baseline: one row per block, in the order the blocks were asked for.
/// <para>
/// <see cref="Complete"/> false means at least one block could not be counted, so
/// at least one row is a floor and so is anything derived from the highest of them.
/// Reported rather than thrown: a half-year of history with one gap in it is worth
/// scoring against, and an exception is not.
/// </para>
/// </summary>
public sealed record ActivityBaseline(IReadOnlyList<ActivityVolume> Blocks, bool Complete)
{
    public static ActivityBaseline Empty { get; } = new([], true);
}

/// <summary>
/// PORT — how much the person merged and closed in each of a series of past blocks,
/// so the score can be read against their own record rather than against a number
/// somebody picked.
/// <para>
/// Whose work is not a parameter, for the same reason it is not one on
/// <see cref="IActivitySource"/>: the dashboard is a personal view, and a login
/// travelling through the port would make it a reporting tool on other people. The
/// source resolves the signed-in identity itself.
/// </para>
/// <para>
/// Deliberately a second port rather than a mode of <see cref="IActivitySource"/>,
/// and the split is about cost rather than tidiness. That one answers "which pull
/// requests, and what happened inside them", which is a page walk plus several calls
/// per pull request and is affordable only over a recent window. This one answers
/// "how many", at a fixed price per block however busy the block was, which is the
/// only reason half a year of history is askable at all. A port that could do both
/// would let a caller ask for the expensive one by accident.
/// </para>
/// <para>
/// It refuses to say anything about a single week, and that refusal is deliberate
/// rather than missing: the caller hands over the blocks, so the grid — how long a
/// block is, and how many — stays a decision of the derivation that has to defend it
/// on screen instead of being buried in a provider.
/// </para>
/// </summary>
public interface IActivityBaselineSource
{
    /// <summary>
    /// Merged pull request and closed issue counts per block, for the signed-in
    /// person's own work in the given repositories. An empty repository list or an
    /// empty block list answers <see cref="ActivityBaseline.Empty"/> rather than
    /// asking anybody anything.
    /// </summary>
    Task<ActivityBaseline> GetBaselineAsync(
        IReadOnlyList<DashboardRepository> repositories,
        IReadOnlyList<ActivityWindow> blocks,
        CancellationToken cancellationToken = default);
}
