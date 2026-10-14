namespace Backlog.UI.Components.Progress;

/// <summary>
/// One sprint series as its card on Settings › Sprints draws it, in plain values: the
/// library carries no domain, so the host turns its series into this and the card's
/// callbacks back into its own calls.
/// </summary>
/// <param name="Id">The series' id, as text. Used for the card's ids and test ids.</param>
/// <param name="Name">The series' name, the card's title and its switch's label.</param>
/// <param name="IsOn">Whether the series is switched on.</param>
/// <param name="Running">One line on the open sprint — "Sprint 42 runs Mon 5 Oct to Fri
/// 16 Oct" — or null before the first sprint starts.</param>
/// <param name="Repositories">The repositories the series holds, by alias, in order.</param>
/// <param name="Weeks">The sprint length in weeks: 1 or 2.</param>
/// <param name="StartWeekday">The weekday every sprint starts on.</param>
/// <param name="NamePattern">The pattern a sprint's name is made from, with
/// <c>{n}</c> for its number.</param>
/// <param name="NextNumber">The number the next sprint will carry, which the card names
/// beside the pattern.</param>
/// <param name="Goal">How the series picks its goal tasks and when the goal is met.</param>
/// <param name="ShowInDaily">Whether the open sprint shows in the daily brief.</param>
/// <param name="ShowInWeekly">Whether the open sprint shows in the weekly brief.</param>
public sealed record SprintSeriesCardModel(
    string Id,
    string Name,
    bool IsOn,
    string? Running,
    IReadOnlyList<string> Repositories,
    int Weeks,
    DayOfWeek StartWeekday,
    string NamePattern,
    int NextNumber,
    SprintGoalSettings Goal,
    bool ShowInDaily,
    bool ShowInWeekly)
{
    /// <summary>The marker a pattern carries for the sprint's number.</summary>
    public const string NumberMarker = "{n}";

    /// <summary>The name the next sprint gets from <paramref name="pattern"/>, or null
    /// for a pattern that does not carry <see cref="NumberMarker"/> exactly once with
    /// some text beside it — the same rule the series holds a pattern to.</summary>
    public static string? NextName(string? pattern, int number)
    {
        var trimmed = pattern?.Trim() ?? string.Empty;
        var first = trimmed.IndexOf(NumberMarker, StringComparison.Ordinal);

        if (first < 0
            || trimmed.IndexOf(NumberMarker, first + NumberMarker.Length, StringComparison.Ordinal) >= 0
            || string.IsNullOrWhiteSpace(trimmed.Replace(NumberMarker, string.Empty, StringComparison.Ordinal)))
        {
            return null;
        }

        return trimmed.Replace(
            NumberMarker,
            number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }
}

/// <summary>
/// The Sprint goal section of a series' card: which of the four rules count a task
/// toward the goal, the plans and tags two of them name, and the share of goal tasks
/// that meets it.
/// </summary>
/// <param name="InPlans">Tasks in the chosen plans count.</param>
/// <param name="Plans">The plan tags that rule names, with their <c>+</c>.</param>
/// <param name="MarkedAsGoal">Tasks marked as goal in the sprint count.</param>
/// <param name="Tagged">Tasks carrying one of the chosen tags count.</param>
/// <param name="Tags">The tags that rule names.</param>
/// <param name="Everything">Every task in the sprint counts.</param>
/// <param name="Share">The whole percentage of goal tasks that has to be done; 100 is
/// every goal task.</param>
public sealed record SprintGoalSettings(
    bool InPlans,
    IReadOnlyList<string> Plans,
    bool MarkedAsGoal,
    bool Tagged,
    IReadOnlyList<string> Tags,
    bool Everything,
    int Share)
{
    /// <summary>What a new series starts with: every task in the sprint counts, and
    /// the goal is met when every goal task is done.</summary>
    public static SprintGoalSettings Default { get; } = new(false, [], false, false, [], true, 100);
}
