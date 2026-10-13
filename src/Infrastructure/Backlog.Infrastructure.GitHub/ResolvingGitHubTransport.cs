using System.Net;
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

/// <summary>What "Test this account" found for one GitHub account: one sentence,
/// and whether it is good news.</summary>
public sealed record GitHubAccountCheck(bool Passed, string Summary);

/// <summary>
/// Answers "does this account's credential really authenticate as this account?"
/// - the one thing a card cannot tell by looking at itself. A token is a token
/// until GitHub says whose it is, and a <c>gh</c> login is a name until the CLI
/// hands over a token for it.
/// <para>
/// Separate from <see cref="IGitHubConnectionProbe"/>, whose question is
/// machine-wide, because thirty-odd test doubles implement that one and this
/// question is only ever asked from the account card.
/// </para>
/// </summary>
public interface IGitHubAccountProbe
{
    Task<GitHubAccountCheck> CheckAccountAsync(GitHubAccount account, CancellationToken cancellationToken = default);
}

/// <summary>
/// Picks how to talk to GitHub, per call.
/// <para>
/// For an unbound path — the great majority, and everything an install that never
/// opens the Accounts panel ever sends — the order is what it has always been: the
/// <c>gh</c> CLI's identity when it is signed in, otherwise a configured token. The
/// CLI comes first on purpose, because it means the common case needs no secret
/// stored in this app at all. Since 2026-10 that identity travels over HTTP on the
/// token the CLI hands over, held in memory only, rather than as a <c>gh api</c>
/// process per call; <c>gh api</c> remains the fallback.
/// </para>
/// <para>
/// A path <em>bound</em> to an account skips that order entirely and goes out over
/// HTTP with that account's credential. It has to: <c>gh api</c> has no per-call
/// account selector, so the CLI can only ever speak as whoever <c>gh</c> is
/// currently switched to, and letting it answer for a bound path is precisely how a
/// call for one owner's repository left as another owner and came back a 404.
/// </para>
/// </summary>
public sealed class ResolvingGitHubTransport : IGitHubTransport, IGitHubConnectionProbe, IGitHubAccountProbe
{
    /// <summary>The host the CLI's token is borrowed for — the only one the HTTP
    /// route sends it to.</summary>
    private const string GitHubDotCom = "github.com";

    private readonly GitHubSettingsStore _settings;
    private readonly GhCliTransport _cli;
    private readonly TokenTransport _token;
    private readonly IGitHubCredentialResolver _credentials;
    private readonly IGhCliAccountSource _accounts;

    public ResolvingGitHubTransport(
        GitHubSettingsStore settings,
        GhCliTransport? cli = null,
        TokenTransport? token = null,
        IGitHubCredentialResolver? credentials = null,
        IGhCliAccountSource? accounts = null,
        IHttpClientFactory? httpClients = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _cli = cli ?? new GhCliTransport();

        // Over the same executable as the CLI it probes, so the token borrowed for
        // the CLI's login comes from the same gh that said who is signed in.
        _accounts = accounts ?? new GhCliAccountSource(_cli.Executable);

        // One resolver, not two. Routing here and the credential the token
        // transport actually sends have to be the same answer, or a bound path
        // could be routed on one opinion and authenticated on another.
        _credentials = credentials ?? token?.Credentials ?? new GitHubCredentialResolver(settings, _accounts);
        _token = token ?? new TokenTransport(_credentials, () => settings.Current.ApiEndpoint, httpClients: httpClients);
    }

    /// <summary>Reads one environment variable. A seam so a test can say "GH_HOST is
    /// set" without changing the environment every other test in the process reads.</summary>
    internal Func<string, string?> EnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

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
        await _cli.IsAvailableAsync(cancellationToken).ConfigureAwait(false) || _credentials.HasAnyCredential;

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
        if (await _cli.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new GitHubConnection(
                true,
                _cli.Account is null
                    ? "Connected through the GitHub CLI."
                    : $"Connected through the GitHub CLI as {_cli.Account}.",
                _cli.Account);
        }

        if (await _token.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new GitHubConnection(true, "Connected with a repository personal access token.");
        }

