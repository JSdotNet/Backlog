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
    /// <summary>Repositories in the order they were configured.</summary>
    public List<GitHubRepositoryRef> Repositories { get; init; } = [];

    /// <summary>Personal access token, used only when the GitHub CLI is not
    /// signed in. Null means "rely on <c>gh</c>".</summary>
    public string? Token { get; init; }

    public GitHubRepositoryRef? PrimaryRepository =>
        Repositories.FirstOrDefault(r => r.IsPrimary) ?? Repositories.FirstOrDefault();

    public GitHubRepositoryRef? Find(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return PrimaryRepository;

        var normalized = GitHubRepositoryRef.NormalizeAlias(alias);
        return Repositories.FirstOrDefault(r => string.Equals(r.Alias, normalized, StringComparison.Ordinal))
            ?? Repositories.FirstOrDefault(r => string.Equals(r.FullName, alias.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? PrimaryRepository;
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
            Repositories = EnsureOnePrimary([.. repositories.Select(PreserveExistingRepositorySettings)]),
            Token = Current.Token
        });

    public string? SetToken(string? token) =>
        Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories],
            Token = string.IsNullOrWhiteSpace(token) ? null : token.Trim()
        });

    public string? SetPrimaryRepository(string alias)
    {
        var normalized = GitHubRepositoryRef.NormalizeAlias(alias);
        if (!Current.Repositories.Any(r => string.Equals(r.Alias, normalized, StringComparison.Ordinal)))
        {
            return "That repository is no longer configured.";
        }

        return Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories.Select(r => r with { IsPrimary = string.Equals(r.Alias, normalized, StringComparison.Ordinal) })],
            Token = Current.Token
        });
    }

    public string? SetCloneDirectory(string alias, string? cloneDirectory)
    {
        var normalized = GitHubRepositoryRef.NormalizeAlias(alias);
        if (!Current.Repositories.Any(r => string.Equals(r.Alias, normalized, StringComparison.Ordinal)))
        {
            return "That repository is no longer configured.";
        }

        return Save(new GitHubSettings
        {
            Repositories =
            [
                .. Current.Repositories.Select(r =>
                    string.Equals(r.Alias, normalized, StringComparison.Ordinal)
                        ? r with { CloneDirectory = CleanPath(cloneDirectory) }
                        : r)
            ],
            Token = Current.Token
        });
    }

    public string? SetKnowledgeFolder(string alias, string key, bool enabled, string? path)
    {
        var normalized = GitHubRepositoryRef.NormalizeAlias(alias);
        if (!Current.Repositories.Any(r => string.Equals(r.Alias, normalized, StringComparison.Ordinal)))
        {
            return "That repository is no longer configured.";
        }

        return Save(new GitHubSettings
        {
            Repositories =
            [
                .. Current.Repositories.Select(r =>
                    string.Equals(r.Alias, normalized, StringComparison.Ordinal)
                        ? r with
                        {
                            KnowledgeFolders =
                            [
                                .. KnowledgeFolderSetting.Normalize(r.KnowledgeFolders)
                                    .Select(folder => string.Equals(folder.Key, key, StringComparison.OrdinalIgnoreCase)
                                        ? folder with { Enabled = enabled, Path = CleanPath(path) }
                                        : folder)
                            ]
                        }
                        : r)
            ],
            Token = Current.Token
        });
    }

    private string? Save(GitHubSettings settings)
    {
        var normalized = new GitHubSettings
        {
            Repositories = EnsureOnePrimary(settings.Repositories),
            Token = settings.Token
        };
        Current = normalized;

        string? error = null;
        try
        {
            var dto = new SettingsDto
            {
                Repositories = [.. normalized.Repositories.Select(r => new RepositoryDto
                {
                    Alias = r.Alias,
                    Owner = r.Owner,
                    Name = r.Name,
                    CloneDirectory = r.CloneDirectory,
                    IsPrimary = r.IsPrimary,
                    KnowledgeFolders =
                    [
                        .. KnowledgeFolderSetting.Normalize(r.KnowledgeFolders).Select(f => new KnowledgeFolderDto
                        {
                            Key = f.Key,
                            Enabled = f.Enabled,
                            Path = f.Path
                        })
                    ]
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
                Repositories = EnsureOnePrimary(
                [
                    .. dto.Repositories
                        .Where(r => !string.IsNullOrWhiteSpace(r.Owner) && !string.IsNullOrWhiteSpace(r.Name))
                        .Select(r => new GitHubRepositoryRef(
                            GitHubRepositoryRef.NormalizeAlias(string.IsNullOrWhiteSpace(r.Alias) ? r.Name! : r.Alias!),
                            r.Owner!,
                            r.Name!)
                        {
                            CloneDirectory = CleanPath(r.CloneDirectory),
                            IsPrimary = r.IsPrimary,
                            KnowledgeFolders = KnowledgeFolderSetting.Normalize(
                                r.KnowledgeFolders.Select(f => new KnowledgeFolderSetting(
                                    string.IsNullOrWhiteSpace(f.Key) ? string.Empty : f.Key!,
                                    string.Empty,
                                    string.Empty)
                                {
                                    Enabled = f.Enabled,
                                    Path = f.Path
                                }))
                        })
                ]),
                Token = string.IsNullOrWhiteSpace(dto.Token) ? null : dto.Token
            };
        }
        catch (Exception)
        {
            // A corrupt settings file must never stop the app from opening.
            return new GitHubSettings();
        }
    }

    private GitHubRepositoryRef PreserveExistingRepositorySettings(GitHubRepositoryRef repository)
    {
        var existing = Current.Repositories.FirstOrDefault(r => string.Equals(r.Alias, repository.Alias, StringComparison.Ordinal))
            ?? Current.Repositories.FirstOrDefault(r => string.Equals(r.FullName, repository.FullName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return repository with { KnowledgeFolders = KnowledgeFolderSetting.Normalize(repository.KnowledgeFolders) };
        }

        return repository with
        {
            CloneDirectory = string.IsNullOrWhiteSpace(repository.CloneDirectory) ? existing.CloneDirectory : repository.CloneDirectory,
            IsPrimary = repository.IsPrimary || existing.IsPrimary,
            KnowledgeFolders = KnowledgeFolderSetting.Normalize(existing.KnowledgeFolders)
        };
    }

    private static List<GitHubRepositoryRef> EnsureOnePrimary(IReadOnlyList<GitHubRepositoryRef> repositories)
    {
        if (repositories.Count == 0) return [];

        var primary = repositories.FirstOrDefault(r => r.IsPrimary) ?? repositories[0];

        return
        [
            .. repositories.Select(r => r with
            {
                IsPrimary = ReferenceEquals(r, primary) || string.Equals(r.Alias, primary.Alias, StringComparison.Ordinal),
                CloneDirectory = CleanPath(r.CloneDirectory),
                KnowledgeFolders = KnowledgeFolderSetting.Normalize(r.KnowledgeFolders)
            })
        ];
    }

    private static string? CleanPath(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

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
        public string? CloneDirectory { get; set; }
        public bool IsPrimary { get; set; }
        public List<KnowledgeFolderDto> KnowledgeFolders { get; set; } = [];
    }

    private sealed class KnowledgeFolderDto
    {
        public string? Key { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Path { get; set; }
    }
}
