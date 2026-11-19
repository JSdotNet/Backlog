using System.Text.Json;

using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.SharedKernel.Storage;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps, per monitored target, the entries a capture run passed over, in a
/// JSON file next to the capture sources it ran over.
/// <para>
/// Its own file rather than a section of <c>capture-runs.json</c>: the run log
/// is for the reader and nothing the run does depends on it, while this is the
/// one thing the run reads back. It grows with what the watched feeds have
/// ever listed and was not delivered — a few hundred ids for a whole-site
/// feed — and is never trimmed, for the reason the run gives.
/// </para>
/// <para>
/// Recording never throws, for the reason <see cref="CaptureRunLogStore"/>
/// gives: the in-memory ledger is updated first, so the session keeps it, and
/// what a refused write loses is a second first look after a restart.
/// </para>
/// </summary>
public sealed class CaptureTargetLedgerStore : ICaptureTargetLedger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly JsonFileStore<LedgerDto> _file;
    private readonly object _gate = new();
    private readonly Dictionary<(CaptureSourceKind Kind, string Target), HashSet<Guid>> _targets;

    public CaptureTargetLedgerStore()
        : this(AppDataPaths.Shared("capture-targets.json"))
    {
    }

    /// <summary>Names the ledger file separately from the per-user location,
    /// for the reason <see cref="CaptureSourcesSettingsStore(string)"/> does.</summary>
    public CaptureTargetLedgerStore(string path)
    {
        _file = new JsonFileStore<LedgerDto>(path, JsonOptions);

        _targets = Read();
    }

    public IReadOnlySet<Guid>? PassedOverAt(CaptureSourceKind kind, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        lock (_gate)
        {
            return _targets.TryGetValue(Key(kind, target), out var ids) ? new HashSet<Guid>(ids) : null;
        }
    }

    public void Record(CaptureSourceKind kind, string target, IReadOnlyCollection<Guid> passedOver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(passedOver);

        lock (_gate)
        {
            _targets[Key(kind, target)] = [.. passedOver];
            Save();
        }
    }

    /// <summary>A target as the reader typed it, trimmed. Case is kept: a URL
    /// path is case-sensitive, so <c>/Blog</c> and <c>/blog</c> are two targets.</summary>
    private static (CaptureSourceKind, string) Key(CaptureSourceKind kind, string target) =>
        (kind, target.Trim());

    // A refused write is kept for this session; only its surviving a restart is lost.
    private void Save() =>
        _file.TrySave(new LedgerDto
        {
            Targets = [.. _targets.Select(pair => new TargetDto
            {
                Kind = CaptureSourceKinds.Slug(pair.Key.Kind),
                Target = pair.Key.Target,
                PassedOver = [.. pair.Value]
            })]
        });

    private Dictionary<(CaptureSourceKind Kind, string Target), HashSet<Guid>> Read()
    {
        var targets = new Dictionary<(CaptureSourceKind Kind, string Target), HashSet<Guid>>();

        // A corrupt or unreachable ledger must never stop the app from opening.
        // What it costs is a first look at every target again.
        if (_file.Load() is not { } dto) return targets;

        foreach (var target in dto.Targets)
        {
            // A kind nobody can read is dropped rather than guessed at.
            if (!CaptureSourceKinds.TryParse(target.Kind, out var kind)) continue;
            if (string.IsNullOrWhiteSpace(target.Target)) continue;

            targets[Key(kind, target.Target)] = [.. target.PassedOver];
        }

        return targets;
    }

    private sealed class LedgerDto
    {
        public List<TargetDto> Targets { get; init; } = [];
    }

    private sealed class TargetDto
    {
        public string Kind { get; init; } = string.Empty;

        public string Target { get; init; } = string.Empty;

        public List<Guid> PassedOver { get; init; } = [];
    }
}
