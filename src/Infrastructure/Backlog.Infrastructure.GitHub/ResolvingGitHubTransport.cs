using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>How the app is currently able to reach GitHub — the one line
/// Settings shows so nobody has to guess whether a push will work.</summary>
public sealed record GitHubConnection(bool IsConnected, string Summary, string? Account = null);

/// <summary>Answers "can this machine reach GitHub, and as whom?" — separate
/// from <see cref="IGitHubTransport"/> because Settings asks that question
/// without wanting to send anything.</summary>
public interface IGitHubConnectionProbe
{
    Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets what was previously worked out, so signing in with
    /// <c>gh auth login</c> while the app is open is noticed.</summary>
    void Invalidate();
}

/// <summary>
/// Picks how to talk to GitHub: the <c>gh</c> CLI when it is signed in,
/// otherwise a configured token.
/// <para>
/// The CLI comes first on purpose — it means the common case needs no secret in
/// this app at all. The token exists for machines without <c>gh</c>, and is only
/// consulted when the CLI cannot answer.
/// </para>
/// </summary>
public sealed class ResolvingGitHubTransport(
    GitHubSettingsStore settings,
    GhCliTransport? cli = null,
    TokenTransport? token = null) : IGitHubTransport, IGitHubConnectionProbe
{
    private readonly GhCliTransport _cli = cli ?? new GhCliTransport();
    private readonly TokenTransport _token = token ?? new TokenTransport(settings.Current.TokenForPath, () => settings.Current.ApiEndpoint);

    public string Description => "GitHub CLI, or a personal access token";

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        await ResolveAsync(cancellationToken) is not null;

    /// <summary>Re-checks the CLI, so signing in with <c>gh auth login</c> while
    /// the app is open is noticed.</summary>
    public void Invalidate() => _cli.Invalidate();

    /// <summary>Describes the current connection for Settings.</summary>
    public async Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default)
    {
        if (await _cli.IsAvailableAsync(cancellationToken))
        {
            return new GitHubConnection(
                true,
                _cli.Account is null
                    ? "Connected through the GitHub CLI."
                    : $"Connected through the GitHub CLI as {_cli.Account}.",
                _cli.Account);
        }

        if (await _token.IsAvailableAsync(cancellationToken))
        {
            return new GitHubConnection(true, "Connected with a repository personal access token.");
        }

        return new GitHubConnection(
            false,
            "Not connected. Sign in with `gh auth login`, or paste a personal access token in repository settings.");
    }

    public async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var transport = await ResolveAsync(cancellationToken)
            ?? throw new GitHubNotConfiguredException(
                "No way to reach GitHub. Sign in with `gh auth login`, or add a personal access token in repository settings.");

        return await transport.SendAsync(method, path, body, apiVersion, cancellationToken);
    }

    private async Task<IGitHubTransport?> ResolveAsync(CancellationToken cancellationToken)
    {
        if (await _cli.IsAvailableAsync(cancellationToken)) return _cli;
        if (await _token.IsAvailableAsync(cancellationToken)) return _token;
        return null;
    }
}
