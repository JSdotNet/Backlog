using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// A resolver that answers from a table instead of from the configuration and the
/// <c>gh</c> CLI.
/// <para>
/// The seam <see cref="TokenTransport"/> gained when it stopped taking a pair of
/// delegates. It exists so a transport test can say "this path resolves to this
/// credential" without building a settings store, a workspace root and a stand-in
/// executable to make it true — and so the two questions the interface carries can
/// be answered independently, which is the whole point of their being two.
/// </para>
/// </summary>
internal sealed class StubCredentialResolver : IGitHubCredentialResolver
{
    private readonly Func<string?, GitHubCredential?> _resolve;

    private StubCredentialResolver(Func<string?, GitHubCredential?> resolve, bool hasAnyCredential)
    {
        _resolve = resolve;
        HasAnyCredential = hasAnyCredential;
    }

    /// <summary>Every path leaves with the same unbound token — the shape of a
    /// machine that has a repository token and nothing else.</summary>
    public static StubCredentialResolver WithToken(string token = "ghp_example") =>
        new(_ => new GitHubCredential(token, null, null), hasAnyCredential: true);

    /// <summary>Every path leaves as a named account, which is the shape that must
    /// never be answered by the CLI.</summary>
    public static StubCredentialResolver Bound(string account, string token = "ghp_bound") =>
        new(_ => new GitHubCredential(token, null, account), hasAnyCredential: true);

    /// <summary>Nothing is configured: every path is the default identity.</summary>
    public static StubCredentialResolver None() => new(_ => null, hasAnyCredential: false);

    public bool HasAnyCredential { get; }

    public Task<GitHubCredential?> ResolveAsync(string? path, CancellationToken cancellationToken = default) =>
        Task.FromResult(_resolve(path));
}
