namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The credential one call leaves with.
/// <para>
/// <see cref="Account"/> is the login it was chosen <em>by name</em> for, and is
/// null when nothing named an identity — an unbound repository's own token, which
/// is the fallback the token control in Settings has always described itself as.
/// The difference is load bearing: a credential chosen by name may never be
/// substituted for another identity, so it wins over the CLI, while an unnamed one
/// stays where it has always been, underneath it.
/// </para>
/// </summary>
public sealed record GitHubCredential(string Token, string? ApiEndpoint, string? Account)
{
    /// <summary>Whether this credential was chosen by name.</summary>
    public bool IsBound => Account is not null;
}

/// <summary>
/// Answers "which credential authenticates this path", per call.
/// <para>
/// An interface rather than the bare <c>Func&lt;string?, string?&gt;</c> the token
/// transport used to take, for two reasons. It composes two behaviours now —
/// reading the configuration, and extracting a token from the <c>gh</c> CLI — and
/// the second is a subprocess, so the lookup has to be asynchronous rather than
/// blocked on inside a synchronous delegate on the UI thread.
/// </para>
/// <para>
/// Two members, and the split between them is the correction Amendment A of the
/// design makes. "Which credential authenticates <em>this</em> path" and "is this
/// machine configured to reach GitHub with a token at all" were one question, and
/// the single answer they shared was the cross-repository fallback: a path with no
/// credential of its own took the first token in the list, which is how a call for
/// one owner's repository left carrying another owner's identity and came back a
/// 404. Splitting them lets the fallback be deleted from the first question, where
/// it is the defect, while the second keeps the answer it actually needed.
/// </para>
/// </summary>
public interface IGitHubCredentialResolver
{
    /// <summary>
    /// The credential this path's call must go out with, or null when it goes out
    /// as this machine's default identity.
    /// <para>
    /// Throws <see cref="GitHubNotConfiguredException"/> naming the account when the
    /// path is bound to one this machine cannot satisfy. It never falls through to
    /// another identity — falling through is the bug.
    /// </para>
    /// </summary>
    Task<GitHubCredential?> ResolveAsync(string? path, CancellationToken cancellationToken = default);

    /// <summary>Whether this machine holds a token at all. The only question the
    /// availability probe asks, and it is never permitted to select a credential for
    /// a request.</summary>
    bool HasAnyCredential { get; }
}

/// <summary>
/// Resolves against the configuration and the <c>gh</c> CLI.
/// <para>
/// Reads <c>settings.Current</c> <em>per call</em>, which is the fix for a live
/// defect this replaces: the token transport used to be constructed with a method
/// group bound to the settings snapshot that existed at construction time, while
/// the API endpoint beside it was a lambda reading <c>Current</c>. Every mutator
/// replaces <c>Current</c>, so a token configured after startup — or a workspace
/// move, which is what <c>Reload()</c> is wired to — was visible to the endpoint
/// half and invisible to the token half.
/// </para>
/// </summary>
public sealed class GitHubCredentialResolver : IGitHubCredentialResolver
{
    private readonly Func<GitHubSettings> _settings;
    private readonly IGhCliAccountSource _accounts;

    public GitHubCredentialResolver(GitHubSettingsStore settings, IGhCliAccountSource? accounts = null)
        : this(() => settings.Current, accounts)
    {
        ArgumentNullException.ThrowIfNull(settings);
    }

    /// <summary>
    /// The composed form: the settings read per call, and the CLI to extract a
    /// credential from.
    /// <para>
    /// A <see cref="Func{TResult}"/> rather than the value, and the shape is the
    /// point: reading the settings per call is exactly what the type it replaces did
    /// not do, and a resolver holding a snapshot would reintroduce the defect it was
    /// written to fix.
    /// </para>
    /// </summary>
    public GitHubCredentialResolver(Func<GitHubSettings> settings, IGhCliAccountSource? accounts = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _accounts = accounts ?? new GhCliAccountSource();
    }

    public bool HasAnyCredential => _settings().HasAnyCredential;

    public async Task<GitHubCredential?> ResolveAsync(string? path, CancellationToken cancellationToken = default)
    {
        var choice = _settings().AccountForPath(path);

        if (!choice.IsBound)
        {
            // Unbound. Either the repository carries a token of its own, or this is
            // the default identity and no credential of ours is involved at all.
            return choice.Token is { } repositoryToken
                ? new GitHubCredential(repositoryToken, choice.ApiEndpoint, null)
                : null;
        }

        var login = choice.Login!;

        if (choice.Account is not { } account)
        {
            // The workspace expects a login this machine holds no account for. The
            // ordinary day-one state of a second install, and it is reported rather
            // than answered with whoever happens to be signed in.
            throw Unsatisfiable(choice, $"this machine has no credential for '{login}'");
        }

        if (account.Credential is GitHubCredentialKind.PersonalAccessToken)
        {
            return account.HasToken
                ? new GitHubCredential(account.Token!.Trim(), choice.ApiEndpoint, account.Login)
                : throw Unsatisfiable(choice, $"no personal access token has been pasted for '{login}'");
        }

        var token = await _accounts.GetTokenAsync(account.Login, account.Host, cancellationToken);

        return string.IsNullOrWhiteSpace(token)
            ? throw Unsatisfiable(choice, $"the GitHub CLI has no token for '{login}'")
            : new GitHubCredential(token.Trim(), choice.ApiEndpoint, account.Login);
    }

    /// <summary>A binding this machine cannot meet. A settings problem rather than a
    /// failure, so it carries the two things somebody needs: what was being reached
    /// for, and which account it was to be reached as.</summary>
    private static GitHubNotConfiguredException Unsatisfiable(GitHubAccountChoice choice, string reason) =>
        new($"{choice.Subject ?? choice.Login} is worked as '{choice.Login}', and {reason}.");
}
