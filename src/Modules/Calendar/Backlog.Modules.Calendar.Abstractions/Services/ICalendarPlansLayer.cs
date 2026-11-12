using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Calendar.Abstractions.Services;

/// <summary>
/// The Plans layer as the Calendar reads it (local ADR 0055 §3): the last read of
/// <see cref="ICalendarPlans"/>, read again only when one is owed — on arriving, after the
/// plan, its work or the pace changed, after a feature switched, on a new day, and when
/// Show plans is ticked — never on every render, because a read gathers the backlog per
/// plan. One per window.
/// </summary>
public interface ICalendarPlansLayer
{
    /// <summary>Raised when a read is owed: the port said the plans changed, or a feature
    /// switched. It may arrive on any thread.</summary>
    event Action? Changed;

    /// <summary>Whether the host composed a Plans port at all.</summary>
    bool Offered { get; }

    /// <summary>The plans as last read: <see cref="CalendarPlansDto.Empty"/> before the
    /// first read and after one that failed.</summary>
    CalendarPlansDto Plans { get; }

    /// <summary>Offered, and the last read found a roadmap
    /// (<see cref="CalendarPlansDto.Available"/>).</summary>
    bool Available { get; }

    /// <summary>The Calendar arrived on screen: the next <see cref="ReadIfOwedAsync"/> reads.</summary>
    void Arrive();

    /// <summary>Reads when one is owed or the day moved on since the last read.</summary>
    Task ReadIfOwedAsync(DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Reads now, owed or not.</summary>
    Task ReadAsync(DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Starts the shelf plan tagged <paramref name="tag"/> on
    /// <paramref name="start"/> through the port: the refusal in words, or null. Null with
    /// no port.</summary>
    Task<string?> StartAsync(string tag, DateOnly start, CancellationToken cancellationToken = default);
}
