namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// The hours worked over one stretch of local dates — a day or a calendar week — split
/// by office hours, beside the hours planned (local ADR 0019, §7).
/// </summary>
/// <param name="From">The first local date, inclusive.</param>
/// <param name="Through">The last local date, inclusive. For the week today falls in,
/// today: the planned hours are the ones the week has had so far.</param>
/// <param name="Inside">The time worked inside the dates' office hours.</param>
/// <param name="Outside">The time worked outside them, days off included.</param>
/// <param name="Planned">The working hours of the dates, overrides applied.</param>
public sealed record HoursWorkedPeriod(
    DateOnly From,
    DateOnly Through,
    TimeSpan Inside,
    TimeSpan Outside,
    TimeSpan Planned)
{
    /// <summary>Every hour worked, inside office hours and outside them: the roadmap's
    /// actual figure for the same dates.</summary>
    public TimeSpan Actual => Inside + Outside;
}

/// <summary>
/// How long the person worked over the dashboard's window, per day and per calendar week
/// (Monday first), and how much of it fell outside the hours they meant to work.
/// </summary>
/// <param name="Days">One entry per local date of the window, oldest first, through
/// today.</param>
/// <param name="Weeks">One entry per calendar week of the window, oldest first: the sum of
/// its days. The last is the week today falls in, counted through today.</param>
public sealed record HoursWorkedInsight(
    IReadOnlyList<HoursWorkedPeriod> Days,
    IReadOnlyList<HoursWorkedPeriod> Weeks)
{
    /// <summary>The time worked inside office hours over the whole window.</summary>
    public TimeSpan Inside => Sum(period => period.Inside);

    /// <summary>The time worked outside office hours over the whole window.</summary>
    public TimeSpan Outside => Sum(period => period.Outside);

    /// <summary>The hours planned over the whole window.</summary>
    public TimeSpan Planned => Sum(period => period.Planned);

    /// <summary>Every hour worked over the whole window.</summary>
    public TimeSpan Actual => Inside + Outside;

    private TimeSpan Sum(Func<HoursWorkedPeriod, TimeSpan> of) =>
        TimeSpan.FromTicks(Days.Sum(period => of(period).Ticks));
}
