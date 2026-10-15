namespace Backlog.Modules.Progress.UI.InProgress;

/// <summary>
/// A request from outside the In progress tab to open the sprint view on one sprint of
/// one series — the weekly brief's "Open Sprint n"
/// (<c>.devbook/domain/progress/features.md#sprints-in-the-briefs</c>). A class rather
/// than a value: each press is a new request, so pressing the same sprint twice opens
/// it twice.
/// </summary>
/// <param name="seriesId">The series whose sprint view opens.</param>
/// <param name="number">The sprint it opens on.</param>
public sealed class SprintViewRequest(Guid seriesId, int number)
{
    public Guid SeriesId { get; } = seriesId;

    public int Number { get; } = number;
}
