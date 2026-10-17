using System.Globalization;

namespace Backlog.UI.Components.PhoneStandup;

/// <summary>
/// Whether a goal lands in time, in the two words its forecast badge prints
/// (<c>.devbook/design/color-scheme.md#goal-forecast-badges</c>). The library's own: it
/// references no module, so a host maps the Progress module's standing onto this.
/// </summary>
public enum StandupStanding
{
    /// <summary>It lands on or before its due date.</summary>
    OnTrack,

    /// <summary>It lands late, has no landing date, or an open impediment holds it back.</summary>
    AtRisk
}

/// <summary>
/// Where an impediment stands, in the order it moves
/// (<c>.devbook/domain/progress/domain.md#impediment-state</c>), as the library draws it
/// (<c>.devbook/design/color-scheme.md#impediment-state-tones</c>). The library's own, for
/// the reason <see cref="StandupStanding"/> is.
/// </summary>
public enum StandupImpedimentState
{
    New,
    Reported,
    Escalated,
    BeingHandled,
    Resolved
}

/// <summary>One step an impediment took: the state it moved to, the day, and who it was
/// with at that step, if anyone.</summary>
public sealed record StandupImpedimentStep(StandupImpedimentState State, DateOnly On, string? With);

/// <summary>The items of one goal in a stand-up block, or of no goal when
/// <paramref name="GoalTitle"/> is null.</summary>
public sealed record StandupWorkGroup<TItem>(string? GoalTitle, IReadOnlyList<TItem> Items);

/// <summary>The words and slugs the phone's stand-up parts share, so a state reads the
/// same on its badge, its step and in a test.</summary>
public static class StandupWords
{
    /// <summary>Every state, in the order an impediment moves through them.</summary>
    public static IReadOnlyList<StandupImpedimentState> States { get; } =
    [
        StandupImpedimentState.New,
        StandupImpedimentState.Reported,
        StandupImpedimentState.Escalated,
        StandupImpedimentState.BeingHandled,
        StandupImpedimentState.Resolved
    ];

    /// <summary>The state as a person reads it: "Being handled" for
    /// <see cref="StandupImpedimentState.BeingHandled"/>.</summary>
    public static string Label(StandupImpedimentState state) => state switch
    {
        StandupImpedimentState.BeingHandled => "Being handled",
        _ => state.ToString()
    };

    /// <summary>The state as a class modifier: <c>being-handled</c>.</summary>
    public static string Slug(StandupImpedimentState state) => state switch
    {
        StandupImpedimentState.BeingHandled => "being-handled",
        _ => state.ToString().ToLowerInvariant()
    };

    /// <summary>The standing as a person reads it.</summary>
    public static string Label(StandupStanding standing) => standing == StandupStanding.AtRisk ? "At risk" : "On track";

    /// <summary>The standing as a class modifier.</summary>
    public static string Slug(StandupStanding standing) => standing == StandupStanding.AtRisk ? "at-risk" : "on-track";

    /// <summary>
    /// How a goal's items stand, in the words a screen reader hears for the bar that
    /// draws them: "9 of 15 done, 3 in progress, 1 blocked, 2 not started". An open part
    /// with no items is left out; a goal counting nothing says so.
    /// </summary>
    public static string Progress(int done, int doing, int blocked, int toDo)
    {
        var total = done + doing + blocked + toDo;
        if (total <= 0) return "Nothing counted yet";

        var parts = new List<string>(4) { $"{done} of {total} done" };
        if (doing > 0) parts.Add($"{doing} in progress");
        if (blocked > 0) parts.Add($"{blocked} blocked");
        if (toDo > 0) parts.Add($"{toDo} not started");

        return string.Join(", ", parts);
    }

    /// <summary>A day as the stand-up prints it: "30 Oct".</summary>
    public static string Day(DateOnly day) => day.ToString("d MMM", CultureInfo.InvariantCulture);

    /// <summary>A step's day as the state list prints it: "Wed 14".</summary>
    public static string StepDay(DateOnly day) => day.ToString("ddd d", CultureInfo.InvariantCulture);
}
