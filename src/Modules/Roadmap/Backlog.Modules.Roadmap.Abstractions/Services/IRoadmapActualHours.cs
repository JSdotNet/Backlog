namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// How long agents were active on each local date — the actual hours a day or week
/// head shows beside its planned hours (ADR 0019).
/// <para>
/// A port on Roadmap Planning's own surface, answered by an infrastructure adapter
/// over the Sessions context's agent activity — the join a screen may not make for
/// itself (<c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>).
/// The axis renders one context and asks this; the adapter reads the sessions.
/// </para>
/// <para>
/// The figure is the union of every agent session's active stretches, so two
/// sessions running at once count that time once. A stretch that crosses local
/// midnight counts toward each date for its own part. A wait, in which the agent had
/// stopped, is not active time.
/// </para>
/// <para>
/// Presentation only. The actual hours move no bar and change no pace; a pace is
/// still measured from finished points.
/// </para>
/// </summary>
public interface IRoadmapActualHours
{
    /// <summary>
    /// The agent-active time on each local date from <paramref name="from"/> through
    /// <paramref name="through"/>, both included.
    /// <para>
    /// Only a date with some active time is present, so a date missing from the map
    /// is a date nobody worked. A date after today is never answered, and today counts
    /// up to now.
    /// </para>
    /// <para>
    /// <see langword="null"/> when this host cannot state the actual hours at all: it
    /// composed no Sessions activity, or the Sessions area is switched off. That is a
    /// different answer from an empty map, which says the hours were read and came to
    /// nothing. A read that fails throws, and the heads show their planned hours alone
    /// for a null and a throw alike.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<DateOnly, TimeSpan>?> ReadAsync(
        DateOnly from,
        DateOnly through,
        CancellationToken cancellationToken = default);
}
