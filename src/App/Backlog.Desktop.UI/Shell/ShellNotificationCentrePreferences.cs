using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Notifications.UI;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The notification centre's Group by, filters and Show settled switch, kept per device in the shell's own
/// navigation file beside the tools pane's tab and the Board's columns — the centre's
/// <see cref="INotificationCentrePreferences"/>. The file keeps each by the centre's slug;
/// the centre reads a slug it does not know as the default. Show settled is kept only while on,
/// so hidden — the default — reads back as no choice.
/// </summary>
public sealed class ShellNotificationCentrePreferences(ShellNavigationStore store) : INotificationCentrePreferences
{
    private readonly ShellNavigationStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public NotificationCentreChoices Choices => new(
        _store.NotificationsGroupBy,
        _store.NotificationsAskedBy,
        _store.NotificationsKind,
        _store.NotificationsFrom,
        _store.NotificationsShowSettled ? true : null);

    public void Remember(NotificationCentreChoices choices)
    {
        ArgumentNullException.ThrowIfNull(choices);

        _store.SetNotificationsView(choices.GroupBy, choices.AskedBy, choices.Kind, choices.From, choices.ShowSettled ?? false);
    }
}
