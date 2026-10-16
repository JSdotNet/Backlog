using System.Text.Json;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// When each usage source was last read, as one small JSON file in the per-user
/// application data folder (<c>usage-reads.json</c>).
/// <para>
/// Runtime state of this device, not a setting: it sits beside the per-machine
/// caches, never under the backlog root that ADR 0005 syncs and never in
/// <c>github.json</c>, whose registry and identities ride the task feed. A second
/// device reads its own providers and keeps its own times.
/// </para>
/// <para>
/// Read once, on first use, and rewritten on every recorded read. Non-throwing on
/// every path: an unreadable file starts empty, and a status that could not be
/// written is still held for this run.
/// </para>
/// </summary>
public sealed class UsageReadLogStore : IUsageReadStatus, IUsageReadRecorder
{
    /// <summary>The file's name in the per-user folder.</summary>
    public const string FileName = "usage-reads.json";

    /// <summary>A reason is one line on a settings card; anything past this is cut.</summary>
    internal const int MaxReasonLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Func<string> _path;

    private readonly TimeProvider _time;

    private readonly Lock _gate = new();

    private Dictionary<UsageSource, UsageReadStatus>? _statuses;

    public UsageReadLogStore(Func<string> path, TimeProvider? time = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _time = time ?? TimeProvider.System;
    }

    public UsageReadLogStore(string path, TimeProvider? time = null)
        : this(() => path, time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
    }

    public event Action? Changed;

    /// <summary>Where the statuses are kept.</summary>
    public string SettingsPath => _path();

    public IReadOnlyDictionary<UsageSource, UsageReadStatus> All
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<UsageSource, UsageReadStatus>(Statuses());
            }
        }
    }

    public UsageReadStatus? For(UsageSource source)
    {
        lock (_gate)
        {
            return Statuses().GetValueOrDefault(source);
        }
    }

    public void Succeeded(UsageSource source) =>
        Record(source, _ => new UsageReadStatus(_time.GetUtcNow(), null, null));

    public void Failed(UsageSource source, string reason) =>
        Record(source, previous => new UsageReadStatus(previous?.SucceededAt, _time.GetUtcNow(), OneLine(reason)));

    private void Record(UsageSource source, Func<UsageReadStatus?, UsageReadStatus> next)
    {
        if (string.IsNullOrWhiteSpace(source.Account)) return;

        lock (_gate)
        {
            var statuses = Statuses();
            statuses[source] = next(statuses.GetValueOrDefault(source));
            Write(statuses);
        }

        Changed?.Invoke();
    }

    /// <summary>The first non-blank line, trimmed and cut to one card line.</summary>
    internal static string OneLine(string? reason)
    {
        var line = (reason ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(line)) return "The read failed without saying why.";

        return line.Length <= MaxReasonLength ? line : line[..(MaxReasonLength - 1)] + "…";
    }

    private Dictionary<UsageSource, UsageReadStatus> Statuses() => _statuses ??= Read();

    private Dictionary<UsageSource, UsageReadStatus> Read()
    {
        var statuses = new Dictionary<UsageSource, UsageReadStatus>();

        try
        {
            var path = _path();
            if (!File.Exists(path)) return statuses;

            var stored = JsonSerializer.Deserialize<StoredRead[]>(File.ReadAllText(path), JsonOptions);

            foreach (var read in stored ?? [])
            {
                // A provider this version no longer knows is dropped rather than
                // failing the whole file.
                if (read is null
                    || string.IsNullOrWhiteSpace(read.Account)
                    || !Enum.TryParse<SpendProvider>(read.Provider, ignoreCase: true, out var provider)
                    || !Enum.IsDefined(provider))
                {
                    continue;
                }

                statuses[new UsageSource(provider, read.Account)] = new UsageReadStatus(
                    read.SucceededAt,
                    read.FailedAt,
                    read.FailedAt is null ? null : OneLine(read.FailureReason));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            statuses.Clear();
        }

        return statuses;
    }

    private void Write(Dictionary<UsageSource, UsageReadStatus> statuses)
    {
        try
        {
            var path = _path();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(
                statuses
                    .OrderBy(pair => pair.Key.Provider)
                    .ThenBy(pair => pair.Key.Account, StringComparer.Ordinal)
                    .Select(pair => new StoredRead
                    {
                        Provider = pair.Key.Provider.ToString(),
                        Account = pair.Key.Account,
                        SucceededAt = pair.Value.SucceededAt,
                        FailedAt = pair.Value.FailedAt,
                        FailureReason = pair.Value.FailureReason
                    })
                    .ToArray(),
                JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Held for this run; the next recorded read tries again.
        }
    }

    private sealed record StoredRead
    {
        public string? Provider { get; init; }

        public string? Account { get; init; }

        public DateTimeOffset? SucceededAt { get; init; }

        public DateTimeOffset? FailedAt { get; init; }

        public string? FailureReason { get; init; }
    }
}
