namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// The one place <see cref="RoadmapPlanning.Changed"/> is actually held.
/// <para>
/// A singleton behind a scoped port, because the listener and the writer are rarely in
/// the same scope: the band subscribes from its screen's, and Tasks' Import writes
/// through an adapter resolved in its own. An event kept on each scoped instance would
/// only ever be heard by the scope that raised it.
/// </para>
/// </summary>
internal sealed class RoadmapPlanChanges
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
