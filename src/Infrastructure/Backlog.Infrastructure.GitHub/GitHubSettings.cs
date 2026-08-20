using System.Text.Json;

using Backlog.Modules.Knowledge.Abstractions;

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
    public const string DefaultApiEndpoint = "https://api.github.com";

    /// <summary>Repositories in the order they were configured.</summary>
    public List<GitHubRepositoryRef> Repositories { get; init; } = [];

    /// <summary>Legacy global token read from older settings files and migrated
    /// onto repositories when saved again.</summary>
    public string? Token { get; init; }

    public string ApiEndpoint { get; init; } = DefaultApiEndpoint;

    public bool HasRepositoryToken => Repositories.Any(r => !string.IsNullOrWhiteSpace(r.Token));

    public GitHubRepositoryRef? Find(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return null;

        var trimmed = alias.Trim();
        var normalized = GitHubRepositoryRef.NormalizeAlias(trimmed);
        return Repositories.FirstOrDefault(r => string.Equals(r.Alias, normalized, StringComparison.Ordinal))
            ?? Repositories.FirstOrDefault(r => string.Equals(r.FullName, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Which identity hue each configured repository wears, keyed by alias.
    /// <para>
    /// Worked out from the whole list rather than per repository, because an
    /// unchosen repository's hue depends on what its neighbours have claimed. Callers
    /// that need one repository's hue ask this and index into it, so every surface is
    /// reading the same answer.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, int> Colours() => RepositoryColours.Resolve(Repositories);

    /// <summary>The hue a repository wears, or null when the alias names no configured
    /// repository.</summary>
    public int? ColourFor(string? alias)
    {
        var repository = Find(alias);
        return repository is null ? null : Colours().GetValueOrDefault(repository.Alias);
    }

    public string? TokenForPath(string? path)
    {
        var repository = RepositoryFromApiPath(path);
        return repository?.Token
            ?? Repositories.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Token))?.Token;
    }


    private GitHubRepositoryRef? RepositoryFromApiPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var trimmed = path.TrimStart('/');
        const string prefix = "repos/";
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var parts = trimmed[prefix.Length..].Split('/', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;

        return Repositories.FirstOrDefault(r =>
            string.Equals(r.Owner, parts[0], StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Name, parts[1], StringComparison.OrdinalIgnoreCase));
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
            Repositories = NormalizeRepositories([.. repositories.Select(PreserveExistingRepositorySettings)]),
            ApiEndpoint = Current.ApiEndpoint
        });

    public string? SetRepositoryToken(string alias, string? token)
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
                        ? r with { Token = CleanToken(token) }
                        : r)
            ],
            ApiEndpoint = Current.ApiEndpoint
        });
    }

    public string? SetToken(string? token)
    {
        var repository = Current.Repositories.FirstOrDefault();
        return repository is null ? null : SetRepositoryToken(repository.Alias, token);
    }

    public string? SetApiEndpoint(string? apiEndpoint) =>
        Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories],
            Token = null,
            ApiEndpoint = CleanEndpoint(apiEndpoint) ?? GitHubSettings.DefaultApiEndpoint
        });

    public string? RemoveRepository(string alias)
    {
        var normalized = GitHubRepositoryRef.NormalizeAlias(alias);
        if (!Current.Repositories.Any(r => string.Equals(r.Alias, normalized, StringComparison.Ordinal)))
        {
            return "That repository is no longer configured.";
        }

        return Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories.Where(r => !string.Equals(r.Alias, normalized, StringComparison.Ordinal))],
            ApiEndpoint = Current.ApiEndpoint
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
            ApiEndpoint = Current.ApiEndpoint
        });
    }

    /// <summary>
    /// Records which identity hue a repository wears, or clears the choice so the hue
    /// falls back to the repository's position. Follows the house rule of no save
    /// button: the choice is persisted as it is made.
    /// </summary>
    public string? SetRepositoryColour(string alias, int? colour)
    {
        var normalized = GitHubRepositoryRef.NormalizeAlias(alias);
        if (!Current.Repositories.Any(r => string.Equals(r.Alias, normalized, StringComparison.Ordinal)))
        {
            return "That repository is no longer configured.";
        }

        if (colour is not null && !RepositoryColours.IsSanctioned(colour))
        {
            return $"{colour} is not one of the {RepositoryColours.Available} colours.";
        }

        return Save(new GitHubSettings
        {
            Repositories =
            [
                .. Current.Repositories.Select(r =>
                    string.Equals(r.Alias, normalized, StringComparison.Ordinal)
                        ? r with { Colour = colour }
                        : r)
            ],
            ApiEndpoint = Current.ApiEndpoint
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
            ApiEndpoint = Current.ApiEndpoint
        });
    }

    private string? Save(GitHubSettings settings)
    {
        var normalized = new GitHubSettings
        {
            Repositories = NormalizeRepositories(settings.Repositories),
            Token = null,
            ApiEndpoint = CleanEndpoint(settings.ApiEndpoint) ?? GitHubSettings.DefaultApiEndpoint
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
                    Token = r.Token,
                    Colour = r.Colour,
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
                Token = null,
                ApiEndpoint = normalized.ApiEndpoint
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
                Repositories = NormalizeRepositories(
                [
                    .. dto.Repositories
                        .Where(r => !string.IsNullOrWhiteSpace(r.Owner) && !string.IsNullOrWhiteSpace(r.Name))
                        .Select(r => new GitHubRepositoryRef(
                            GitHubRepositoryRef.NormalizeAlias(string.IsNullOrWhiteSpace(r.Alias) ? r.Name! : r.Alias!),
                            r.Owner!,
                            r.Name!)
                        {
                            CloneDirectory = CleanPath(r.CloneDirectory),
                            Token = CleanToken(r.Token) ?? CleanToken(dto.Token),
                            Colour = CleanColour(r.Colour),
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
                Token = CleanToken(dto.Token),
                ApiEndpoint = CleanEndpoint(dto.ApiEndpoint) ?? GitHubSettings.DefaultApiEndpoint
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
            return repository with
            {
                Token = CleanToken(repository.Token) ?? CleanToken(Current.Token),
                Colour = CleanColour(repository.Colour),
                KnowledgeFolders = KnowledgeFolderSetting.Normalize(repository.KnowledgeFolders)
            };
        }

        return repository with
        {
            CloneDirectory = string.IsNullOrWhiteSpace(repository.CloneDirectory) ? existing.CloneDirectory : repository.CloneDirectory,
            Token = CleanToken(repository.Token) ?? existing.Token ?? CleanToken(Current.Token),
            Colour = CleanColour(repository.Colour) ?? existing.Colour,
            KnowledgeFolders = KnowledgeFolderSetting.Normalize(existing.KnowledgeFolders)
        };
    }

    private static List<GitHubRepositoryRef> NormalizeRepositories(IEnumerable<GitHubRepositoryRef> repositories) =>
    [
        .. repositories.Select(r => r with
        {
            CloneDirectory = CleanPath(r.CloneDirectory),
            Token = CleanToken(r.Token),
            Colour = CleanColour(r.Colour),
            KnowledgeFolders = KnowledgeFolderSetting.Normalize(r.KnowledgeFolders)
        })
    ];

    private static string? CleanPath(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private static string? CleanToken(string? token) => string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    /// <summary>A colour outside the sanctioned range is dropped rather than clamped,
    /// for the reason <c>.design/color-scheme.md</c> gives: clamping would hand somebody
    /// a hue they did not ask for and make it look like a choice they had made.</summary>
    private static int? CleanColour(int? colour) => RepositoryColours.IsSanctioned(colour) ? colour : null;

    private static string? CleanEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;

        var trimmed = endpoint.Trim();
        return trimmed.EndsWith("/", StringComparison.Ordinal)
            ? trimmed.TrimEnd('/')
            : trimmed;
    }

    private sealed class SettingsDto
    {
        public List<RepositoryDto> Repositories { get; set; } = [];
        public string? Token { get; set; }
        public string? ApiEndpoint { get; set; }
    }

    private sealed class RepositoryDto
    {
        public string? Alias { get; set; }
        public string? Owner { get; set; }
        public string? Name { get; set; }
        public string? CloneDirectory { get; set; }
        public string? Token { get; set; }
        public int? Colour { get; set; }
        public List<KnowledgeFolderDto> KnowledgeFolders { get; set; } = [];
    }

    private sealed class KnowledgeFolderDto
    {
        public string? Key { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Path { get; set; }
    }
}
