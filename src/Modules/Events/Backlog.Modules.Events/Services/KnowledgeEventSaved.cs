namespace Backlog.Modules.Events.Services;

/// <summary>
/// The one place a person's change to an event or a series is announced: every service that
/// saves one — the pane, the To Event route, the Harvest Task Policy, an applied attachment
/// request — raises it after the save. <see cref="KnowledgeEventReplication"/> forwards it as
/// <see cref="Abstractions.Services.IKnowledgeEventReplication.Changed"/>, so the sync loop
/// pushes the change within seconds instead of on its next tick. A copy taken from another
/// device is never announced. A singleton, because the pane and the policy run in scopes of
/// their own.
/// </summary>
internal sealed class KnowledgeEventSaved
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
