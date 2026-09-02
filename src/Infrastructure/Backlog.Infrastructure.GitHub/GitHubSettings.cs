using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Whether the repository identity hues are drawn at all.
    /// <para>
    /// Off unless somebody turned it on, and off is what an older settings file with no
    /// such property reads as. The hues are a layer over a workspace that reads
    /// perfectly well without them — every surface that carries one also carries the
    /// alias in words — so opting in is the honest default rather than opting out.
    /// </para>
    /// </summary>
    public bool ShowRepositoryColours { get; init; }

    /// <summary>
    /// The repository a name refers to, or null when nothing configured answers
    /// to it.
    /// <para>
    /// Dispatches on shape, and is the single seam every alias lookup in the app
    /// funnels through — <c>GitHubIntegration.ResolveRepository</c> included — so
    /// it carries the same rule <c>IRepositoryDirectory.Resolve</c> applies. A
    /// name containing a <c>/</c> is an <c>owner/name</c> identity and is matched
    /// against <see cref="GitHubRepositoryRef.FullName"/> without regard to case;
    /// anything else is an alias and is matched exactly, both sides having been
    /// through the same normalization.
    /// </para>
    /// <para>
    /// It used to try alias first and fall back to full name for every input.
    /// Dispatching instead of falling back is what stops an alias that happens
    /// to read like half a coordinate from shadowing a real one, and it is the
    /// reason an id can now be stored on an entry: the stored value resolves by
    /// the branch that is about identity rather than by luck.
    /// </para></summary>
    public GitHubRepositoryRef? Find(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return null;

        var trimmed = alias.Trim();

        if (trimmed.Contains('/', StringComparison.Ordinal))
        {
            return Repositories.FirstOrDefault(r => string.Equals(r.FullName, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        var normalized = GitHubRepositoryRef.NormalizeAlias(trimmed);
        return Repositories.FirstOrDefault(r => string.Equals(r.Alias, normalized, StringComparison.Ordinal));
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

    /// <summary>
    /// The hues the surfaces may draw: <see cref="Colours"/> when the visualization is
    /// on, and nothing at all when it is off.
    /// <para>
    /// The gate is here, beside the answer, for the reason the answer is here: a surface
    /// that decided for itself whether to draw its hue would be deciding for itself what
    /// the identity of a repository looks like, and five surfaces deciding separately is
    /// exactly what <c>.design/color-scheme.md#band-identity-tokens</c> forbids. One
    /// place answers "which hue", so one place answers "and is it shown".
    /// </para>
    /// <para>
    /// Empty rather than a dictionary of nulls, because empty is a shape every caller
    /// already handles: a repository missing from the map is one that gets no mark, which
    /// is precisely the presentation the off state wants.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, int> VisibleColours() =>
        ShowRepositoryColours ? Colours() : ReadOnlyDictionary<string, int>.Empty;

    /// <summary>The hue a surface may draw for one repository, or null when the alias
    /// names nothing configured <em>or</em> the visualization is off. The two reasons
    /// deliberately look the same to a caller: "no mark" is one state on screen, and a
    /// surface that could tell them apart would be a surface that could act on the
    /// difference.</summary>
    public int? VisibleColourFor(string? alias) => ShowRepositoryColours ? ColourFor(alias) : null;

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
/// Reads and writes <see cref="GitHubSettings"/> across the two files a
/// repository is configured in, and is the single façade over both.
/// <para>
/// A repository has a shared half and a machine half. Its identity — the
/// <c>owner/name</c> an entry files itself against, the alias somebody types and
/// reads, and the identity hue it wears — is workspace data: it belongs in the
/// backlog folder, which is the thing that gets synced, so the same repository
/// means the same thing on every install. Its clone directory and its token are
/// machine data: a path off another machine's <c>D:</c> reads as corruption
/// rather than as difference, and a token has no business travelling with a
/// synced folder.
/// </para>
/// <para>
/// So <c>{RootDirectory}/config/repos.json</c> holds the identity rows, and the
/// per-user <c>github.json</c> holds an overlay keyed on the id plus the two
/// settings that are about this install rather than about any repository — the
/// API endpoint, and whether the hues are drawn at all. Composing them happens
/// here rather than in a second injectable store, because every one of the
/// thirty-odd consumers asks the same question it always did and none of them
/// should have to learn that the answer now comes from two places.
/// </para>
/// <para>
/// The overlay keys on the id and not on the alias, deliberately. The alias is a
/// mutable label now; keying machine data on it would let a rename orphan a
/// clone directory or, worse, re-point a token at a different repository.
/// </para>
/// <para>
/// Follows the house rule of no save button: callers commit a whole value and it
/// is persisted immediately.
/// </para>
/// </summary>
public sealed class GitHubSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>The message a shared-file write is refused with while the
    /// registry cannot be read. It names the registry rather than the repository
    /// because that is the honest answer: "that repository is no longer
    /// configured" would be a guess, when the list of what is configured is
    /// exactly the thing that could not be read.</summary>
    private const string RegistryUnreadable =
        "The repository registry couldn't be read, so repositories can't be changed right now.";

    private const string SaveFailed = "Changed, but the choice couldn't be saved for next time.";

    private readonly string _path;
    private readonly Func<string> _rootDirectory;

    /// <summary>The local file's repository rows exactly as they were last read
    /// or written, kept so a write while the registry is unreadable can put back
    /// what it does not have the standing to prune.</summary>
    private List<RepositoryDto> _localRows = [];

    private RegistryState _registryState;

    /// <summary>Where the per-user file sits when nothing overrides it. Named so
    /// a host can compose the two-argument form without restating the path, and
    /// so the parameterless constructor and that composition cannot drift
    /// apart.</summary>
    public static string DefaultLocalPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Backlog",
        "github.json");

    public GitHubSettingsStore()
        : this(DefaultLocalPath)
    {
    }

    /// <summary>
    /// The one-path form, kept exactly as it was: the local file goes where it is
    /// told and the registry goes in a <c>config</c> folder beside it.
    /// <para>
    /// Preserved rather than replaced because every test and the web harness's
    /// local-development store name one path and mean "a whole isolated
    /// configuration, here", and that is still what they get. A host that wants
    /// the registry to follow the workspace root passes the two-argument form.
    /// </para>
    /// </summary>
    public GitHubSettingsStore(string path)
        : this(path, () => Path.GetDirectoryName(path)!)
    {
    }

    /// <summary>
    /// The composed form: a fixed per-user file, and the workspace root the
    /// shared registry sits under, read per call.
    /// <para>
    /// A <see cref="Func{TResult}"/> rather than the workspace store itself, for
    /// the reason the architecture leaves no alternative:
    /// <c>Backlog.Infrastructure.FileSystem</c> references this project, so
    /// taking <c>WorkspaceSettingsStore</c> here would cycle. It is also the
    /// pattern both hosts already use twice for <c>RootedSqliteTaskRepository</c>
    /// and the roadmap plan — read the root per call, so pointing the app at a
    /// different folder takes effect without restarting it.
    /// </para>
    /// </summary>
    public GitHubSettingsStore(string localPath, Func<string> rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(localPath);
        ArgumentNullException.ThrowIfNull(rootDirectory);

        _path = localPath;
        _rootDirectory = rootDirectory;
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        Current = Load();
    }

    /// <summary>Raised after the configuration changes, so open views and any
    /// cached connection can react.</summary>
    public event Action? Changed;

    public GitHubSettings Current { get; private set; }

    /// <summary>Where the per-user file lives, shown in Settings so it can be
    /// found (and so it is obvious the token is not in the backlog
    /// folder).</summary>
    public string SettingsPath => _path;

    /// <summary>Where the shared registry lives. Shown in Settings when
    /// <see cref="RegistryError"/> says it could not be read, because a file
    /// somebody has to go and look at is worth naming; the ordinary case does not
    /// need the path and does not show it.</summary>
    public string RegistryPath => Path.Combine(_rootDirectory(), "config", "repos.json");

    /// <summary>Why the shared registry could not be read, or null when it was
    /// read or is simply not there yet. A workspace whose registry is corrupt
    /// still opens; this is what lets Settings say so, instead of showing an
    /// empty repository list as though somebody had cleared it.</summary>
    public string? RegistryError { get; private set; }

    /// <summary>
    /// Re-reads both files against whatever root the provider now answers with,
    /// re-runs the carry-over, and announces the change like any other.
    /// <para>
    /// Wired to the workspace's <c>RootChanged</c> by both hosts. Moving the
    /// backlog folder moves the registry with it, exactly as it moves the task
    /// database and the roadmap plan, so the repositories on screen have to
    /// become the new workspace's repositories rather than stay the old one's.
    /// </para>
    /// </summary>
    public void Reload()
    {
        Current = Load();
        Changed?.Invoke();
    }

    /// <summary>Replaces the configured repositories. Returns an error message
    /// when persisting failed; the in-memory value is updated either way so the
    /// session still works.</summary>
    public string? SetRepositories(IEnumerable<GitHubRepositoryRef> repositories)
    {
        if (_registryState is RegistryState.Unreadable) return RegistryUnreadable;

        return Save(new GitHubSettings
        {
            Repositories = NormalizeRepositories([.. repositories.Select(PreserveExistingRepositorySettings)]),
            ApiEndpoint = Current.ApiEndpoint,
            ShowRepositoryColours = Current.ShowRepositoryColours
        });
    }

    public string? SetRepositoryToken(string alias, string? token)
    {
        if (Find(alias) is not { } target) return NotConfigured;

        return Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories.Select(r => IsSame(r, target) ? r with { Token = CleanToken(token) } : r)],
            ApiEndpoint = Current.ApiEndpoint,
            ShowRepositoryColours = Current.ShowRepositoryColours
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
            ApiEndpoint = CleanEndpoint(apiEndpoint) ?? GitHubSettings.DefaultApiEndpoint,
            ShowRepositoryColours = Current.ShowRepositoryColours
        });

    public string? RemoveRepository(string alias)
    {
        if (_registryState is RegistryState.Unreadable) return RegistryUnreadable;
        if (Find(alias) is not { } target) return NotConfigured;

        // The overlay row goes with it, which the reduced write does by itself: it
        // emits a row per repository that is still configured, so an id that is
        // gone is simply not written. That matters most for the token — a secret
        // left behind after "remove" is a secret nobody knows is there.
        return Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories.Where(r => !IsSame(r, target))],
            ApiEndpoint = Current.ApiEndpoint,
            ShowRepositoryColours = Current.ShowRepositoryColours
        });
    }

    public string? SetCloneDirectory(string alias, string? cloneDirectory)
    {
        if (Find(alias) is not { } target) return NotConfigured;

        return Save(new GitHubSettings
        {
            Repositories =
            [
                .. Current.Repositories.Select(r => IsSame(r, target)
                    ? r with { CloneDirectory = CleanPath(cloneDirectory) }
                    : r)
            ],
            ApiEndpoint = Current.ApiEndpoint,
            ShowRepositoryColours = Current.ShowRepositoryColours
        });
    }

    /// <summary>
    /// Records which identity hue a repository wears, or clears the choice so the hue
    /// falls back to the repository's position. Follows the house rule of no save
    /// button: the choice is persisted as it is made.
    /// <para>
    /// A shared write, because the hue is part of what a repository <em>is</em>:
    /// <c>RepositoryColours.Resolve</c> takes an unchosen hue from list position,
    /// so the answer already depends on the shared ordered list, and a hue that
    /// differed per install would be exactly the several-answers-to-one-question
    /// that <c>.design/color-scheme.md#band-identity-tokens</c> forbids.
    /// </para>
    /// </summary>
    public string? SetRepositoryColour(string alias, int? colour)
    {
        if (_registryState is RegistryState.Unreadable) return RegistryUnreadable;
        if (Find(alias) is not { } target) return NotConfigured;

        if (colour is not null && !RepositoryColours.IsSanctioned(colour))
        {
            return $"{colour} is not one of the {RepositoryColours.Available} colours.";
        }

        return Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories.Select(r => IsSame(r, target) ? r with { Colour = colour } : r)],
            ApiEndpoint = Current.ApiEndpoint,
            ShowRepositoryColours = Current.ShowRepositoryColours
        });
    }

    /// <summary>
    /// Shows or hides the repository identity hues across the whole app. Follows the
    /// house rule of no save button: the choice is persisted as it is made, and the
    /// in-memory value changes either way so a workspace whose settings file could not
    /// be written still does what was asked of it for the rest of the session.
    /// <para>
    /// Local, unlike the hue itself. It is not a property of any repository — the
    /// shared schema is a list of repositories — so it sits beside the API
    /// endpoint. The asymmetry is worth saying out loud: the hue a repository
    /// wears is its identity and is shared; the decision to draw hues at all is
    /// this install's own.
    /// </para>
    /// </summary>
    public string? SetShowRepositoryColours(bool show) =>
        Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories],
            ApiEndpoint = Current.ApiEndpoint,
            ShowRepositoryColours = show
        });

    /// <summary>
    /// Points one of a repository's knowledge folders somewhere else, or turns it
    /// off.
    /// <para>
    /// Local, and the whole list stays local rather than being split down the
    /// middle. Every repository knowledge resolution is gated on the clone
    /// directory — <c>KnowledgeFolderSource.ResolveRepository</c> answers
    /// <c>Unavailable</c> for a blank one — so a shared <c>enabled</c> would have
    /// no shared consequence; <c>KnowledgeFolderSetting</c> is Second Brain's
    /// published language and the shared registry has to stay a Repository
    /// Management artifact; and splitting one row across two files would make this
    /// a two-file write whose partial failure leaves an inconsistent row.
    /// </para>
    /// </summary>
    public string? SetKnowledgeFolder(string alias, string key, bool enabled, string? path)
    {
        if (Find(alias) is not { } target) return NotConfigured;

        return Save(new GitHubSettings
        {
            Repositories =
            [
                .. Current.Repositories.Select(r => IsSame(r, target)
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
            ApiEndpoint = Current.ApiEndpoint,
            ShowRepositoryColours = Current.ShowRepositoryColours
        });
    }

    private const string NotConfigured = "That repository is no longer configured.";

    /// <summary>The repository a mutator's key names. All six repository mutators
    /// still take an alias, still resolve it through the one lookup the whole app
    /// uses, and then act on the resolved row's identity — so Settings and the
    /// roadmap band are unchanged, refusal message included, while the thing being
    /// changed is keyed on something that does not move.</summary>
    private GitHubRepositoryRef? Find(string alias) => Current.Find(alias);

    /// <summary>Two references to one repository. Compared on the id rather than
    /// the alias, because the alias is exactly the value a rename moves.</summary>
    private static bool IsSame(GitHubRepositoryRef left, GitHubRepositoryRef right) =>
        string.Equals(left.FullName, right.FullName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies a whole value in memory, then persists it: the shared registry
    /// first, then the local file.
    /// <para>
    /// Both are attempted even when the first fails, and the one existing message
    /// is returned when either did. That keeps today's contract verbatim — the
    /// change takes effect for this session and the person is told it may not
    /// survive a restart — and it avoids the worse outcome of skipping the second
    /// write, which would leave the two files describing different configurations
    /// for no gain.
    /// </para>
    /// <para>
    /// Every mutator comes through here, so every save writes both files even when
    /// only one half changed. The routing table in the design says which half a
    /// mutator <em>changes</em>, not which file it touches: rewriting identical
    /// identity rows costs one small file and removes the class of bug where a
    /// mutator forgets which file its field lives in.
    /// </para>
    /// </summary>
    private string? Save(GitHubSettings settings)
    {
        var normalized = new GitHubSettings
        {
            Repositories = NormalizeRepositories(settings.Repositories),
            Token = null,
            ApiEndpoint = CleanEndpoint(settings.ApiEndpoint) ?? GitHubSettings.DefaultApiEndpoint,
            ShowRepositoryColours = settings.ShowRepositoryColours
        };
        Current = normalized;

        string? error = null;

        // Not attempted while the registry is unreadable: writing over a file that
        // could not be parsed would destroy whatever it holds, and the mutators
        // that would have changed it have already refused.
        if (_registryState is not RegistryState.Unreadable && WriteRegistry(normalized) is not null)
        {
            error = SaveFailed;
        }

        if (WriteLocal(normalized) is not null) error = SaveFailed;

        Changed?.Invoke();
        return error;
    }

    /// <summary>The shared half: one row per repository, holding the id, the alias
    /// and the chosen hue and nothing else. No token ever reaches this file, and no
    /// clone path.</summary>
    private string? WriteRegistry(GitHubSettings settings) =>
        WriteRegistryRows(
        [
            .. settings.Repositories.Select(r => new RegistryRepositoryDto
            {
                Id = r.FullName,
                Alias = r.Alias,
                Colour = r.Colour
            })
        ]);

    private string? WriteRegistryRows(List<RegistryRepositoryDto> rows)
    {
        try
        {
            var path = RegistryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllText(path, JsonSerializer.Serialize(new RegistryDto { Repositories = rows }, JsonOptions));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return SaveFailed;
        }
    }

    /// <summary>The machine half, plus the two settings that are about this
    /// install rather than about a repository.</summary>
    private string? WriteLocal(GitHubSettings settings)
    {
        var rows = LocalRowsFor(settings);

        try
        {
            var dto = new SettingsDto
            {
                Repositories = rows,
                Token = null,
                ApiEndpoint = settings.ApiEndpoint,
                ShowRepositoryColours = settings.ShowRepositoryColours
            };

            File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOptions));

            // What is on disk is now what a later write has to preserve, so the
            // legacy fields this write dropped are not put back by the next one.
            _localRows = rows;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SaveFailed;
        }
    }

    /// <summary>
    /// The reduced local rows: an id, and whatever this machine knows about that
    /// repository.
    /// <para>
    /// The legacy identity fields are never emitted. They are read so an older
    /// file still opens and can be carried over, and dropped by the next write —
    /// the same per-field treatment the legacy global token already gets, where
    /// the value is read and migrated onto the rows but written back as null.
    /// </para>
    /// <para>
    /// A repository this machine knows nothing about gets no row at all. That is
    /// the ordinary shape of a repository somebody registered on another install,
    /// and an id-only row would claim otherwise.
    /// </para>
    /// <para>
    /// Rows for ids that are no longer configured are simply not written, which is
    /// the pruning <see cref="SetRepositories"/> and <see cref="RemoveRepository"/>
    /// owe: a token is a secret, and leaving one behind after the repository it
    /// belongs to is gone is worse than losing the clone path beside it. The one
    /// exception is an unreadable registry, where the in-memory list is not a
    /// statement about what is configured and pruning against it would delete
    /// tokens for repositories that are perfectly fine.
    /// </para>
    /// </summary>
    private List<RepositoryDto> LocalRowsFor(GitHubSettings settings)
    {
        var preserve = _registryState is RegistryState.Unreadable;

        var rows = new List<RepositoryDto>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var repository in settings.Repositories)
        {
            var existing = FindLocalRow(repository.FullName);
            if (!CarriesMachineLocalData(repository) && !preserve) continue;

            var row = new RepositoryDto
            {
                Id = repository.FullName,
                CloneDirectory = repository.CloneDirectory,
                Token = repository.Token,
                KnowledgeFolders =
                [
                    .. KnowledgeFolderSetting.Normalize(repository.KnowledgeFolders).Select(f => new KnowledgeFolderDto
                    {
                        Key = f.Key,
                        Enabled = f.Enabled,
                        Path = f.Path
                    })
                ]
            };

            if (preserve && existing is not null)
            {
                // While the registry cannot be read, these fields are the only
                // record that this repository exists at all, so the write that
                // normally drops them keeps them until the registry can be read.
                row.Alias = existing.Alias;
                row.Owner = existing.Owner;
                row.Name = existing.Name;
                row.Colour = existing.Colour;
            }

            rows.Add(row);
            written.Add(repository.FullName);
        }

        if (preserve)
        {
            rows.AddRange(_localRows.Where(row => ExplicitIdOf(row) is { } id && !written.Contains(id)));
        }

        return rows;
    }

    /// <summary>Whether a repository has anything machine-local to remember at
    /// all. The knowledge folders are compared against the defaults rather than
    /// merely counted, because "every section on, at its conventional folder" is
    /// already what a repository nobody has configured folders for
    /// answers.</summary>
    private static bool CarriesMachineLocalData(GitHubRepositoryRef repository) =>
        repository.CloneDirectory is not null
        || repository.Token is not null
        || !KnowledgeFolderSetting.Normalize(repository.KnowledgeFolders)
            .SequenceEqual(KnowledgeFolderSetting.Defaults());

    /// <summary>
    /// Reads both files, carries a legacy local file over into the registry, and
    /// composes the one value every consumer sees.
    /// <para>
    /// Eager rather than lazy. <see cref="Current"/> has to be right before the
    /// first consumer touches it, and there are about thirty of them across the
    /// settings screen, the entry list, the roadmap band and the push flow — a
    /// migration that ran when somebody happened to open one screen would leave
    /// the other twenty-nine reading a half-migrated workspace.
    /// </para>
    /// </summary>
    private GitHubSettings Load()
    {
        var local = ReadLocal();
        _localRows = local.Rows;

        var registry = ReadRegistry();
        _registryState = registry.State;
        RegistryError = registry.Error;

        var rows = registry.Rows;
        var carriedOver = false;

        if (_registryState is not RegistryState.Unreadable)
        {
            var withCarryOver = WithCarryOver(rows, local.Rows);
            if (withCarryOver.Count != rows.Count)
            {
                // The shared write comes first and the reduced local write only
                // follows a successful one. If the registry could not be written,
                // the legacy file stays exactly as it is and the next start tries
                // again — a failure here must not be the thing that loses the
                // repositories it was carrying.
                carriedOver = WriteRegistryRows(
                [
                    .. withCarryOver.Select(row => new RegistryRepositoryDto
                    {
                        Id = row.Id,
                        Alias = row.Alias,
                        Colour = row.Colour
                    })
                ]) is null;

                // Either way the session runs on the carried-over list, so a
                // read-only workspace still shows the repositories it has.
                rows = withCarryOver;
            }
        }

        var composed = Compose(rows, local);

        if (carriedOver) _ = WriteLocal(composed);

        return composed;
    }

    /// <summary>
    /// The registry rows, plus one for every legacy local row the registry does
    /// not already have.
    /// <para>
    /// Gap-filling only: an id already present is left exactly as the registry
    /// holds it — alias, hue, position and all — so install #2 never reorders or
    /// renames what install #1 chose. The gate is per repository rather than per
    /// file, which is what makes it idempotent <em>and</em> what lets an install
    /// whose synced workspace already has a registry still contribute the one
    /// repository only it ever had.
    /// </para>
    /// <para>
    /// Bounded hazard, accepted: a repository deliberately removed on install #1
    /// can be resurrected by install #2's stale legacy file. The window is one
    /// session per install and is closed by the reduced write immediately after a
    /// successful carry-over. If that write fails the window stays open — degrade,
    /// do not fail.
    /// </para>
    /// </summary>
    private static List<RegistryRow> WithCarryOver(List<RegistryRow> registry, List<RepositoryDto> localRows)
    {
        var carried = new List<RegistryRow>(registry);

        foreach (var row in localRows)
        {
            if (IdentityOf(row) is not { } identity) continue;
            if (carried.Any(known => string.Equals(known.Id, identity.Id, StringComparison.OrdinalIgnoreCase))) continue;

            carried.Add(identity);
        }

        return carried;
    }

    /// <summary>
    /// Joins the identity rows to the local overlay, which is the value every
    /// consumer of this store has always seen.
    /// <para>
    /// While the registry is unreadable the identities come from the local file's
    /// legacy fields instead, so an un-migrated install keeps working exactly as
    /// it did before the split. An already-migrated one has nothing to fall back
    /// on and shows an empty list, with <see cref="RegistryError"/> saying why.
    /// </para>
    /// </summary>
    private GitHubSettings Compose(List<RegistryRow> rows, LocalFile local)
    {
        var identities = _registryState is RegistryState.Unreadable
            ? LegacyIdentities(local.Rows)
            : rows;

        return new GitHubSettings
        {
            Repositories = NormalizeRepositories(
            [
                .. identities.Select(identity =>
                {
                    var overlay = FindOverlay(local.Rows, identity);

                    return new GitHubRepositoryRef(identity.Alias, identity.Owner, identity.Name)
                    {
                        CloneDirectory = CleanPath(overlay?.CloneDirectory),
                        Token = CleanToken(overlay?.Token) ?? CleanToken(local.Token),
                        Colour = CleanColour(identity.Colour),

                        // A repository with no overlay row starts from the defaults,
                        // which is exactly where a repository registered on another
                        // install has to start.
                        KnowledgeFolders = KnowledgeFolderSetting.Normalize(
                            (overlay?.KnowledgeFolders ?? []).Select(f => new KnowledgeFolderSetting(
                                string.IsNullOrWhiteSpace(f.Key) ? string.Empty : f.Key!,
                                string.Empty,
                                string.Empty)
                            {
                                Enabled = f.Enabled,
                                Path = f.Path
                            }))
                    };
                })
            ]),
            Token = CleanToken(local.Token),
            ApiEndpoint = CleanEndpoint(local.ApiEndpoint) ?? GitHubSettings.DefaultApiEndpoint,
            ShowRepositoryColours = local.ShowRepositoryColours
        };
    }

    /// <summary>
    /// The identities a legacy local file states, first one wins per id.
    /// <para>
    /// Only the pre-split <c>owner</c>/<c>name</c> rows count, deliberately. An
    /// <c>id</c> is the overlay's key rather than a statement of identity — it
    /// names no alias and no hue — so a row that has one and nothing else says
    /// which repository this machine knows something about, not what that
    /// repository is called. Composing from it would invent a label the shared
    /// file, once readable again, may well disagree with.
    /// </para>
    /// </summary>
    private static List<RegistryRow> LegacyIdentities(List<RepositoryDto> localRows)
    {
        var identities = new List<RegistryRow>();

        foreach (var row in localRows)
        {
            if (string.IsNullOrWhiteSpace(row.Owner) || string.IsNullOrWhiteSpace(row.Name)) continue;
            if (IdentityOf(row) is not { } identity) continue;
            if (identities.Any(known => string.Equals(known.Id, identity.Id, StringComparison.OrdinalIgnoreCase))) continue;

            identities.Add(identity);
        }

        return identities;
    }

    /// <summary>The local row that belongs to one identity, matched on the id
    /// without regard to case. A file written before the split states
    /// <c>owner</c> and <c>name</c> rather than <c>id</c>, which
    /// <see cref="ExplicitIdOf"/> reads as the same coordinate; a row that states
    /// neither is matched by alias, as a legacy fallback only, because keying on
    /// the alias is the very thing the split stopped doing.</summary>
    private static RepositoryDto? FindOverlay(List<RepositoryDto> localRows, RegistryRow identity)
    {
        var byId = localRows.FirstOrDefault(row =>
            ExplicitIdOf(row) is { } id && string.Equals(id, identity.Id, StringComparison.OrdinalIgnoreCase));

        if (byId is not null) return byId;

        return localRows.FirstOrDefault(row =>
            ExplicitIdOf(row) is null
            && !string.IsNullOrWhiteSpace(row.Alias)
            && string.Equals(GitHubRepositoryRef.NormalizeAlias(row.Alias!), identity.Alias, StringComparison.Ordinal));
    }

    /// <summary>The row's own id: the <c>id</c> field, or the legacy
    /// <c>owner</c>/<c>name</c> pair it replaced. Null when the row states
    /// neither, which is a row that can only be matched by alias.</summary>
    private static string? ExplicitIdOf(RepositoryDto row)
    {
        if (!string.IsNullOrWhiteSpace(row.Id)) return row.Id!.Trim();

        return string.IsNullOrWhiteSpace(row.Owner) || string.IsNullOrWhiteSpace(row.Name)
            ? null
            : $"{row.Owner!.Trim()}/{row.Name!.Trim()}";
    }

    /// <summary>One local row read as an identity row, or null when it states no
    /// coordinate. The alias falls back to the repository name, which is what a
    /// configured line with no explicit alias has always meant.</summary>
    private static RegistryRow? IdentityOf(RepositoryDto row) =>
        ExplicitIdOf(row) is { } id
            ? RegistryRow.From(id, row.Alias, CleanColour(row.Colour))
            : null;

    private (RegistryState State, List<RegistryRow> Rows, string? Error) ReadRegistry()
    {
        try
        {
            var path = RegistryPath;

            // Missing is the ordinary first-run and fresh-workspace state, and it
            // is writable: the next save creates the file. Deliberately not an
            // error, so nothing tells somebody about a problem they do not have.
            if (!File.Exists(path)) return (RegistryState.Missing, [], null);

            var dto = JsonSerializer.Deserialize<RegistryDto>(File.ReadAllText(path), JsonOptions);
            if (dto is null) return (RegistryState.Missing, [], null);

            return (RegistryState.Loaded, [.. dto.Repositories.Select(row => RegistryRow.From(row)).OfType<RegistryRow>()], null);
        }
        catch (Exception)
        {
            // A corrupt settings file must never stop the app from opening. Unlike
            // the local file, though, this one cannot simply read as empty: an
            // empty shared registry would prune every overlay row and refuse
            // nothing. So the state is remembered, and the writes that would act
            // on a list nobody has are refused instead.
            return (RegistryState.Unreadable, [], RegistryUnreadable);
        }
    }

    private LocalFile ReadLocal()
    {
        try
        {
            if (!File.Exists(_path)) return new LocalFile();

            var dto = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return new LocalFile();

            return new LocalFile
            {
                Rows = dto.Repositories,
                Token = dto.Token,
                ApiEndpoint = dto.ApiEndpoint,
                ShowRepositoryColours = dto.ShowRepositoryColours
            };
        }
        catch (Exception)
        {
            // A corrupt settings file must never stop the app from opening.
            return new LocalFile();
        }
    }

    private RepositoryDto? FindLocalRow(string id) =>
        _localRows.FirstOrDefault(row =>
            ExplicitIdOf(row) is { } rowId && string.Equals(rowId, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Carries the machine half of a repository across a re-typed list, keyed on
    /// the id.
    /// <para>
    /// It used to match alias-or-full-name, which meant an alias rename preserved
    /// a clone directory by luck. Keying on the id preserves it by definition, and
    /// only a changed <c>owner/name</c> — a genuinely different repository — loses
    /// it.
    /// </para>
    /// </summary>
    private GitHubRepositoryRef PreserveExistingRepositorySettings(GitHubRepositoryRef repository)
    {
        var existing = Current.Repositories.FirstOrDefault(r => IsSame(r, repository));

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

    /// <summary>
    /// What reading the shared registry found. Three states rather than two,
    /// because "not there" and "there but unreadable" want opposite treatment: the
    /// first is writable and may be carried over into, the second must never be
    /// written over, must never prune an overlay row, and must refuse the writes
    /// that would act on a list it does not have.
    /// </summary>
    private enum RegistryState
    {
        Missing,
        Loaded,
        Unreadable
    }

    /// <summary>One identity row, already split into the parts the rest of the
    /// class needs.</summary>
    private sealed record RegistryRow(string Id, string Alias, string Owner, string Name, int? Colour)
    {
        public static RegistryRow? From(RegistryRepositoryDto dto) => From(dto.Id, dto.Alias, CleanColour(dto.Colour));

        /// <summary>
        /// A stored row read as an identity, or null when its <c>id</c> is not a
        /// repository coordinate.
        /// <para>
        /// Dropped rather than repaired, which is the tolerance the local file has
        /// always had for a row missing its owner or name. The id is one string in
        /// the file on purpose — the registry is the authority on the value
        /// <c>repo_ids</c> stores, so it stores that value byte for byte — and
        /// owner and name derive from the single <c>/</c> it has to contain.
        /// </para>
        /// </summary>
        public static RegistryRow? From(string? id, string? alias, int? colour)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            var parts = id.Trim().Split('/');
            if (parts.Length != 2) return null;

            var owner = parts[0].Trim();
            var name = parts[1].Trim();
            if (owner.Length == 0 || name.Length == 0) return null;

            return new RegistryRow(
                $"{owner}/{name}",
                GitHubRepositoryRef.NormalizeAlias(string.IsNullOrWhiteSpace(alias) ? name : alias!),
                owner,
                name,
                colour);
        }
    }

    /// <summary>The local file as it was read: the overlay rows untouched, and the
    /// fields that are about this install.</summary>
    private sealed class LocalFile
    {
        public List<RepositoryDto> Rows { get; init; } = [];
        public string? Token { get; init; }
        public string? ApiEndpoint { get; init; }
        public bool ShowRepositoryColours { get; init; }
    }

    private sealed class RegistryDto
    {
        public List<RegistryRepositoryDto> Repositories { get; set; } = [];
    }

    /// <summary>
    /// One row of the shared registry: the identity, the label, the hue. There is
    /// no version field — nothing else in this repository has one, and both
    /// migrations are idempotent without one, so a version would be a thing to
    /// maintain that gates nothing.
    /// </summary>
    private sealed class RegistryRepositoryDto
    {
        public string? Id { get; set; }
        public string? Alias { get; set; }
        public int? Colour { get; set; }
    }

    private sealed class SettingsDto
    {
        public List<RepositoryDto> Repositories { get; set; } = [];
        public string? Token { get; set; }
        public string? ApiEndpoint { get; set; }

        /// <summary>Defaults to false, which is what makes a file written before the
        /// visualization existed read as off rather than as anything having to be
        /// migrated.</summary>
        public bool ShowRepositoryColours { get; set; }
    }

    /// <summary>
    /// One row of the per-user file: the id it belongs to, and the machine half.
    /// <para>
    /// <see cref="Alias"/>, <see cref="Owner"/>, <see cref="Name"/> and
    /// <see cref="Colour"/> are FROZEN LEGACY FIELDS. They are read so a file
    /// written before the registry existed still opens and can be carried over,
    /// and they are never written again — the same per-field treatment
    /// <see cref="SettingsDto.Token"/> already gets, where the value is read and
    /// migrated onto the rows but written back as null. Do not add to them, and do
    /// not start emitting them: identity lives in the shared registry now, and a
    /// local file that also stated it would be a second answer to the one question
    /// the split exists to give one answer to.
    /// </para>
    /// </summary>
    private sealed class RepositoryDto
    {
        public string? Id { get; set; }
        public string? CloneDirectory { get; set; }
        public string? Token { get; set; }
        public List<KnowledgeFolderDto> KnowledgeFolders { get; set; } = [];

        // Omitted when null rather than written as null, which is what makes the
        // reduced write actually reduced: a `"alias": null` in the file would
        // still be the local file having an opinion about identity. They are only
        // ever non-null on the one path that deliberately preserves them, while
        // the registry cannot be read.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Alias { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Owner { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Colour { get; set; }
    }

    private sealed class KnowledgeFolderDto
    {
        public string? Key { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Path { get; set; }
    }
}
