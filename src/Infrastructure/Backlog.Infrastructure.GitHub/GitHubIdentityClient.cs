using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Who GitHub thinks we are.
/// </summary>
/// <remarks>
/// Its own client rather than a method on <see cref="IGitHubClient"/> because the
/// two things that need it — filtering activity to the person's own work, and
/// choosing between the user and organization billing endpoints — are not issue
/// operations, and because being able to fake the login on its own is what makes
/// both of those testable.
/// <para>
/// This exists so that neither of those needs a setting. A login typed into
/// Settings is a login that goes stale, and one that quietly points the dashboard
/// at somebody else's work if it is ever mistyped.
/// </para>
/// </remarks>
public interface IGitHubIdentityClient
{
    /// <summary>The authenticated account's login, or null when GitHub cannot be
    /// reached or is not authenticated.</summary>
    Task<string?> GetLoginAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IGitHubIdentityClient"/>
public sealed class GitHubIdentityClient(IGitHubTransport transport) : IGitHubIdentityClient
{
    /// <summary>
    /// Cached for the lifetime of the client. The signed-in account does not change
    /// while the app is open, and the alternative is a round trip in front of every
    /// activity fetch.
    /// </summary>
    private string? _login;

    private bool _asked;

    public async Task<string?> GetLoginAsync(CancellationToken cancellationToken = default)
    {
        if (_asked) return _login;

        try
        {
            var response = await transport
                .SendAsync(HttpMethod.Get, "user", body: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _login = response.ValueKind == JsonValueKind.Object
                && response.TryGetProperty("login", out var login)
                && login.ValueKind == JsonValueKind.String
                    ? login.GetString()
                    : null;
        }
        catch (GitHubException)
        {
            _login = null;
        }
        catch (GitHubNotConfiguredException)
        {
            _login = null;
        }

        _asked = true;

        return _login;
    }
}
