namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>Which kind of row a <see cref="RepositoryWeekly"/> is.</summary>
public enum RepositoryBandKind
{
    /// <summary>A repository this workspace has configured, named by its alias — the
    /// name the header's scope chips and the roadmap already use for it.</summary>
    Configured,

    /// <summary>Every recorded repository the workspace has not configured, folded
    /// into one row. Real work, but nothing here has a name or a colour for it.</summary>
    Other,

    /// <summary>Every session with no repository at all: the agent recorded none and
    /// its working folder lies inside no registered clone. Claude records none, so
    /// this is every Claude session outside the clones this workspace registered — and
    /// every Copilot one that did not say.</summary>
    Unrecorded
}

/// <summary>
/// One row of the sessions part's weekly series cut by repository: the measures
/// <see cref="AssistantSessionsInsight"/> already cuts by week, cut again by where the
/// sessions were recorded as working, and named the way the rest of the product names
/// a repository.
/// </summary>
/// <param name="Name">
/// The configured alias for a <see cref="RepositoryBandKind.Configured"/> row, and
/// <see cref="OtherName"/> or <see cref="UnrecordedName"/> for the other two kinds —
/// so a surface can key its colours and its scope on the same word the header does.
/// </param>
/// <param name="Kind">See <see cref="RepositoryBandKind"/>. Carried as a kind rather
/// than left to a magic name, so a surface can style or explain the two folded rows
/// without matching on a string that a rename would silently break.</param>
/// <param name="SessionsPerWeek">How many of this row's sessions fell in each ISO
/// week, on <see cref="AssistantSessionsInsight.SessionsPerWeek"/>'s terms — the week
/// the session last moved.</param>
/// <param name="PromptsPerSessionPerWeek">The mean prompts per counted session of this
/// row in each week, on <see cref="AssistantSessionsInsight.PromptsPerSessionPerWeek"/>'s
/// terms — zero for a week with no counted session.</param>
/// <param name="ActiveTimePerWeek">Hours producing, per ISO week, on
/// <see cref="AssistantSessionsInsight.ActiveTimePerWeek"/>'s buckets and terms.</param>
/// <param name="WaitingPerWeek">Hours waiting, likewise.</param>
/// <param name="MostSessionsAtOncePerWeek">The most of this row's sessions producing at
/// one instant in each week — a maximum within the row, not addable across rows.</param>
/// <param name="MostAgentsAtOncePerWeek">The most agents this row's sessions had
/// spawned and running at one instant in each week, on the same terms.</param>
public sealed record RepositoryWeekly(
    string Name,
    RepositoryBandKind Kind,
    IReadOnlyList<InsightPoint> SessionsPerWeek,
    IReadOnlyList<InsightPoint> PromptsPerSessionPerWeek,
    IReadOnlyList<InsightPoint> ActiveTimePerWeek,
    IReadOnlyList<InsightPoint> WaitingPerWeek,
    IReadOnlyList<InsightPoint> MostSessionsAtOncePerWeek,
    IReadOnlyList<InsightPoint> MostAgentsAtOncePerWeek)
{
    /// <summary>What the folded row of unconfigured repositories is called. One
    /// string, here, so the service and every surface agree on it.</summary>
    public const string OtherName = "Other repositories";

    /// <summary>What the row of sessions with no recorded repository is called.</summary>
    public const string UnrecordedName = "No repository recorded";
}
