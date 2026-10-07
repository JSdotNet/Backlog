using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// The roadmap's plans as the Tasks Calendar draws them: each planned window, each
/// milestone, and the plans still on the roadmap's shelf — and the one thing the Calendar
/// does to them, starting a shelf plan's window on a day.
/// <para>
/// A port on Tasks' own surface rather than a reference to Roadmap Planning, the shape
/// <see cref="IRoadmapPlanIntake"/> takes: this module asks its own port, and an
/// infrastructure adapter that may see both contexts answers it through Roadmap's own
/// abstractions — never its storage. The windows are the ones the roadmap draws, and a
/// window started here is placed by the roadmap's own import placement, so nothing in
/// Tasks sizes a window.
/// </para>
/// </summary>
public interface ICalendarPlans
{
    /// <summary>Raised after the plan changed, whichever caller changed it. It may arrive
    /// on any thread.</summary>
    event Action? Changed;

    /// <summary>Whether the Calendar draws the plans — its "Show plans" box. On until the
    /// reader turns it off on this device.</summary>
    bool Shown { get; }

    /// <summary>Remembers the "Show plans" box on this device. A choice the host cannot
    /// keep for next time still holds for this run.</summary>
    void SetShown(bool shown);

    /// <summary>The plan as it reads on <paramref name="today"/>: the windows the roadmap
    /// would draw today, its milestones, and its shelf.</summary>
    Task<CalendarPlansDto> ReadAsync(DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the shelf plan tagged <paramref name="tag"/> on the roadmap with its window
    /// opening on <paramref name="start"/>, its end counted by the roadmap from the plan's
    /// points and the pace in use. Answers the refusal in words when the roadmap refused,
    /// or null.
    /// </summary>
    Task<string?> StartAsync(string tag, DateOnly start, CancellationToken cancellationToken = default);
}
