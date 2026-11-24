using Backlog.Modules.Sync.Services;

namespace Backlog.Tests;

/// <summary>
/// GitHub's owner sign-in as the sync service's tests see it: <see cref="OwnerCode"/>
/// signs in the owner, <see cref="StrangerCode"/> someone else, and any other code
/// nobody.
/// </summary>
internal sealed class StubGitHubOwnerSignIn : IGitHubOwnerSignIn
{
    /// <summary>The owner's numeric GitHub id.</summary>
    public const long OwnerGitHubId = 7655166;

    /// <summary>The code GitHub sends back when the owner signed in.</summary>
    public const string OwnerCode = "code-from-the-owner";

    /// <summary>The code GitHub sends back when someone other than the owner signed in.</summary>
    public const string StrangerCode = "code-from-someone-else";

    public Task<long?> ResolveUserIdAsync(string authorizationCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(authorizationCode switch
        {
            OwnerCode => OwnerGitHubId,
            StrangerCode => 1,
            _ => null,
        });

    public Uri AuthorizeAddress(string state) => new($"https://github.test/login/oauth/authorize?state={Uri.EscapeDataString(state)}");
}
