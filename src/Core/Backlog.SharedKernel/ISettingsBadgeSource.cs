namespace Backlog.SharedKernel;

/// <summary>
/// Where a module's settings page gets the short value the settings menu draws on
/// the right of its name - a count or a state.
/// <para>
/// A <see cref="SettingsSection"/> names the type through
/// <see cref="SettingsSection.BadgeSource"/>, and the shell resolves it from the
/// container, so a module supplies its page's badge without the shell knowing
/// what it counts. The shell reads <see cref="Badge"/> each time it draws the menu
/// and redraws the menu when <see cref="Changed"/> is raised; it may be raised on
/// any thread. The shell resolves a source once and holds it while the settings
/// screen is open, so register it as a singleton.
/// </para>
/// </summary>
public interface ISettingsBadgeSource
{
    /// <summary>The value to draw, or null or blank for none.</summary>
    string? Badge { get; }

    /// <summary>Raised after <see cref="Badge"/> may have changed.</summary>
    event Action? Changed;
}
