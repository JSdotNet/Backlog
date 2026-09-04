using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// A <c>gh</c> CLI that answers from a dictionary.
/// <para>
/// The real source launches a subprocess, so <see cref="GhCliAccountSourceTests"/>
/// drives it through the stand-in executable. Everything <em>above</em> it —
/// which account a path resolves to, and what happens when the CLI cannot produce a
/// credential for one — is about the decision rather than the command line, and is
/// better tested against a table than against a batch file.
/// </para>
/// </summary>
internal sealed class StubGhCliAccountSource : IGhCliAccountSource
{
    /// <summary>The token each login has, if any. A login that is missing is one the
    /// CLI is not signed in to.</summary>
    public Dictionary<string, string> Tokens { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<GhCliAccount> Accounts { get; } = [];

    /// <summary>Every login a token was asked for, in order.</summary>
    public List<string> Asked { get; } = [];

    public int Invalidations { get; private set; }

    public Task<IReadOnlyList<GhCliAccount>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GhCliAccount>>(Accounts);

    public Task<string?> GetTokenAsync(string login, string? host = null, CancellationToken cancellationToken = default)
    {
        Asked.Add(login);
        return Task.FromResult(Tokens.GetValueOrDefault(login));
    }

    public void Invalidate() => Invalidations++;
}
