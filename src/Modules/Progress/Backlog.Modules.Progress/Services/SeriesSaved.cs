namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The one place a saved sprint series is announced: every command that saves a series
/// raises it once the save is done and the gate let go, and a pulled copy raises it too.
/// Two listen: <see cref="SprintSeriesReplication"/>, which forwards a person's change to
/// the sync loop, and <see cref="SprintSeriesPlanning.Changed"/>, the published notice
/// every sprint screen re-reads on — so a series pulled from another device redraws them.
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