        return new GitHubConnection(
            false,
            "Not connected. Sign in with `gh auth login`, or paste a personal access token in repository settings.");
    }

    /// <summary>
    /// Resolves the credential the way a call bound to this account would - by a
    /// path that names the login, so the resolver's own rules apply, including its
    /// refusal to fall through to another identity - then asks <c>GET user</c> with
    /// it and holds GitHub's answer against the card.
    /// </summary>
    public async Task<GitHubAccountCheck> CheckAccountAsync(GitHubAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        GitHubCredential? credential;
        try
        {
            credential = await _credentials.ResolveAsync($"users/{account.Login}", cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubNotConfiguredException ex)
        {
            return new GitHubAccountCheck(false, ex.Message);
        }

        if (credential is not { IsBound: true })
        {
            return new GitHubAccountCheck(false, $"No credential on this machine is bound to '{account.Login}'.");
        }

        JsonElement user;
        try
        {
            user = await _token.SendAsAsync(credential, HttpMethod.Get, "user", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubException ex)
        {
            return new GitHubAccountCheck(false, ex.Message);
        }
        catch (GitHubNotConfiguredException ex)
        {
            return new GitHubAccountCheck(false, ex.Message);
        }

        var login = user.ValueKind == JsonValueKind.Object
            && user.TryGetProperty("login", out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var endpoint = string.IsNullOrWhiteSpace(credential.ApiEndpoint)
            ? _token.EndpointUri("user").GetLeftPart(UriPartial.Authority)
            : GitHubSettings.NormalizeApiEndpoint(credential.ApiEndpoint);

        if (login is null)
        {
            return new GitHubAccountCheck(false, $"GitHub at {endpoint} answered without saying whose credential this is.");
        }

        if (!GitHubAccount.IsSameLogin(login, account.Login))
        {
            return new GitHubAccountCheck(
                false,
                $"The credential for {account.Login} authenticates as {login}, not {account.Login}. Calls for {account.Login} would leave as the wrong person.");
        }

        return new GitHubAccountCheck(
            true,
            account.Credential is GitHubCredentialKind.GhCli
                ? $"GitHub recognises the GitHub CLI's token as {login} at {endpoint}."
                : $"GitHub recognises the token as {login} at {endpoint}.");
    }

    /// <summary>
    /// Carries one call the way its path decides.
    /// <para>
    /// The credential is resolved before the CLI is even asked, because whether the
    /// path names an identity is the thing that decides. A bound path goes over
    /// HTTP no matter what the CLI could do; an unbound one keeps the order it has
    /// always had — the CLI's identity first, a repository's own token underneath it,
    /// exactly as its own copy in Settings says.
    /// </para>
    /// <para>
    /// The CLI's identity no longer means a <c>gh api</c> process per call. Its token
    /// is borrowed once, held in memory for <see cref="GhCliAccountSource.TokenLifetime"/>,
    /// and the call goes over HTTP with it; <c>gh api</c> stays the fallback whenever
    /// no github.com token can be had and whenever gh would not be talking to
    /// github.com. A refused borrowed token surfaces as its 401 and is forgotten,
    /// together with the CLI's availability, so the next call starts over.
    /// </para>
    /// <para>
    /// The bound route's token transport resolves again when it sends. That is one
    /// repeated lookup over configuration already in memory, or one cache hit against
    /// a token fetched moments earlier — cheap enough to be worth the two types each
    /// answering for themselves rather than passing a credential between them.
    /// </para>
    /// </summary>
    public async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var credential = await _credentials.ResolveAsync(path, cancellationToken).ConfigureAwait(false);

        if (credential is { IsBound: true })
        {
            return await _token.SendAsync(method, path, body, apiVersion, cancellationToken).ConfigureAwait(false);
        }

        if (await _cli.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await CliCredentialAsync(cancellationToken).ConfigureAwait(false) is { } borrowed)
            {
                try
                {
                    // SendAsAsync, never SendAsync: the token transport's own lookup
                    // would put the repository's token above the CLI's identity.
                    return await _token.SendAsAsync(borrowed, method, path, body, apiVersion, cancellationToken).ConfigureAwait(false);
                }
                catch (GitHubException ex) when (ex.Status == HttpStatusCode.Unauthorized)
                {
                    // Revoked or rotated since it was borrowed. Not sent again:
                    // guideline 0015 resends nothing after an authentication failure.
                    // Forgetting the token and the CLI's remembered availability
                    // makes the next call re-probe gh and borrow afresh — or fall
                    // back, when gh's own credential is the one that went bad.
                    _accounts.Invalidate();
                    _cli.Invalidate();
                    throw;
                }
            }

            return await _cli.SendAsync(method, path, body, apiVersion, cancellationToken).ConfigureAwait(false);
        }

        if (credential is not null || _credentials.HasAnyCredential)
        {
            return await _token.SendAsync(method, path, body, apiVersion, cancellationToken).ConfigureAwait(false);
        }

        throw new GitHubNotConfiguredException(
            "No way to reach GitHub. Sign in with `gh auth login`, or add a personal access token in repository settings.");
    }

    /// <summary>
    /// The token gh holds for the login it is signed in as, ready to send — or null
    /// when the call should stay with <c>gh api</c>.
    /// <para>
    /// Only on github.com. <c>gh api</c> goes to gh's own default host and has never
    /// read the install-wide endpoint, so an Enterprise endpoint, or a
    /// <c>GH_HOST</c> or <c>GH_ENTERPRISE_TOKEN</c> that moves gh elsewhere, keeps the
    /// call where it has always gone rather than putting a github.com token on
    /// another host.
    /// </para>
    /// </summary>
    private async Task<GitHubCredential?> CliCredentialAsync(CancellationToken cancellationToken)
    {
        if (_cli.Account is not { } login || !OnGitHubDotCom()) return null;

        // github.com by name, not gh's default host: a gh signed in only to an
        // Enterprise host answers `gh api user` from there, and its token must never
        // be sent to api.github.com. Such a login has no github.com token, so nothing
        // is borrowed and the call stays with gh api.
        var token = await _accounts.GetTokenAsync(login, GitHubDotCom, cancellationToken).ConfigureAwait(false);

        return token is null
            ? null
            : new GitHubCredential(token, GitHubSettings.DefaultApiEndpoint, Account: null);
    }

    private bool OnGitHubDotCom()
    {
        if (!string.IsNullOrWhiteSpace(EnvironmentVariable("GH_HOST"))) return false;
        if (!string.IsNullOrWhiteSpace(EnvironmentVariable("GH_ENTERPRISE_TOKEN"))) return false;

        return GitHubSettings.IsDefaultApiEndpoint(_settings.Current.ApiEndpoint);
    }
}
