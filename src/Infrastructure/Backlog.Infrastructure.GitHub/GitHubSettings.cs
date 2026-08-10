using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The GitHub half of the app's settings: which repositories may be pushed to,
/// and how to authenticate.
/// <para>
/// Kept in a per-user file next to the backlog pointer rather than inside the
/// backlog folder itself. The backlog folder is meant to be synced or committed;
/// a token has no business travelling with it.
/// </para>
/// </summary>
public sealed class GitHubSettings
{
    /// <summary>Repositories in the order they were configured. The first is the
    /// default when an entry names no area.</summary>
    public List<GitHubRepositoryRef> Repositories { get; init; } = [];

    /// <summary>Personal access token, used only when the GitHub CLI is not
    /// signed in. Null means "rely on <c>gh</c>".</summary>
    public string? Token { get; init; }

    public GitHubRepositoryRef? Find(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return null;

        var normalized = GitHubRepositoryRef.NormalizeAlias(alias);
        return Repositories.FirstOrDefault(r => string.Equals(r.Alias, normalized, StringComparison.Ordinal))
            ?? Repositories.FirstOrDefault(r => string.Equals(r.FullName, alias.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The multi-line text shown in Settings, one repository per line.</summary>
    public string ToText() => string.Join('\n', Repositories.Select(r => r.ToLine()));

    /// <summary>Reads the Settings text back. Unparseable lines are reported
    /// rather than thrown on — a half-typed line is an ordinary thing to have in
    /// a text box, and the lines around it should still take effect.</summary>
    public static (List<GitHubRepositoryRef> Repositories, IReadOnlyList<string> Errors) ParseText(string? text)
    {
        var repositories = new List<GitHubRepositoryRef>();
        var errors = new List<string>();

        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var parsed = GitHubRepositoryRef.TryParse(line, out var error);

            if (parsed is null)
            {
                if (error is not null) errors.Add(error);
                continue;
            }

            if (repositories.Any(r => string.Equals(r.Alias, parsed.Alias, StringComparison.Ordinal)))
            {
                errors.Add($"'{parsed.Alias}' is configured twice — an area can only point at one repository.");
                continue;
            }

            repositories.Add(parsed);
        }

        return (repositories, errors);
    }
}

/// <summary>
/// Reads and writes <see cref="GitHubSettings"/> in the per-user application
/// folder. Follows the house rule of no save button: callers commit a whole
/// value and it is persisted immediately.
/// </summary>
public sealed class GitHubSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public GitHubSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            "github.json"))
    {
    }

    public GitHubSettingsStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Current = Read();
    }

    /// <summary>Raised after the configuration changes, so open views and any
    /// cached connection can react.</summary>
    public event Action? Changed;

    public GitHubSettings Current { get; private set; }

    /// <summary>Where the file lives, shown in Settings so it can be found (and
    /// so it is obvious the token is not in the backlog folder).</summary>
    public string SettingsPath => _path;

    /// <summary>Replaces the configured repositories. Returns an error message
    /// when persisting failed; the in-memory value is updated either way so the
    /// session still works.</summary>
    public string? SetRepositories(IEnumerable<GitHubRepositoryRef> repositories) =>
        Save(new GitHubSettings
        {
            Repositories = [.. repositories],
            Token = Current.Token
        });

    public string? SetToken(string? token) =>
        Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories],
            Token = string.IsNullOrWhiteSpace(token) ? null : token.Trim()
        });

    private string? Save(GitHubSettings settings)
    {
        Current = settings;

        string? error = null;
        try
        {
            var dto = new SettingsDto
            {
                Repositories = [.. settings.Repositories.Select(r => new RepositoryDto
                {
                    Alias = r.Alias,
                    Owner = r.Owner,
                    Name = r.Name
                })],
                Token = settings.Token
            };

            File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the choice couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private GitHubSettings Read()
    {
        try
        {
            if (!File.Exists(_path)) return new GitHubSettings();

            var dto = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return new GitHubSettings();

            return new GitHubSettings
            {
                Repositories =
                [
                    .. dto.Repositories
                        .Where(r => !string.IsNullOrWhiteSpace(r.Owner) && !string.IsNullOrWhiteSpace(r.Name))
                        .Select(r => new GitHubRepositoryRef(
                            GitHubRepositoryRef.NormalizeAlias(string.IsNullOrWhiteSpace(r.Alias) ? r.Name! : r.Alias!),
                            r.Owner!,
                            r.Name!))
                ],
                Token = string.IsNullOrWhiteSpace(dto.Token) ? null : dto.Token
            };
        }
        catch (Exception)
        {
            // A corrupt settings file must never stop the app from opening.
            return new GitHubSettings();
        }
    }

    private sealed class SettingsDto
    {
        public List<RepositoryDto> Repositories { get; set; } = [];
        public string? Token { get; set; }
    }

    private sealed class RepositoryDto
    {
        public string? Alias { get; set; }
        public string? Owner { get; set; }
        public string? Name { get; set; }
    }
}
