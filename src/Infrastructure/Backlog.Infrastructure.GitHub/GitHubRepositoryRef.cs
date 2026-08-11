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

    /// <summary>The repository that owns entries and knowledge not assigned to a
    /// more specific repository.</summary>
    public bool IsPrimary { get; init; }

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

public sealed record KnowledgeFolderSetting(string Key, string DisplayName, string DefaultRelativePath, bool SupportsPathOverride = true)
{
    public bool Enabled { get; init; } = true;

    /// <summary>Optional repository-relative or absolute override. Null means the
    /// conventional folder at the repository root is used.</summary>
    public string? Path { get; init; }

    public string EffectivePath => string.IsNullOrWhiteSpace(Path) ? DefaultRelativePath : Path.Trim();

    public static List<KnowledgeFolderSetting> Defaults() =>
    [
        new(".backlog", "Backlog", ".backlog"),
        new("instructions", "Instructions", string.Empty, SupportsPathOverride: false),
        new(".domain", "Domain", ".domain"),
        new(".arc42", "arc42 architecture", ".arc42"),
        new(".tech", "Technology", ".tech"),
        new(".design", "Design", ".design")
    ];

    public static List<KnowledgeFolderSetting> Normalize(IEnumerable<KnowledgeFolderSetting>? configured)
    {
        var byKey = (configured ?? []).ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. Defaults().Select(folder =>
                byKey.TryGetValue(folder.Key, out var existing)
                    ? folder with
                    {
                        Enabled = existing.Enabled,
                        Path = folder.SupportsPathOverride && !string.IsNullOrWhiteSpace(existing.Path) ? existing.Path.Trim() : null
                    }
                    : folder)
        ];
    }
}
