namespace Backlog.Desktop.UI.Shell;

/// <summary>Whether Backlog starts when the person signs in to Windows (local ADR
/// 0043 §5).</summary>
public enum StartupRegistrationState
{
    /// <summary>Backlog starts at sign-in, straight to the tray.</summary>
    Enabled,

    /// <summary>Backlog does not start at sign-in, and the app may turn it on.</summary>
    Disabled,

    /// <summary>The person turned it off in Task Manager's Startup apps; only they
    /// can turn it on again, there.</summary>
    DisabledByUser,

    /// <summary>Group policy turned it off.</summary>
    DisabledByPolicy,

    /// <summary>This process has no package identity — a Debug or Aspire build — so
    /// there is no startup task to register.</summary>
    Unavailable
}

/// <summary>
/// Starting Backlog when the person signs in: the desktop head answers it with the
/// MSIX <c>StartupTask</c>; a host with no such thing composes none, and the settings
/// page resolves it as optional.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>The registration as Windows reports it now.</summary>
    Task<StartupRegistrationState> GetStateAsync();

    /// <summary>Asks Windows to turn starting at sign-in on or off, and answers the
    /// state it ended in — which may not be the one asked for.</summary>
    Task<StartupRegistrationState> SetEnabledAsync(bool enabled);
}
