using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// The devbook's drift at a glance: the sync units the sweeps have spoken about, grouped
/// by the direction they go, and the <c>devbook-drift</c> issues still open.
/// </summary>
/// <remarks>
/// <para>
/// The direction is the one the sweep resolved when it verified the unit, carried on its
/// verdict. A unit whose direction changed since reads under the old one until the next
/// sweep picks it up — under its new direction, which is the sweep that now owns it.
/// A unit no sweep has verified is not listed: this part reads what the sweeps said, and
/// listing units nobody has looked at would need the devbook's own unit lister.
/// </para>
/// <para>
/// A unit is marked <see cref="DriftUnit.SyncFailed"/> when its drift issue — titled
/// <c>[Devbook drift] &lt;unit&gt;</c> by the sweep — carries <c>sync-failed</c>. The
/// title is the only key the sweep gives the issue, so it is matched as written.
/// </para>
/// <para>
/// Either source may answer alone. Only both failing makes the part unavailable, because
/// the verdicts are worth seeing without GitHub and the issues without a local verdict.
/// </para>
/// </remarks>
internal sealed class DriftInsights(
    IRepositoryDirectory repositories,
    IDriftUnitSource units,
    IDriftIssueSource issues) : IDriftInsights
{
    /// <summary>The order the groups read in: the two that write first, then the one
    /// that writes both ways, then the ones that only report or are left out.</summary>
    internal static readonly string[] DirectionOrder = ["push", "pull", "sync", "report", "off"];

    /// <summary>How the sweep titles a drift issue, before the unit or chapter it is about.</summary>
    internal const string IssueTitlePrefix = "[Devbook drift]";

    public async Task<InsightResult<DriftInsight>> GetDriftAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var inScope = scope.IsAllRepositories
            ? repositories.Repositories
            : [.. repositories.Repositories.Where(repository => scope.Repositories.Contains(repository.Alias))];

        var unitsRead = ReadUnits(inScope);
        var (issuesRead, issuesNote) = await ReadIssuesAsync(inScope, cancellationToken);

        if (unitsRead is null && issuesRead is null)
        {
            return InsightResult<DriftInsight>.Unavailable(
                "Drift is unavailable: this app keeps no devbook sync verdicts, and " + Lowered(issuesNote));
        }

        var failed = (issuesRead ?? [])
            .Where(issue => issue.SyncFailed)
            .Select(issue => (issue.RepositoryAlias, Reference: Reference(issue.Title)))
            .ToHashSet();

        var marked = (unitsRead ?? [])
            .Select(unit => unit with
            {
                SyncFailed = failed.Contains((unit.RepositoryAlias, unit.Unit.Trim().ToLowerInvariant()))
            })
            .ToList();

        var directions = marked
            .GroupBy(unit => Direction(unit.Direction))
            .OrderBy(group => Rank(group.Key))
            .Select(group => new DriftDirection(
                group.Key,
                [.. group.OrderBy(unit => unit.RepositoryAlias, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(unit => unit.Unit, StringComparer.OrdinalIgnoreCase)]))
            .ToList();

        return InsightResult<DriftInsight>.Ready(new DriftInsight(directions, issuesRead ?? [], issuesNote));
    }

    private IReadOnlyList<DriftUnit>? ReadUnits(IReadOnlyList<DashboardRepository> inScope) =>
        units.IsAvailable ? [.. inScope.SelectMany(units.Units)] : null;

    private async Task<(IReadOnlyList<DriftIssue>? Issues, string? Note)> ReadIssuesAsync(
        IReadOnlyList<DashboardRepository> inScope,
        CancellationToken cancellationToken)
    {
        var available = await issues.GetAvailabilityAsync(cancellationToken);

        if (!available.IsAvailable)
        {
            return (null, $"The drift issues could not be read: {Reason(available.Reason)}");
        }

        DriftIssueRead read;
        try
        {
            read = await issues.GetOpenAsync(inScope, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, $"The drift issues could not be read: {Reason(exception.Message)}");
        }

        if (read.Complete) return (read.Issues, null);

        var why = string.IsNullOrWhiteSpace(read.Failure) ? string.Empty : $" ({Reason(read.Failure).TrimEnd('.')})";

        return (read.Issues, $"Some repositories' drift issues could not be read{why}, so the issues listed are a floor.");
    }

    /// <summary>
    /// A provider's message cut to its first sentence. GitHub's rate-limit refusal runs
    /// on with a request id, a timestamp and a terms-of-service paragraph; the first
    /// sentence is the reason, and the rest is for GitHub Support.
    /// </summary>
    internal static string Reason(string message)
    {
        var trimmed = message.Trim();
        var end = trimmed.IndexOf(". ", StringComparison.Ordinal);

        return end < 0 ? trimmed : trimmed[..(end + 1)];
    }

    /// <summary>The unit or chapter a drift issue's title names, lower-cased.</summary>
    internal static string Reference(string title)
    {
        var trimmed = title.Trim();

        if (trimmed.StartsWith(IssueTitlePrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[IssueTitlePrefix.Length..].Trim();
        }

        return trimmed.ToLowerInvariant();
    }

    private static string? Direction(string? direction) =>
        string.IsNullOrWhiteSpace(direction) ? null : direction.Trim().ToLowerInvariant();

    private static int Rank(string? direction)
    {
        if (direction is null) return DirectionOrder.Length + 1;

        var index = Array.IndexOf(DirectionOrder, direction);

        return index < 0 ? DirectionOrder.Length : index;
    }

    private static string Lowered(string? note) =>
        string.IsNullOrEmpty(note) ? "no drift issues could be read." : char.ToLowerInvariant(note[0]) + note[1..];
}
