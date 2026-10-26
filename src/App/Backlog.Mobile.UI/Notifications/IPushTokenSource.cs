namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// Where this install's push token comes from (local ADR 0039 §4). The Android head answers with
/// Firebase Messaging's token; a host with no OS push — the browser harness, the tests' shell —
/// answers null through <see cref="NoPushTokenSource"/>, and nothing is registered.
/// </summary>
public interface IPushTokenSource
{
    /// <summary>The current token, or null when this host has none.</summary>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);
}

/// <summary>A host with no OS push: there is never a token to register.</summary>
public sealed class NoPushTokenSource : IPushTokenSource
{
    public Task<string?> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
