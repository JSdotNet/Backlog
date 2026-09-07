using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// One repository the app is allowed to talk to, as configured in Settings.
/// <para>
/// <see cref="Alias"/> is the short name a person types in an entry's
/// <c>`@area`</c> token. Reusing the area sigil rather than inventing a repo
/// sigil is deliberate: an entry already says which pile it belongs to, and for
/// repository work that pile <em>is</em> the repository.
/// </para>
/// </summary>
public sealed record GitHubRepositoryRef(string Alias, string Owner, string Name)
{
    /// <summary>The <c>owner/name</c> form GitHub itself uses.</summary>
    public string FullName => $"{Owner}/{Name}";

    public string Url => $"https://github.com/{Owner}/{Name}";

    /// <summary>Local clone root for this repository. Null means it has not been
    /// cloned or the app has not been told where it lives yet.</summary>
    public string? CloneDirectory { get; init; }

    /// <summary>Personal access token for this repository, used only when the
    /// GitHub CLI is not signed in. Null means "rely on <c>gh</c>".</summary>
    public string? Token { get; init; }

    /// <summary>
    /// Which of the sanctioned identity hues this repository wears, 1 to
    /// <see cref="RepositoryColours.Available"/>. Null is the ordinary case and means
    /// nobody has chosen — the hue is then taken from the repository's position, which
    /// is why this is nullable rather than defaulted.
    /// <para>
    /// A number, never a colour. Inventing a hue is a design decision and
    /// <c>.design/color-scheme.md#band-identity-tokens</c> is where it is made; this
    /// records only which of the approved ones somebody picked.
    /// </para>
    /// </summary>
    public int? Colour { get; init; }

    /// <summary>
    /// The login of the account this repository is worked as, or null for "whatever
    /// this machine's default is" — which is what every repository answered before
    /// accounts existed, and still is unless somebody picks one.
    /// <para>
    /// Shared identity rather than machine data, and so it travels in the registry
    /// beside <see cref="Alias"/> and <see cref="Colour"/>. "That is my work
    /// repository" is true on every install of a workspace; whether <em>this</em>
    /// machine holds a credential for that login is a separate fact, and lives in
    /// the per-user file as a <see cref="GitHubAccount"/>.
    /// </para>
    /// <para>
    /// Deliberately not part of the <c>alias = owner/repo</c> grammar, for the
    /// reason the hue is not: it is chosen from a known list rather than typed, so
    /// picking it beats spelling it, and the parser, its error messages and its
    /// duplicate detection are all left alone.
    /// </para>
    /// </summary>
    public string? Account { get; init; }

    /// <summary>The knowledge folders configured for this repository. The type
    /// is Second Brain's published language rather than this adapter's: a
    /// repository is one place those folders can live, not the thing that
    /// defines them. That is why this project references that module's
    /// abstractions and not the reverse.</summary>
    public List<KnowledgeFolderSetting> KnowledgeFolders { get; init; } = KnowledgeFolderSetting.Defaults();

    /// <summary>
    /// Reads one configured line. Accepted forms:
    /// <code>
    /// alias = owner/repo
    /// owner/repo
    /// https://github.com/owner/repo
    /// </code>
    /// A line without an explicit alias takes the repository name as its alias,
    /// which is what someone typing <c>`@backlog`</c> would expect. Returns null
    /// with a reason for anything that isn't a repository, so a half-typed line
    /// can be reported rather than silently dropped.
    /// </summary>
    public static GitHubRepositoryRef? TryParse(string? line, out string? error)
    {
        error = null;

        var text = (line ?? string.Empty).Trim();
        if (text.Length == 0 || text.StartsWith('#')) return null;

        string? alias = null;
        var separator = text.IndexOf('=');
        if (separator >= 0)
        {
            alias = text[..separator].Trim();
            text = text[(separator + 1)..].Trim();

            if (alias.Length == 0)
            {
                error = $"'{line!.Trim()}' has no name before the '='.";
                return null;
            }
        }

        text = StripUrl(text).Trim('/');

        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            error = $"'{line!.Trim()}' is not a repository — write it as owner/repo.";
            return null;
        }

        alias ??= parts[1];

        return new GitHubRepositoryRef(NormalizeAlias(alias), parts[0], parts[1]);
    }

    /// <summary>Aliases are compared to the area token, which the entry parser
    /// lower-cases, so they are stored the same way.</summary>
    public static string NormalizeAlias(string alias) => alias.Trim().ToLowerInvariant();

    /// <summary>The canonical single-line form written back into Settings.</summary>
    public string ToLine() =>
        string.Equals(Alias, NormalizeAlias(Name), StringComparison.Ordinal)
            ? FullName
            : $"{Alias} = {FullName}";

    private static string StripUrl(string text)
    {
        const string https = "https://github.com/";
        const string http = "http://github.com/";

        if (text.StartsWith(https, StringComparison.OrdinalIgnoreCase)) text = text[https.Length..];
        else if (text.StartsWith(http, StringComparison.OrdinalIgnoreCase)) text = text[http.Length..];

        return text.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? text[..^4] : text;
    }
}
