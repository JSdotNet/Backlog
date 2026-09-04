namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// How this machine satisfies one GitHub identity.
/// <para>
/// A per-machine fact, which is why it lives in the per-user file rather than in
/// the shared registry: install #2 may have no <c>gh</c> at all, or be signed in
/// to a different set of logins, while the workspace's opinion about which
/// account a repository is worked as is the same everywhere.
/// </para>
/// </summary>
public enum GitHubCredentialKind
{
    /// <summary>Ask the <c>gh</c> CLI for this login's token when a call needs
    /// one. The app never holds the credential: <c>gh</c> keeps it, refreshes it
    /// and revokes it.</summary>
    GhCli,

    /// <summary>Use the personal access token pasted into Settings. The route for
    /// a machine without <c>gh</c>, and for GitHub Enterprise Server.</summary>
    PersonalAccessToken
}

/// <summary>
/// One GitHub identity this machine can speak as.
/// <para>
/// <see cref="Login"/> is the id. GitHub logins are unique per host and compared
/// without regard to case, which is the same rule
/// <see cref="GitHubRepositoryRef.FullName"/> is matched by throughout the store.
/// </para>
/// <para>
/// A <see cref="GitHubCredentialKind.GhCli"/> account carries no
/// <see cref="Token"/> and never will. <c>gho_</c> tokens are OAuth tokens the
/// CLI rotates, so writing one down would create a stale secret in a file — a
/// correctness regression and a security one. They are fetched when a call needs
/// one and held in memory only.
/// </para>
/// </summary>
public sealed record GitHubAccount(string Login)
{
    /// <summary>The value a repository binding stores. Named separately from
    /// <see cref="Login"/> so the two readings — "who this is" and "what the
    /// binding points at" — are the same value on purpose rather than by
    /// accident.</summary>
    public string Id => Login;

    /// <summary>An optional human label, for when a login is not the name
    /// somebody thinks of the account by.</summary>
    public string? DisplayName { get; init; }

    public GitHubCredentialKind Credential { get; init; } = GitHubCredentialKind.GhCli;

    /// <summary>The pasted token, for
    /// <see cref="GitHubCredentialKind.PersonalAccessToken"/> only. Always null
    /// otherwise — see the type's remarks.</summary>
    public string? Token { get; init; }

    /// <summary>The host this login belongs to. Null means github.com; a GitHub
    /// Enterprise Server hostname otherwise.</summary>
    public string? Host { get; init; }

    /// <summary>An API endpoint that overrides the install-wide one, for an
    /// account on another host. Null is the ordinary case.</summary>
    public string? ApiEndpoint { get; init; }

    /// <summary>What a surface shows. The login when nobody gave it a label,
    /// which is the ordinary case.</summary>
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Login : DisplayName!.Trim();

    /// <summary>
    /// Whether this account already holds a credential, without asking anything
    /// outside this process.
    /// <para>
    /// A pasted token, and only that. Whether <c>gh</c> can produce a token for a
    /// <see cref="GitHubCredentialKind.GhCli"/> account is a subprocess away and
    /// cannot be answered here, which is exactly why the transport asks a resolver
    /// rather than reading this.
    /// </para>
    /// </summary>
    public bool HasToken => Credential is GitHubCredentialKind.PersonalAccessToken
        && !string.IsNullOrWhiteSpace(Token);

    /// <summary>Two logins that name the same account. Case-insensitive, the way
    /// GitHub compares them.</summary>
    public static bool IsSameLogin(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A login as it is stored: trimmed, and null rather than blank. The
    /// casing is left alone — GitHub spells a login the way its owner does, and
    /// lower-casing it would put a name on screen nobody chose.</summary>
    public static string? NormalizeLogin(string? login) =>
        string.IsNullOrWhiteSpace(login) ? null : login.Trim();
}

/// <summary>
/// Which identity one API path's call has to go out as.
/// <para>
/// The answer to the only question that matters at the moment a request leaves:
/// "which credential authenticates <em>this</em> path". A pure function over the
/// configuration, so it is decided the same way every time and can be read as a
/// table.
/// </para>
/// <para>
/// Four shapes, and the differences between them are load bearing. A <em>bound</em>
/// path names an account and must never leave as any other identity — falling
/// through to whoever happens to be signed in is the 404 this whole change exists
/// to fix. An <em>unsatisfied</em> path names an account this machine has no row
/// for, which is the ordinary day-one state of a second install and is reported
/// rather than guessed around. A path carrying only a <em>repository token</em> is
/// today's behaviour unchanged. And <em>default</em> — the great majority — is
/// today's behaviour exactly: whatever this machine's signed-in identity is.
/// </para>
/// </summary>
public sealed record GitHubAccountChoice
{
    private GitHubAccountChoice()
    {
    }

    /// <summary>The call goes out as this machine's default identity, which is
    /// what every call does today.</summary>
    public static GitHubAccountChoice Default { get; } = new();

    /// <summary>Bound, and this machine has the account the binding names.</summary>
    public static GitHubAccountChoice Bound(GitHubAccount account, string subject) => new()
    {
        Account = account,
        Login = account.Login,
        Token = account.HasToken ? account.Token : null,
        Subject = subject
    };

    /// <summary>Bound to a login this machine holds no account for.</summary>
    public static GitHubAccountChoice Unsatisfied(string login, string subject) => new()
    {
        Login = GitHubAccount.NormalizeLogin(login),
        Subject = subject
    };

    /// <summary>Unbound, but the repository carries a token of its own — the
    /// fallback the token control in Settings has always described itself
    /// as.</summary>
    public static GitHubAccountChoice RepositoryToken(string token, string subject) => new()
    {
        Token = token,
        Subject = subject
    };

    /// <summary>The bound account, or null when the binding is unsatisfied or
    /// there is no binding.</summary>
    public GitHubAccount? Account { get; private init; }

    /// <summary>The login the binding names, whether or not this machine can
    /// satisfy it. Null when nothing is bound.</summary>
    public string? Login { get; private init; }

    /// <summary>The token already in hand. Null both when nothing is configured
    /// and when the credential still has to be fetched from the <c>gh</c>
    /// CLI.</summary>
    public string? Token { get; private init; }

    /// <summary>What the choice is about — a repository, an organization or a
    /// login — so a refusal can name the thing somebody has to go and fix.</summary>
    public string? Subject { get; private init; }

    /// <summary>Whether the endpoint the account named overrides the install-wide
    /// one.</summary>
    public string? ApiEndpoint => Account?.ApiEndpoint;

    /// <summary>Whether an identity was chosen by name. A bound call may not be
    /// substituted for another identity under any circumstances.</summary>
    public bool IsBound => Login is not null;

    /// <summary>Bound to a login this machine cannot satisfy at all — no account
    /// row, or a token account with nothing pasted into it.</summary>
    public bool IsUnsatisfied => IsBound
        && (Account is null
            || (Account.Credential is GitHubCredentialKind.PersonalAccessToken && !Account.HasToken));

    /// <summary>Nothing about this path names an identity, so it goes out the way
    /// every call goes out today.</summary>
    public bool IsDefault => Login is null && Token is null;
}
