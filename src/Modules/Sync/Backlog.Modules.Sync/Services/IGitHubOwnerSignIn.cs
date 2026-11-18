namespace Backlog.Modules.Sync.Services;

/// <summary>
/// Turns the authorization code GitHub sent back into the numeric id of the GitHub
/// user who signed in (local ADR 0052 §1). Declared here and implemented by the
/// host, which holds the OAuth App's client secret — the same split as
/// <see cref="IDeviceTokenIssuer"/>.
/// </summary>
public interface IGitHubOwnerSignIn
{
    /// <summary>The signed-in user's numeric id, or null when GitHub refused the
    /// code or did not answer. Never the login name: a login can be renamed and
    /// then taken by someone else.</summary>
    Task<long?> ResolveUserIdAsync(string authorizationCode, CancellationToken cancellationToken = default);

    /// <summary>The GitHub page that signs the owner in and comes back to the
    /// service's callback carrying <paramref name="state"/>.</summary>
    Uri AuthorizeAddress(string state);
}

/// <summary>
/// The stand-in when the host composed no GitHub sign-in: nobody signs in, so no
/// sign-in can succeed. A Development service with no owner configured never asks
/// it; a deployed one refuses to start without the real one.
/// </summary>
public sealed class NoGitHubOwnerSignIn : IGitHubOwnerSignIn
{
    public Task<long?> ResolveUserIdAsync(string authorizationCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(null);

    public Uri AuthorizeAddress(string state) =>
        throw new InvalidOperationException("No GitHub sign-in is configured on this sync service.");
}
