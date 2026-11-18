using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Calendar.Abstractions.Services;

/// <summary>
/// The Plans layer port the Calendar owns (local ADR 0055 §2): the roadmap's plans as the
/// Calendar draws them — each planned window, each milestone, and the plans still on the
/// roadmap's shelf — and the one thing the Calendar does to them, starting a shelf plan's
/// window on a day.
/// <para>
/// A port on the Calendar's own surface rather than a reference to Roadmap Planning, the
/// shape <c>IRoadmapPlanIntake</c> takes: this module asks its own port, and an
/// infrastructure adapter that may see both contexts answers it through Roadmap's own
/// abstractions — never its storage. The windows are the ones the roadmap draws, and a
/// window started here is placed by the roadmap's own import placement, so nothing in the
/// Calendar sizes a window. Whether the plans are shown is the Calendar's own view
/// choice, not this port's.
/// </para>
/// </summary>
public interface ICalendarPlans
{
    /// <summary>Raised after the plan changed, whichever caller changed it. It may arrive
    /// on any thread.</summary>
    event Action? Changed;

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
