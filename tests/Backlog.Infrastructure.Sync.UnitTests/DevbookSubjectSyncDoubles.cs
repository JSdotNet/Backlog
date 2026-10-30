using Backlog.Infrastructure.Sync.DevbookSubjects;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>The subject-sync state port with no file behind it, recording every
/// state it was handed.</summary>
internal sealed class InMemoryDevbookSubjectSyncStateStore : IDevbookSubjectSyncStateStore
{
    public InMemoryDevbookSubjectSyncStateStore(DevbookSubjectSyncState? initial = null) =>
        Current = initial ?? new DevbookSubjectSyncState(DateTimeOffset.MinValue, null);

    public event Action? Changed;

    public DevbookSubjectSyncState Current { get; private set; }

    public List<DevbookSubjectSyncState> Saved { get; } = [];

    public string StorePath => "in memory";

    public void Save(DevbookSubjectSyncState state)
    {
        Current = state;
        Saved.Add(state);
        Changed?.Invoke();
    }
}

/// <summary>
/// The subject state store with nothing on disk, as the exchange and the merge
/// see it: what this machine holds, seeded as if a panel had set it, with
/// <see cref="Applied"/> recording every document replication wrote. The three
/// state operations are the panels' and the MCP tools', and nothing here calls
/// them.
/// </summary>
internal sealed class InMemoryDevbookSubjectStateStore : IDevbookSubjectStateStore
{
    private readonly Dictionary<Guid, DevbookSubjectState> _states = [];

    public event Action? Changed;

    public List<DevbookSubjectState> Applied { get; } = [];

    /// <summary>Runs once, at the start of the next <see cref="Apply"/> — a
    /// write made on this device while replication was deciding.</summary>
    public Action? BeforeNextApply { get; set; }

    public DevbookSubjectState? Get(string? repositoryAlias, DevbookSubject subject) =>
        _states.GetValueOrDefault(DevbookSubject.IdFor(repositoryAlias, subject));

    public IReadOnlyList<DevbookSubjectState> List(string? repositoryAlias) =>
        [.. _states.Values.Where(state => state.RepositoryAlias == (repositoryAlias ?? string.Empty))];

    public DevbookSubjectState? Find(Guid id) => _states.GetValueOrDefault(id);

    public DevbookSubjectOutcome SetState(string? repositoryAlias, DevbookSubject subject, string state, string by) =>
        throw new NotSupportedException("Seed the store instead.");

    public DevbookSubjectOutcome Approve(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by) =>
        throw new NotSupportedException("Seed the store instead.");

    public DevbookSubjectOutcome Accept(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by) =>
        throw new NotSupportedException("Seed the store instead.");

    /// <summary>The real stores' rule, applied the way they apply it: the check
    /// and the write together.</summary>
    public bool Apply(DevbookSubjectState replicated)
    {
        var interleaved = BeforeNextApply;
        BeforeNextApply = null;
        interleaved?.Invoke();

        if (!DevbookSubjectRules.Supersedes(replicated, _states.GetValueOrDefault(replicated.Id))) return false;

        _states[replicated.Id] = replicated;
        Applied.Add(replicated);
        Changed?.Invoke();

        return true;
    }

    public IReadOnlyList<DevbookSubjectState> ListChangedSince(DateTimeOffset watermark) =>
    [
        .. _states.Values
            .Where(state => state.UpdatedAt > watermark)
            .OrderBy(state => state.UpdatedAt)
            .ThenBy(state => state.Id),
    ];

    /// <summary>What this machine holds, as if a panel had set it.</summary>
    public DevbookSubjectState Seed(DevbookSubjectState state)
    {
        _states[state.Id] = state;
        return state;
    }
}

/// <summary>Builders for the subject shapes the tests exchange: one debt record
/// in one repository, as this machine holds it and as the wire carries it.</summary>
internal static class Subjects
{
    public const string Repository = "JSdotNet/Backlog";
    public const string RecordPath = ".devbook/arc42/tdr/0003-paging.md";

    public static readonly DevbookSubject Debt = DevbookSubject.Debt(RecordPath);

    public static Guid DebtId => DevbookSubject.IdFor(Repository, Debt);

    public static DevbookSubjectState Local(string state, DateTimeOffset updatedAt, DevbookSubject? subject = null, string repository = Repository)
    {
        var named = subject ?? Debt;

        return new(DevbookSubject.IdFor(repository, named), repository, named, state, "DEV-TOWER", updatedAt, null, null, updatedAt);
    }

    public static DevbookSubjectChange Change(string state, DateTimeOffset updatedAt, string by = "DEV-LAPTOP") =>
        new(DebtId, updatedAt, new DevbookSubjectPayload(Repository, "debt", RecordPath, null, state, by, updatedAt));

    public static DevbookSubjectChangeRecord Record(DevbookSubjectChange change, Guid deviceId, long serverTimestamp) =>
        new(change, deviceId, serverTimestamp);
}
