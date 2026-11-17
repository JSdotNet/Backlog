namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The person confirming, with Windows Hello, an act that is hard to undo (local ADR 0052 §6):
/// allowing an agent's grant first. A port, because what asks is the host's: the desktop head
/// asks <c>UserConsentVerifier</c>, which a script or UI automation cannot pass, and the web
/// harness has nothing to ask with and answers <see cref="UserConsentOutcome.NotAvailable"/>.
/// </summary>
public interface IUserConsent
{
    /// <summary>Asks the person to confirm, showing <paramref name="message"/>, and answers how
    /// it ended. Never throws for a person who cancels or a device with no Hello: those are
    /// outcomes.</summary>
    Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default);
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
}
