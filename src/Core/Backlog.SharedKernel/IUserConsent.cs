namespace Backlog.SharedKernel;

/// <summary>
/// The person confirming, with Windows Hello, an act that is hard to undo (local ADR 0058 §6):
/// allowing an agent's grant, switching <c>agents:grant</c> on, running a <c>tasks:admin</c>
/// tool, minting a pairing code and revoking a device. A port, because what asks is the
/// host's: the desktop head asks <c>UserConsentVerifier</c>, which a script or UI automation
/// cannot pass, and the web harness has nothing to ask with and answers
/// <see cref="UserConsentOutcome.NotAvailable"/>. In the shared kernel because the MCP
/// listener and the sync client both ask it.
/// </summary>
public interface IUserConsent
{
    /// <summary>Asks the person to confirm, showing <paramref name="message"/>, and answers how
    /// it ended. Never throws for a person who cancels or a device with no Hello: those are
    /// outcomes. The host may answer from a confirmation it remembers — the desktop's
    /// <see cref="RememberedUserConsent"/> — so use it for acts the person starts at the desk.</summary>
    Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default);

    /// <summary><see cref="VerifyAsync"/>, always shown: never answered from a remembered
    /// confirmation, and never opening a window for later checks (local ADR 0058 §6). For an act
    /// the person did not start at the desk — an agent's <c>tasks:admin</c> call, a Refuse
    /// replayed at the next start. No default body: a decorator that forgot it would otherwise
    /// forward to its own <see cref="VerifyAsync"/> and answer from a remembered confirmation.</summary>
    Task<UserConsentOutcome> VerifyEveryTimeAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>How a <see cref="IUserConsent.VerifyAsync"/> ended.</summary>
public enum UserConsentOutcome
{
    /// <summary>The person confirmed: face, fingerprint or PIN.</summary>
    Verified,

    /// <summary>The person cancelled, or the check failed.</summary>
    Declined,

    /// <summary>Nothing here can ask: no Windows Hello set up, or a host with none.</summary>
    NotAvailable
}

/// <summary>
/// <see cref="IUserConsent"/> for a host that has no Windows Hello to ask — the web harness: it
/// answers <see cref="UserConsentOutcome.NotAvailable"/> every time, so an Allow there is
/// refused rather than granted unconfirmed.
/// </summary>
public sealed class UnavailableUserConsent : IUserConsent
{
    /// <inheritdoc />
    public Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default) =>
        Task.FromResult(UserConsentOutcome.NotAvailable);

    /// <inheritdoc />
    public Task<UserConsentOutcome> VerifyEveryTimeAsync(string message, CancellationToken cancellationToken = default) =>
        Task.FromResult(UserConsentOutcome.NotAvailable);
}
