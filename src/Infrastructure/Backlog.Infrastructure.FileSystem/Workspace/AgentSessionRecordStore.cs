using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="IAgentSessionRecordStore"/>: one small JSON file per
/// session, under one flat folder.
/// <para>
/// A file per session for <see cref="AgentActivityCache"/>'s reason — writing one record
/// can never lose another — and the root arrives as a delegate for the same reason too.
/// Unlike that cache, nothing here is a miss to be re-read: a record is often the only
/// thing left of a session, so an entry this build cannot read is left on disk
/// untouched rather than overwritten, and nothing here ever deletes one.
/// </para>
/// </summary>
public sealed class AgentSessionRecordStore(Func<string> root) : IAgentSessionRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The shape on disk. A later version reads an earlier one by treating the
    /// fields it lacks as unknown — the amend rule already reads an absent value as
    /// "keep what the record held" — so this is bumped only for a change of meaning.</summary>
    private const int CurrentVersion = 1;

    private readonly Func<string> _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly Lock _gate = new();

    public IReadOnlyList<AgentSessionRecord> All()
    {
        var folder = _root();

        if (!Directory.Exists(folder)) return [];

        var records = new List<AgentSessionRecord>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (Read(file) is { } record) records.Add(record);
        }

        return records;
    }

    public SessionRecordUpdate Save(IReadOnlyList<AgentSessionRecord> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var started = 0;
        var amended = 0;

        lock (_gate)
        {
            Directory.CreateDirectory(_root());

            foreach (var reading in readings)
            {
                var path = PathFor(reading.Session);
                var exists = File.Exists(path);
                var stored = exists ? Read(path) : null;

                // A file this build cannot read is somebody's only record of a session.
                // Leaving it is the whole of the remedy; writing over it would be the
                // "remove and replace" this store exists not to do.
                if (exists && stored is null) continue;

                Write(path, AgentSessionRecords.Amend(stored, reading));

                if (stored is null) started++;
                else amended++;
            }
        }

        return new SessionRecordUpdate(started, amended);
    }

    /// <summary>The session id, so a person can recognise the folder, plus a digest of
    /// the agent and the id, because two agents may issue the same string.</summary>
    private string PathFor(AgentSession session) =>
        Path.Combine(_root(), CachePaths.Safe(session.Id, $"{session.Kind}:{session.Id}") + ".json");

    private static AgentSessionRecord? Read(string path)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredRecord>(File.ReadAllText(path), JsonOptions);

            return stored is null || stored.Version > CurrentVersion ? null : stored.ToRecord();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Written beside and moved over, so a record is never half-written: the
    /// only copy of a session is exactly the file a torn write would destroy.</summary>
    private static void Write(string path, AgentSessionRecord record)
    {
        var temporary = path + ".tmp";

        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(StoredRecord.Of(record), JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not written this time; the next reading amends it again.
        }
    }

    /// <summary>The stored shape — separate from the model for the reason
    /// <see cref="AgentActivityCache"/> gives: the file format is a decision this class
    /// makes, not a consequence of a record somebody refactors.</summary>
    private sealed record StoredRecord
    {
        public int Version { get; init; }

        public DateTimeOffset RecordedAt { get; init; }

        public string Id { get; init; } = "";

        public string Kind { get; init; } = "";

        public string EnvironmentId { get; init; } = "";

        public string Environment { get; init; } = "";

        public string Title { get; init; } = "";

        public string WorkingFolder { get; init; } = "";

        public string? Repository { get; init; }

        public string? ResolvedRepository { get; init; }

        public string? Branch { get; init; }

        public DateTimeOffset? StartedAt { get; init; }

        public DateTimeOffset LastActivityAt { get; init; }

        public int? TurnCount { get; init; }

        public string? Entrypoint { get; init; }

        /// <summary>Null where the reading could not say; absent from a file written
        /// before the field existed, which reads the same way.</summary>
        public StoredPullRequest[]? PullRequests { get; init; }

        /// <inheritdoc cref="PullRequests"/>
        public StoredModelUsage[]? ModelUsage { get; init; }

        /// <summary>Null where no reading ever folded the transcript; the three lists
        /// below are then absent rather than empty.</summary>
        public bool HasActivity { get; init; }

        public StoredInterval[] Runs { get; init; } = [];

        public StoredInterval[] Waits { get; init; } = [];

        public StoredLimitHit[] LimitHits { get; init; } = [];

        public static StoredRecord Of(AgentSessionRecord record)
        {
            var session = record.Session;
            var activity = record.Activity;

            return new StoredRecord
            {
                Version = CurrentVersion,
                RecordedAt = record.RecordedAt,
                Id = session.Id,
                Kind = session.Kind.ToString(),
                EnvironmentId = session.EnvironmentId,
                Environment = session.Environment,
                Title = session.Title,
                WorkingFolder = session.WorkingFolder,
                Repository = session.Repository,
                ResolvedRepository = session.ResolvedRepository,
                Branch = session.Branch,
                StartedAt = session.StartedAt,
                LastActivityAt = session.LastActivityAt,
                TurnCount = session.TurnCount,
                Entrypoint = session.Entrypoint,
                PullRequests = session.PullRequests is null ? null
                    : [.. session.PullRequests.Select(pr => new StoredPullRequest { Repository = pr.Repository, Number = pr.Number, Url = pr.Url, LinkedAt = pr.LinkedAt })],
                ModelUsage = session.ModelUsage is null ? null
                    : [.. session.ModelUsage.Select(usage => new StoredModelUsage
                    {
                        Model = usage.Model,
                        InputTokens = usage.InputTokens,
                        OutputTokens = usage.OutputTokens,
                        CacheCreationInputTokens = usage.CacheCreationInputTokens,
                        CacheReadInputTokens = usage.CacheReadInputTokens
                    })],
                HasActivity = activity is not null,
                Runs = [.. (activity?.Runs ?? []).Select(run => new StoredInterval { From = run.StartedAt, To = run.EndedAt })],
                Waits = [.. (activity?.Waits ?? []).Select(wait => new StoredInterval { From = wait.StartedAt, To = wait.EndedAt })],
                LimitHits = [.. (activity?.LimitHits ?? []).Select(hit => new StoredLimitHit
                {
                    At = hit.At,
                    Kind = hit.Kind.ToString(),
                    RateLimitType = hit.RateLimitType,
                    ResetsAt = hit.ResetsAt,
                    OverageStatus = hit.OverageStatus,
                    OverageResetsAt = hit.OverageResetsAt,
                    OverageDisabledReason = hit.OverageDisabledReason,
                    IsUsingOverage = hit.IsUsingOverage
                })]
            };
        }

        public AgentSessionRecord? ToRecord()
        {
            if (string.IsNullOrWhiteSpace(Id)
                || !Enum.TryParse<AgentSessionKind>(Kind, ignoreCase: false, out var kind)
                || !Enum.IsDefined(kind))
            {
                return null;
            }

            var session = new AgentSession(
                Id,
                kind,
                EnvironmentId,
                Environment,
                Title,
                WorkingFolder,
                Repository,
                Branch,
                StartedAt,
                LastActivityAt,
                AgentSessionState.Finished,
                TurnCount,
                AgentSessionOrigin.Recorded)
            {
                ResolvedRepository = ResolvedRepository,
                Entrypoint = Entrypoint,
                PullRequests = PullRequests is null ? null : [.. PullRequests.Select(pr => new AgentPullRequest(pr.Repository, pr.Number, pr.Url, pr.LinkedAt))],
                ModelUsage = ModelUsage is null ? null
                    : [.. ModelUsage.Select(usage => new AgentModelUsage(usage.Model, usage.InputTokens, usage.OutputTokens, usage.CacheCreationInputTokens, usage.CacheReadInputTokens))]
            };

            var activity = HasActivity
                ? new AgentSessionActivity(
                    Id,
                    kind,
                    EnvironmentId,
                    Environment,
                    [.. Runs.Select(run => new AgentActivityRun(run.From, run.To))],
                    [.. Waits.Select(wait => new AgentActivityWait(wait.From, wait.To))])
                {
                    Origin = AgentSessionOrigin.Recorded,
                    LimitHits = [.. LimitHits.Select(hit => new AgentLimitHit(hit.At, KindOf(hit.Kind), hit.RateLimitType)
                    {
                        ResetsAt = hit.ResetsAt,
                        OverageStatus = hit.OverageStatus,
                        OverageResetsAt = hit.OverageResetsAt,
                        OverageDisabledReason = hit.OverageDisabledReason,
                        IsUsingOverage = hit.IsUsingOverage
                    })]
                }
                : null;

            return new AgentSessionRecord(session, activity, RecordedAt);
        }
    }

    private sealed record StoredPullRequest
    {
        public string Repository { get; init; } = "";

        public int Number { get; init; }

        public string Url { get; init; } = "";

        public DateTimeOffset? LinkedAt { get; init; }
    }

    private sealed record StoredModelUsage
    {
        public string Model { get; init; } = "";

        public long InputTokens { get; init; }

        public long OutputTokens { get; init; }

        public long CacheCreationInputTokens { get; init; }

        public long CacheReadInputTokens { get; init; }
    }

    private sealed record StoredInterval
    {
        public DateTimeOffset From { get; init; }

        public DateTimeOffset To { get; init; }
    }

    private sealed record StoredLimitHit
    {
        public DateTimeOffset At { get; init; }

        public string Kind { get; init; } = "";

        public string? RateLimitType { get; init; }

        public DateTimeOffset? ResetsAt { get; init; }

        public string? OverageStatus { get; init; }

        public DateTimeOffset? OverageResetsAt { get; init; }

        public string? OverageDisabledReason { get; init; }

        public bool? IsUsingOverage { get; init; }
    }

    private static AgentLimitKind KindOf(string kind) =>
        Enum.TryParse<AgentLimitKind>(kind, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : AgentLimitKind.Other;
}
