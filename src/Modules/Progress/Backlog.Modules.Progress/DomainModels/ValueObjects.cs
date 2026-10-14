using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.DomainModels;

/// <summary>
/// The first and last day of a sprint, both inclusive
/// (<c>.devbook/domain/progress/domain.md#sprint-window</c>). It starts on the series'
/// start weekday and is exactly one or two weeks long; no other window can be made.
/// </summary>
public sealed record SprintWindow
{
    private SprintWindow(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    /// <summary>How many days the window spans, both ends counted.</summary>
    public int Days => End.DayNumber - Start.DayNumber + 1;

    /// <summary>The window of a sprint of <paramref name="length"/> starting on
    /// <paramref name="start"/>, refused unless <paramref name="start"/> is
    /// <paramref name="startWeekday"/> (<c>progress.window_off_cadence</c>).</summary>
    public static Result<SprintWindow> Create(DateOnly start, SprintLength length, DayOfWeek startWeekday)
    {
        if (!SprintLengths.IsAllowed(length)) return ProgressErrors.LengthNotAllowed((int)length);
        if (start.DayOfWeek != startWeekday) return ProgressErrors.WindowOffCadence(startWeekday, (int)length);

        return new SprintWindow(start, start.AddDays((int)length - 1));
    }

    /// <summary>A window from two stored dates, held to the same cadence rule: it starts
    /// on <paramref name="startWeekday"/> and lasts exactly one or two weeks.</summary>
    public static Result<SprintWindow> Of(DateOnly start, DateOnly end, DayOfWeek startWeekday)
    {
        var days = end.DayNumber - start.DayNumber + 1;

        if (!SprintLengths.IsAllowed((SprintLength)days)) return ProgressErrors.LengthNotAllowed(days);
        if (start.DayOfWeek != startWeekday) return ProgressErrors.WindowOffCadence(startWeekday, days);

        return new SprintWindow(start, end);
    }

    /// <summary>Rehydration from storage: trusted, as the aggregate's other stored
    /// state is.</summary>
    internal static SprintWindow Restore(DateOnly start, DateOnly end) => new(start, end);

    public bool Contains(DateOnly day) => day >= Start && day <= End;
}

/// <summary>The two lengths a series may have, and the check that a value is one of
/// them — an enum cast from a stored or typed number can hold any integer.</summary>
public static class SprintLengths
{
    public static bool IsAllowed(SprintLength length) =>
        length is SprintLength.OneWeek or SprintLength.TwoWeeks;
}

/// <summary>
/// The text a sprint's name is made from, with <c>{n}</c> for its number
/// (<c>.devbook/domain/progress/domain.md#name-pattern</c>). It holds <c>{n}</c> exactly
/// once and some other text besides (<c>progress.pattern_without_number</c>).
/// </summary>
public sealed record NamePattern
{
    public const string Number = "{n}";

    public static readonly NamePattern Default = new("Sprint {n}");

    private NamePattern(string value) => Value = value;

    public string Value { get; }

    public static Result<NamePattern> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        var first = trimmed.IndexOf(Number, StringComparison.Ordinal);

        if (first < 0
            || trimmed.IndexOf(Number, first + Number.Length, StringComparison.Ordinal) >= 0
            || string.IsNullOrWhiteSpace(trimmed.Replace(Number, string.Empty, StringComparison.Ordinal)))
        {
            return ProgressErrors.PatternWithoutNumber();
        }

        return new NamePattern(trimmed);
    }

    internal static NamePattern Restore(string value) => new(value);

    /// <summary>The name of sprint <paramref name="number"/>.</summary>
    public string NameOf(int number) =>
        Value.Replace(Number, number.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
}

/// <summary>
/// When a sprint's goal is met: when <see cref="Share"/> percent of its goal tasks are
/// done (<c>.devbook/domain/progress/domain.md#goal-threshold</c>). 100 is every goal
/// task, and the default. A whole number from 1 to 100
/// (<c>progress.share_out_of_range</c>).
/// </summary>
public sealed record GoalThreshold
{
    public static readonly GoalThreshold EveryGoalTask = new(100);

    private GoalThreshold(int share) => Share = share;

    public int Share { get; }

    public static Result<GoalThreshold> Create(int share) =>
        share is < 1 or > 100 ? ProgressErrors.ShareOutOfRange(share) : new GoalThreshold(share);

    /// <summary>Whether <paramref name="done"/> of <paramref name="total"/> goal tasks
    /// meets the threshold. No goal task is never a met goal.</summary>
    public bool IsMet(int done, int total) => total > 0 && done * 100 >= Share * total;
}

/// <summary>
/// One way a series picks its goal tasks
/// (<c>.devbook/domain/progress/domain.md#goal-rule</c>). An <see cref="GoalRuleKind.InPlans"/>
/// rule names its plan tags and a <see cref="GoalRuleKind.Tagged"/> one its task tags;
/// the other two name nothing.
/// </summary>
public sealed record GoalRule
{
    private GoalRule(GoalRuleKind kind, IReadOnlyList<string> choices)
    {
        Kind = kind;
        Choices = choices;
    }

    public GoalRuleKind Kind { get; }

    /// <summary>Plan tags without the sigil, or task tags as written; compared without
    /// regard to case.</summary>
    public IReadOnlyList<string> Choices { get; }

    public static Result<GoalRule> Create(GoalRuleKind kind, IEnumerable<string>? choices)
    {
        if (!Enum.IsDefined(kind)) return ProgressErrors.GoalRuleWithoutChoice(kind);

        if (kind is GoalRuleKind.Everything or GoalRuleKind.MarkedAsGoal) return new GoalRule(kind, []);

        var cleaned = (choices ?? [])
            .Select(choice => kind == GoalRuleKind.InPlans ? PlanTags.Bare(choice) : choice?.Trim() ?? string.Empty)
            .Where(choice => choice.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return cleaned.Count == 0 ? ProgressErrors.GoalRuleWithoutChoice(kind) : new GoalRule(kind, cleaned);
    }

    internal static GoalRule Restore(GoalRuleKind kind, IReadOnlyList<string> choices) => new(kind, choices);

    public bool Equals(GoalRule? other) =>
        other is not null
        && Kind == other.Kind
        && Choices.Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(other.Choices.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Kind, Choices.Count);
}

/// <summary>Whether a series' open sprint shows in the daily brief and in the weekly
/// brief (<c>.devbook/domain/progress/domain.md#brief-visibility</c>).</summary>
public sealed record BriefVisibility(bool Daily, bool Weekly)
{
    public static readonly BriefVisibility Neither = new(false, false);
}

/// <summary>Plan tags as Progress compares them: the slug without the <c>+</c> sigil, so
/// <c>+progress-sprints</c> and <c>progress-sprints</c> name the same plan.</summary>
public static class PlanTags
{
    public static string Bare(string? tag)
    {
        var trimmed = tag?.Trim() ?? string.Empty;
        return trimmed.StartsWith('+') ? trimmed[1..].Trim() : trimmed;
    }

    public static bool Same(string? left, string? right) =>
        string.Equals(Bare(left), Bare(right), StringComparison.OrdinalIgnoreCase);
}
