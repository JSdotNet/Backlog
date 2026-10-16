using Backlog.Mobile.UI.AppUpdate;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="IAppUpdateCheckStamp"/> in the app's preferences, so the quiet update
/// check stays eight hours apart across launches rather than only within one.
/// </summary>
public sealed class PreferencesAppUpdateCheckStamp(IPreferences preferences) : IAppUpdateCheckStamp
{
    private const string Key = "app-update.last-checked";

    public DateTimeOffset? LastChecked
    {
        get => preferences.ContainsKey(Key)
            ? DateTimeOffset.FromUnixTimeMilliseconds(preferences.Get(Key, 0L))
            : null;
        set
        {
            if (value is null)
            {
                preferences.Remove(Key);
            }
            else
            {
                preferences.Set(Key, value.Value.ToUnixTimeMilliseconds());
            }
        }
    }
}
