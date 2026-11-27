using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Modules.Repositories.Abstractions.Environments;
using Backlog.Modules.Repositories.Abstractions.Services;

namespace Backlog.Infrastructure.GitHub.Repositories;

/// <summary>
/// The registry's named environments (local ADR 0073 §3), kept on this device in
/// <c>environments.json</c> beside the registry's per-device file, <c>github.json</c>.
/// <para>
/// Per device for the reason the clone directory is: a launch link is often a command
/// run in this machine's clone, a folder under it, or a local dashboard, and sync never
/// carries it. Keyed on the repository's <c>owner/name</c> rather than on its alias, as
/// <c>github.json</c> is, and read through the registry's renames, so a renamed
/// repository keeps its environments (local ADR 0060) and a removed one's stay keyed to
/// it rather than moving to the no-repository group.
/// </para>
/// <para>
/// A file that cannot be read answers null rather than no environments, so the module
/// keeps what it holds and never writes over a file that is there; a row a later build
/// wrote with a type or link kind this one does not know is dropped alone. A write that
/// fails keeps the edit for this session and says so.
/// </para>
/// </summary>
// Not on JsonFileStore<T>: it writes through a temporary file and a move, as the pins store does.
public sealed class EnvironmentsFileStore : IEnvironmentStore
{
    /// <summary>The file's name, beside <c>github.json</c>.</summary>
    public const string FileName = "environments.json";

    public const string SaveFailed = "Changed, but the environments couldn't be saved for next time.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly GitHubSettingsStore? _registry;

    /// <summary>Beside <paramref name="registry"/>'s per-device file, following its
    /// renames.</summary>
    public EnvironmentsFileStore(GitHubSettingsStore registry)
        : this(Path.Combine(Path.GetDirectoryName(registry.SettingsPath)!, FileName), registry)
    {
    }

    /// <param name="path">The file.</param>
    /// <param name="registry">The registry whose renames a repository's key follows;
    /// null follows none.</param>
    public EnvironmentsFileStore(string path, GitHubSettingsStore? registry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _registry = registry;

        // A rename changes which repository an old key means, so what Load answers.
        if (registry is not null) registry.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    /// <summary>Where the environments are written.</summary>
    public string FilePath => _path;

    public IReadOnlyList<StoredEnvironment>? Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];

            var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(_path), JsonOptions);
            var renames = _registry?.Current;

            // A row that names no environment is dropped rather than repaired.
            return [.. (dto?.Environments ?? [])
                .Where(row => row.Id != Guid.Empty
                    && !string.IsNullOrWhiteSpace(row.Name)
                    && !string.IsNullOrWhiteSpace(row.Link)
                    && Parse<EnvironmentType>(row.Type) is not null
                    && Parse<LaunchLinkKind>(row.LinkKind) is not null)
                .Select(row => new StoredEnvironment(
                    string.IsNullOrWhiteSpace(row.Repository)
                        ? EnvironmentHome.NoRepository
                        : EnvironmentHome.Repository(renames?.RenamedTo(row.Repository) ?? row.Repository),
                    new RepositoryEnvironment(
                        row.Id,
                        row.Name!.Trim(),
                        Parse<EnvironmentType>(row.Type)!.Value,
                        new LaunchLink(Parse<LaunchLinkKind>(row.LinkKind)!.Value, row.Link!.Trim()),
                        [.. (row.AccessHints ?? []).Where(hint => !string.IsNullOrWhiteSpace(hint)).Select(hint => new AccessHint(hint.Trim()))],
                        row.Pinned,
                        row.Hidden)))
                .DistinctBy(stored => stored.Environment.Id)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable file must never stop the app from opening, and
            // must never read as an empty list the next write would make true.
            return null;
        }
    }

    public string? Save(IReadOnlyList<StoredEnvironment> environments)
    {
        ArgumentNullException.ThrowIfNull(environments);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Written beside the file and moved over it, so a write cut short leaves the
            // previous list rather than half a file that reads as none.
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new FileDto
            {
                Environments = [.. environments.Select(stored => new EnvironmentDto
                {
                    Id = stored.Environment.Id,
                    Repository = stored.Home.RepositoryId,
                    Name = stored.Environment.Name,
                    Type = Write(stored.Environment.Type),
                    LinkKind = Write(stored.Environment.Link.Kind),
                    Link = stored.Environment.Link.Value,
                    AccessHints = stored.Environment.AccessHints.Count == 0
                        ? null
                        : [.. stored.Environment.AccessHints.Select(hint => hint.Text)],
                    Pinned = stored.Environment.Pinned,
                    Hidden = stored.Environment.Hidden
                })]
            }, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SaveFailed;
        }
    }

    /// <summary>An enum as the file writes it: kebab-case, <c>workspace-path</c>.</summary>
    private static string Write<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString());

    /// <summary>An enum as the file holds it, or null for one this build does not know.</summary>
    private static T? Parse<T>(string? value) where T : struct, Enum =>
        Enum.GetValues<T>().Cast<T?>().FirstOrDefault(member => Write(member!.Value) == value?.Trim());

    private sealed class FileDto
    {
        public List<EnvironmentDto> Environments { get; set; } = [];
    }

    private sealed class EnvironmentDto
    {
        public Guid Id { get; set; }

        /// <summary>The repository's <c>owner/name</c>; absent for the no-repository group.</summary>
        public string? Repository { get; set; }

        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? LinkKind { get; set; }
        public string? Link { get; set; }
        public List<string>? AccessHints { get; set; }
        public bool Pinned { get; set; }
        public bool Hidden { get; set; }
    }
}
