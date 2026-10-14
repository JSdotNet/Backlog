namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The one place a saved sprint series is announced: every command that saves a series
/// raises it once the save is done and the gate let go, and a pulled copy raises it too.
/// Today the only listener is <see cref="SprintSeriesReplication"/>, which forwards a
/// person's change to the sync loop; a screen that has to redraw on a pulled series
/// listens here as well.
/// <para>
/// A singleton, for the reason Roadmap's <c>RoadmapPlanChanges</c> is one: the commands
/// run in scopes of their own, and an event kept on a scoped instance would only ever be
/// heard by the scope that raised it.
/// </para>
/// </summary>
public sealed class SeriesSaved
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
