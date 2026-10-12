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
/// <para>
/// The issues are read once per repository focus and kept until <see cref="Invalidate"/>
/// (entry 21). The Overview tile, the Devbook tab and Needs you all ask as the pane opens,
/// and each used to go to GitHub once per repository — three reads for one answer, against
/// a rate limit. The verdicts are a local file and are read on every ask, so a sweep that
/// lands shows at once. A read GitHub's rate limit refused is kept, because asking again
/// within seconds would only spend the limit that refused it, and the part's refresh is
/// how a person asks again; any other failed or partial read is asked again next time.
/// </para>
/// <para>
/// Aliases are matched ignoring case: the verdict file and the issue source can spell a
/// repository's alias differently, and it is still the one repository.
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
    internal const string IssueTitlePrefix = DriftIssue.TitlePrefix;

    private readonly InsightCache _issues = new();

    /// <summary>Drops the issues read so far, so the next ask goes back to GitHub. What a
    /// drift part's refresh does.</summary>
    public void Invalidate() => _issues.Clear();

    public async Task<InsightResult<DriftInsight>> GetDriftAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var inScope = scope.IsAllRepositories
            ? repositories.Repositories
            : [.. repositories.Repositories.Where(repository => scope.Repositories.Contains(repository.Alias))];

        var unitsRead = ReadUnits(inScope);

        // Keyed on the repositories asked about and nothing else: the window and the
        // machine do not reach drift, so moving them is not a new question for GitHub.
        var key = "issues|" + string.Join(
            '|',
            inScope.Select(repository => repository.Alias.ToLowerInvariant()).Order(StringComparer.Ordinal));
        var (issuesRead, issuesNote) = await _issues.GetOrAddAsync(
            key,
            shared => ReadIssuesAsync(inScope, shared),
            cancellationToken);

        // Only a whole read, or one GitHub's rate limit cut short, is kept. Any other
        // failure — not signed in, no network, a repository the account cannot see — is
        // asked again on the next call, so a fixed setting shows without a refresh.
        if (issuesNote is not null && !IsRateLimit(issuesNote)) _issues.Forget(key);

        if (unitsRead is null && issuesRead is null)
        {
            return InsightResult<DriftInsight>.Unavailable(
                "Drift is unavailable: this app keeps no devbook sync verdicts, and " + Lowered(issuesNote));
        }

        var failed = (issuesRead ?? [])
            .Where(issue => issue.SyncFailed)
            .Select(issue => (Alias: issue.RepositoryAlias.Trim().ToLowerInvariant(), Reference: Reference(issue.Title)))
            .ToHashSet();

        var marked = (unitsRead ?? [])
            .Select(unit => unit with
            {
                SyncFailed = failed.Contains((unit.RepositoryAlias.Trim().ToLowerInvariant(), unit.Unit.Trim().ToLowerInvariant()))
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

    private static bool IsRateLimit(string note) =>
        note.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

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
