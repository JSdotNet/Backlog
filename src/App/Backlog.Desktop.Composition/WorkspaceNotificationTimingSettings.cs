using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.Composition;

/// <summary>
/// The Notifications settings page's timings, answered over the workspace settings file
/// where the Sessions context's <see cref="PresenceSettings"/> are kept. The host joins the
/// two contexts here because neither may see the other (<c>ModuleBoundaryTests</c>).
/// </summary>
public sealed class WorkspaceNotificationTimingSettings : INotificationTimingSettings, IDisposable
{
    private readonly WorkspaceSettingsStore _store;

    public WorkspaceNotificationTimingSettings(WorkspaceSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _store.PresenceChanged += OnPresenceChanged;
    }

    public TimeSpan AwayAfter => _store.Presence.AwayAfter;

    public TimeSpan NotifyAfter => _store.Presence.NotifyAfter;

    public TimeSpan DialogWait => _store.Presence.DialogWait;

    public event Action? Changed;

    public string? Set(TimeSpan awayAfter, TimeSpan notifyAfter, TimeSpan dialogWait) =>
        _store.SetPresence(new PresenceSettings(awayAfter, notifyAfter, dialogWait));

    private void OnPresenceChanged() => Changed?.Invoke();

    public void Dispose() => _store.PresenceChanged -= OnPresenceChanged;
}
