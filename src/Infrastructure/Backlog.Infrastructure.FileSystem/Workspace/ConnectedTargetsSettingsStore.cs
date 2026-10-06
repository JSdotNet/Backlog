using System.Text.Json;

using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps the repositories and products connected for linked tasks, their
/// settings and how far each one's sync has got, in a JSON file next to the app's
/// other per-user settings.
/// <para>
/// Its own file, for the reason <see cref="CaptureSourcesSettingsStore"/> gives:
/// <c>settings.json</c> is the pointer to the workspace, and this one is rewritten
/// after every sync. Per machine and never in the workspace, because the progress
/// it holds is this machine's — another desktop syncing the same repository keeps
/// its own, and the deterministic task ids are what keep the two from making the
/// same task twice.
/// </para>
/// <para>
/// Targets and settings only. Never a credential: the file is plain text meant to
/// be read and hand-edited, and a token belongs in the platform's credential store.
/// </para>
/// </summary>
public sealed class ConnectedTargetsSettingsStore : IConnectedTargets
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Lock _gate = new();
    private readonly string _path;
    private List<ConnectedTarget> _targets;

    public ConnectedTargetsSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "connected-targets.json"))
    {
    }

    /// <summary>Names the settings file separately from the per-user location, for
    /// a test or the web harness — see <see cref="CaptureSourcesSettingsStore(string)"/>.</summary>
    public ConnectedTargetsSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _targets = Read();
    }

    public event Action? Changed;

    public string SettingsPath => _path;

    public IReadOnlyList<ConnectedTarget> List()
    {
        lock (_gate) return [.. _targets];
    }

    public ConnectedTarget? Get(string connectorId, string target)
    {
        lock (_gate) return _targets.FirstOrDefault(candidate => candidate.Is(connectorId, target));
    }

    public string? Save(ConnectedTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ConnectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Target);

        var cleaned = target with { ConnectorId = target.ConnectorId.Trim(), Target = target.Target.Trim() };

        string? error;
        lock (_gate)
        {
            var index = _targets.FindIndex(candidate => candidate.Is(cleaned.ConnectorId, cleaned.Target));
            if (index >= 0)
            {
                if (_targets[index] == cleaned) return null;
                _targets[index] = cleaned;
            }
            else
            {
                _targets.Add(cleaned);
            }

            error = Write();
        }

        Changed?.Invoke();
        return error;
    }

    public string? Update(string connectorId, string target, Func<ConnectedTarget, ConnectedTarget> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        string? error;
        lock (_gate)
        {
            var index = _targets.FindIndex(candidate => candidate.Is(connectorId, target));
            if (index < 0) return null;

            // The pair names the target, so the change may not rename it into
            // another one: its identity is kept whatever the function returned.
            var current = _targets[index];
            var changed = change(current) with { ConnectorId = current.ConnectorId, Target = current.Target };
            if (changed == current) return null;

            _targets[index] = changed;
            error = Write();
        }

        Changed?.Invoke();
        return error;
    }

    public string? Remove(string connectorId, string target)
    {
        string? error;
        lock (_gate)
        {
            if (_targets.RemoveAll(candidate => candidate.Is(connectorId, target)) == 0) return null;
            error = Write();
        }

        Changed?.Invoke();
        return error;
    }

    /// <summary>
    /// Writes the whole list. Answers a sentence for the person when the file could
    /// not be written; the list in memory stands either way.
    /// <para>
    /// Written to a sibling and moved over the top, the way <c>DeviceIdentityStore</c>
    /// writes, so the file is replaced whole or not at all. Writing in place truncates
    /// first, and a write cut short there leaves a file that no longer parses — which
    /// <see cref="Read"/> opens as nothing connected, so the next save would write
    /// every other target away. One fixed sibling name is enough: every write holds
    /// <see cref="_gate"/>, and each host keeps its own file.
    /// </para>
    /// </summary>
    private string? Write()
    {
        var temp = _path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(
                new ConnectedTargetsDto { Targets = [.. _targets.Select(ConnectedTargetDto.From)] },
                JsonOptions));
            File.Move(temp, _path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Discard(temp);
            return "Changed, but the connected repositories couldn't be saved for next time.";
        }
    }

    private static void Discard(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Litter beside a correct file; not worth failing a save over.
        }
    }

    private List<ConnectedTarget> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];

            var dto = JsonSerializer.Deserialize<ConnectedTargetsDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return [];

            // A line naming no connector or no target is a half-finished hand edit
            // and syncs nothing, so it is dropped rather than guessed at; a repeat
            // keeps the first.
            var targets = new List<ConnectedTarget>();
            foreach (var target in dto.Targets.Select(line => line.ToTarget()).OfType<ConnectedTarget>())
            {
                if (!targets.Any(existing => existing.Is(target.ConnectorId, target.Target))) targets.Add(target);
            }

            return targets;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable file must never stop the app from opening:
            // nothing is connected until somebody connects it again.
            return [];
        }
    }

    private sealed class ConnectedTargetsDto
    {
        public List<ConnectedTargetDto> Targets { get; init; } = [];
    }

    /// <summary>The file's shape: every setting spelled out, so the file is the
    /// documentation of what can be set. Intervals are written as
    /// <see cref="TimeSpan"/> strings (<c>00:15:00</c>).</summary>
    private sealed class ConnectedTargetDto
    {
        public string ConnectorId { get; init; } = string.Empty;

        public string Target { get; init; } = string.Empty;

        public bool Enabled { get; init; } = true;

        public TimeSpan? SkipUntouchedOlderThan { get; init; }

        public bool TitleFollowsSource { get; init; } = true;

        public TimeSpan? SyncInterval { get; init; }

        public bool PromoteArchivesOriginal { get; init; } = true;

        /// <summary>False when the key is missing, so a file written before the
        /// setting existed completes nothing at the source.</summary>
        public bool CompleteAtSource { get; init; }

        public DateTimeOffset? LastSyncedAt { get; init; }

        public DateTimeOffset? IgnoreUntouchedBefore { get; init; }

        public static ConnectedTargetDto From(ConnectedTarget target) => new()
        {
            ConnectorId = target.ConnectorId,
            Target = target.Target,
            Enabled = target.Enabled,
            SkipUntouchedOlderThan = target.SkipUntouchedOlderThan,
            TitleFollowsSource = target.TitleFollowsSource,
            SyncInterval = target.SyncInterval,
            PromoteArchivesOriginal = target.PromoteArchivesOriginal,
            CompleteAtSource = target.CompleteAtSource,
            LastSyncedAt = target.LastSyncedAt,
            IgnoreUntouchedBefore = target.IgnoreUntouchedBefore,
        };

        /// <summary>The target a line describes, or null when it names none. An
        /// interval that is missing or not positive reads as the default: a zero
        /// interval would sync on every tick.</summary>
        public ConnectedTarget? ToTarget() =>
            string.IsNullOrWhiteSpace(ConnectorId) || string.IsNullOrWhiteSpace(Target)
                ? null
                : new ConnectedTarget(ConnectorId.Trim(), Target.Trim(), Enabled)
                {
                    SkipUntouchedOlderThan = SkipUntouchedOlderThan is { Ticks: > 0 } age ? age : null,
                    TitleFollowsSource = TitleFollowsSource,
                    SyncInterval = SyncInterval is { Ticks: > 0 } interval ? interval : ConnectedTarget.DefaultSyncInterval,
                    PromoteArchivesOriginal = PromoteArchivesOriginal,
                    CompleteAtSource = CompleteAtSource,
                    LastSyncedAt = LastSyncedAt,
                    IgnoreUntouchedBefore = IgnoreUntouchedBefore,
                };
    }
}
