using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>Where the sessions part's week boundary came from.</summary>
public enum WeekSource
{
    /// <summary>No reset is known: weeks run from Monday midnight on the local clock,
    /// labelled by ISO week number.</summary>
    Calendar,

    /// <summary>The reset the assistant's own records last reported — the instant a
    /// weekly refusal said the allowance would reset.</summary>
    Detected,

    /// <summary>The reset the person configured in Settings, which outranks what was
    /// detected: the records may be from another plan or another month.</summary>
    Configured
}

/// <summary>
/// How the sessions part is cutting its weeks, so the surface can say so beside the
/// columns rather than leaving a reader to wonder why W34 became "24 Aug".
/// </summary>
/// <param name="Source">Which boundary is in force.</param>
/// <param name="ResetAt">The reset as a person would say it on the local clock —
/// "Monday 14:00" — or null under the calendar fallback.</param>
public sealed record UsageWeekInfo(WeekSource Source, string? ResetAt);

/// <summary>Refusals inside the window, by allowance.</summary>
/// <param name="FiveHour">Against the rolling five-hour window.</param>
/// <param name="Weekly">Against the weekly allowance.</param>
public sealed record LimitHitCounts(int FiveHour, int Weekly)
{
    public int Total => FiveHour + Weekly;
}

/// <summary>
/// One moment the assistant refused a request against an allowance, placed on the
/// cell of a week's grid it happened in.
/// </summary>
/// <param name="Day">The local date of the hour.</param>
/// <param name="Hour">The local hour of the cell, 0–23.</param>
/// <param name="Kind">Which allowance.</param>
/// <param name="At">When.</param>
/// <param name="ResetsAt">When the assistant said the allowance would reset.</param>
public sealed record LimitMark(DateOnly Day, int Hour, AssistantLimitKind Kind, DateTimeOffset At, DateTimeOffset ResetsAt)
{
    /// <summary>
    /// The last cell of this week the refusal kept walled out — the hour its reset
    /// fell in, or the week's last hour when the reset is past the week — as a local
    /// date and hour on the same terms as <see cref="Day"/> and <see cref="Hour"/>.
    /// Null for a weekly refusal: its reset is the next week's start and marking
    /// every hour up to it would paint the whole grid.
    /// </summary>
    public DateOnly? UntilDay { get; init; }

    public int? UntilHour { get; init; }
}

/// <summary>
/// One week of the sessions part as three grids and a row of totals: seven 24-hour rows
/// from one reset to the next, every hour present, and the allowance refusals that fell
/// inside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rows are calendar days, cut at the reset hour.</b> With a reset at Monday 14:00 the
/// first row is Monday from 14:00 and the last is the following Monday up to 13:00 —
/// eight rows, the first and last partial. The hours outside the week are not in
/// <paramref name="Hours"/> at all, and a grid draws an hour it was not given as not
/// reported, which is the honest reading: those hours belong to the neighbouring weeks.
/// Under the calendar fallback the reset hour is midnight and there are seven full rows.
/// </para>
/// <para>
/// Every hour inside the week is present — 168 of them: an hour nobody worked is a zero
/// and never a gap, on <see cref="AssistantSessionsInsight.ActivityByHour"/>'s rule.
/// </para>
/// </remarks>
/// <param name="Key">The week's key, the same one its column carries.</param>
/// <param name="Label">The week's label, the same one its column carries.</param>
/// <param name="StartsAt">The reset instant the week began at.</param>
/// <param name="Hours">168 cells, in clock order from the week's first hour.</param>
/// <param name="Days">One row of distinct-session counts per calendar day the week
/// touches — seven, or eight when the reset is not at midnight — on
/// <see cref="ActivityDay"/>'s terms.</param>
/// <param name="Limits">The refusals inside the week, oldest first.</param>
public sealed record WeekGrid(
    string Key,
    string Label,
    DateTimeOffset StartsAt,
    IReadOnlyList<ActivityHour> Hours,
    IReadOnlyList<ActivityDay> Days,
    IReadOnlyList<LimitMark> Limits);
