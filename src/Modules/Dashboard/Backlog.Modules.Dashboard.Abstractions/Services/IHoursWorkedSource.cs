namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// One local date's hours worked, split by that date's office hours (local ADR 0019,
/// §7).
/// </summary>
/// <param name="Date">The local date.</param>
/// <param name="Inside">The part of the person's working stretches on this date that
/// fell inside its office hours: the weekday's start to its end, with the day overrides
/// applied. Zero on a date with no office hours.</param>
/// <param name="Outside">The rest of the stretches on this date. Inside plus outside is
/// the actual figure the roadmap's head shows for the date.</param>
/// <param name="Planned">The date's working hours: its weekday's end minus its start
/// when the date is worked, zero when it is not.</param>
public sealed record HoursWorkedDay(DateOnly Date, TimeSpan Inside, TimeSpan Outside, TimeSpan Planned)
{
    /// <summary>Every hour worked on the date, inside office hours and outside them.</summary>
    public TimeSpan Actual => Inside + Outside;
}

/// <summary>
/// PORT — how long the person worked on each date, and how much of it fell inside the
/// hours they meant to work.
/// <para>
/// A window of dates in the signature, on <see cref="IPlanProgressSource"/>'s pattern:
/// what is read is the person's working stretches, and a stretch is only whole when the
/// turns before the window are read too, so the adapter decides how far back to look.
/// The stretches are the ones Roadmap Planning counts its actual hours from, so a date's
/// inside and outside hours always add up to the figure on the roadmap's head.
/// </para>
/// <para>
/// Nothing in this contract names a Sessions type; the adapter that answers it may see
/// both contexts, nothing in this module may.
/// </para>
/// </summary>
public interface IHoursWorkedSource
{
    /// <summary>
    /// One entry per local date from <paramref name="from"/> through
    /// <paramref name="through"/>, both inclusive, oldest first. A date with no work has
    /// zero inside and outside, and a date not yet begun counts no work but keeps its
    /// planned hours.
    /// </summary>
    /// <returns>Null when the hours cannot be stated — the host reads no agent activity,
    /// or the Sessions area is switched off — which is a different answer from a range
    /// nobody worked.</returns>
    /// <exception cref="Exception">The activity could not be read. Not caught: an empty
    /// answer would say the hours were read and came to nothing.</exception>
    Task<IReadOnlyList<HoursWorkedDay>?> ReadAsync(
        DateOnly from,
        DateOnly through,
        CancellationToken cancellationToken = default);
}
