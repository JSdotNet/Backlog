using Backlog.Mobile.UI.Notifications;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="INotificationFilterStore"/> in the app's preferences, so the From and Repository
/// choices a person made on the Notifications tab are still in force after the app is closed.
/// </summary>
/// <remarks>
/// Each choice is kept by its own key as the text the filter compares, an empty one meaning
/// All; a choice that no longer matches anything is cleared on screen with Clear filters.
/// </remarks>
public sealed class PreferencesNotificationFilterStore(IPreferences preferences) : INotificationFilterStore
{
    private const string FromKey = "notifications.filter.from";

    private const string RepositoryKey = "notifications.filter.repository";

    public NotificationFilter Current
    {
        get => new(Read(FromKey), Read(RepositoryKey));
        set
        {
            var filter = value ?? NotificationFilter.None;
            preferences.Set(FromKey, filter.From ?? string.Empty);
            preferences.Set(RepositoryKey, filter.Repository ?? string.Empty);
        }
    }

    private string? Read(string key) =>
        preferences.Get(key, string.Empty) is { Length: > 0 } value ? value : null;
}
