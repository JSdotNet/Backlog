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
/// Picks how to talk to GitHub, per call.
/// <para>
/// For an unbound path — the great majority, and everything an install that never
/// opens the Accounts panel ever sends — the order is what it has always been: the
/// <c>gh</c> CLI when it is signed in, otherwise a configured token. The CLI comes
/// first on purpose, because it means the common case needs no secret in this app
/// at all.
/// </para>
/// <para>
/// A path <em>bound</em> to an account skips that order entirely and goes out over
/// HTTP with that account's credential. It has to: <c>gh api</c> has no per-call
/// account selector, so the CLI can only ever speak as whoever <c>gh</c> is
/// currently switched to, and letting it answer for a bound path is precisely how a
/// call for one owner's repository left as another owner and came back a 404.
/// </para>
/// </summary>
public sealed class ResolvingGitHubTransport : IGitHubTransport, IGitHubConnectionProbe
{
    private readonly GhCliTransport _cli;
    private readonly TokenTransport _token;
    private readonly IGitHubCredentialResolver _credentials;
    private readonly IGhCliAccountSource _accounts;

    public ResolvingGitHubTransport(
        GitHubSettingsStore settings,
        GhCliTransport? cli = null,
        TokenTransport? token = null,
        IGitHubCredentialResolver? credentials = null,
        IGhCliAccountSource? accounts = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _accounts = accounts ?? new GhCliAccountSource();
        _cli = cli ?? new GhCliTransport();

        // One resolver, not two. Routing here and the credential the token
        // transport actually sends have to be the same answer, or a bound path
        // could be routed on one opinion and authenticated on another.
        _credentials = credentials ?? token?.Credentials ?? new GitHubCredentialResolver(settings, _accounts);
        _token = token ?? new TokenTransport(_credentials, () => settings.Current.ApiEndpoint);
    }

    public string Description => "GitHub CLI, or a personal access token";

    /// <summary>
    /// Whether this machine can reach GitHub at all — the pathless question, which
    /// is a different one from "which credential authenticates this path".
    /// <para>
    /// It used to be asked by resolving a null path, and the only answer that could
    /// reach was the cross-repository token fallback. Asking it directly is what
    /// lets that fallback be deleted without a machine that has no <c>gh</c> but a
    /// working repository token being told it cannot reach GitHub.
    /// </para>
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        await _cli.IsAvailableAsync(cancellationToken) || _credentials.HasAnyCredential;

    /// <summary>Re-checks the CLI and forgets any token extracted from it, so a
    /// <c>gh auth login</c>, a <c>gh auth logout</c> or a rotated credential is
    /// noticed while the app is open.</summary>
    public void Invalidate()
    {
        _cli.Invalidate();
        _accounts.Invalidate();
    }

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
        var transport = await ResolveForAsync(path, cancellationToken)
            ?? throw new GitHubNotConfiguredException(
                "No way to reach GitHub. Sign in with `gh auth login`, or add a personal access token in repository settings.");

        return await transport.SendAsync(method, path, body, apiVersion, cancellationToken);
    }

    /// <summary>
    /// Which transport carries one call.
    /// <para>
    /// The credential is resolved before the CLI is even asked, because whether the
    /// path names an identity is the thing that decides. A bound path goes over
    /// HTTP no matter what the CLI could do; an unbound one keeps the order it has
    /// always had, and a repository's own token stays underneath the CLI exactly as
    /// its own copy in Settings says it does.
    /// </para>
    /// <para>
    /// The token transport resolves again when it sends. That is one repeated
    /// lookup over configuration already in memory, or one cache hit against a token
    /// fetched moments earlier — cheap enough to be worth the two types each
    /// answering for themselves rather than passing a credential between them.
    /// </para>
    /// </summary>
    private async Task<IGitHubTransport?> ResolveForAsync(string? path, CancellationToken cancellationToken)
    {
        var credential = await _credentials.ResolveAsync(path, cancellationToken);

        if (credential is { IsBound: true }) return _token;
        if (await _cli.IsAvailableAsync(cancellationToken)) return _cli;
        if (credential is not null || _credentials.HasAnyCredential) return _token;

        return null;
    }
}
