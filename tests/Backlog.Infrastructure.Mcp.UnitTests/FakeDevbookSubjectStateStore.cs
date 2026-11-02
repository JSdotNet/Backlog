using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The subject store, in memory, standing in for the file store the desktop
/// composes.
/// <para>
/// It applies <see cref="DevbookSubjectRules"/> — the one copy of the contract's
/// rules every real store applies — rather than answering canned outcomes. A
/// double that decided refusals itself would make "the tool answers
/// <c>not-approved</c>" true of the double; this way the tool is tested against
/// the rules it will meet in the app, and the double only keeps what comes back.
/// </para>
/// <para>
/// It records as well as keeps. <see cref="Calls"/> is every operation the tool
/// asked for, so a test can show an input the tool refused never reached the
/// store; <see cref="Written"/> is every state actually kept, so a test can show
/// a refusal the rules gave wrote nothing. <c>Apply</c> and
/// <c>ListChangedSince</c> are replication's and refuse: no MCP tool replicates.
/// </para>
/// </summary>
internal sealed class FakeDevbookSubjectStateStore(TimeProvider time) : IDevbookSubjectStateStore
{
    private readonly Dictionary<Guid, DevbookSubjectState> _states = [];

    /// <summary>Every operation asked for, by name, with the alias and subject it
    /// was asked about and the author it was told.</summary>
    public List<(string Operation, string? Alias, DevbookSubject Subject, string? By)> Calls { get; } = [];

    /// <summary>Every state kept, in order.</summary>
    public List<DevbookSubjectState> Written { get; } = [];

    public event Action? Changed
    {
        add { }
        remove { }
    }

    /// <summary>A state put in place before the test, as if another session had
    /// set it. Not recorded as a write.</summary>
    public void Seed(DevbookSubjectState state) => _states[state.Id] = state;

    public DevbookSubjectState? Get(string? repositoryAlias, DevbookSubject subject)
    {
        Calls.Add((nameof(Get), repositoryAlias, subject, null));
        return _states.GetValueOrDefault(DevbookSubject.IdFor(repositoryAlias, subject));
    }

    public IReadOnlyList<DevbookSubjectState> List(string? repositoryAlias) =>
        [.. _states.Values.Where(state => string.Equals(state.RepositoryAlias, repositoryAlias ?? string.Empty, StringComparison.OrdinalIgnoreCase))];

    public DevbookSubjectState? Find(Guid id) => _states.GetValueOrDefault(id);

    public DevbookSubjectOutcome SetState(string? repositoryAlias, DevbookSubject subject, string state, string by) =>
        Operate(nameof(SetState), repositoryAlias, subject, by, current =>
            DevbookSubjectRules.SetState(current, repositoryAlias, subject, state, by, time.GetUtcNow()));

    public DevbookSubjectOutcome Approve(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by) =>
        Operate(nameof(Approve), repositoryAlias, subject, by, current =>
            DevbookSubjectRules.Approve(current, repositoryAlias, subject, fingerprint, by, time.GetUtcNow()));

    public DevbookSubjectOutcome Accept(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by) =>
        Operate(nameof(Accept), repositoryAlias, subject, by, current =>
            DevbookSubjectRules.Accept(current, repositoryAlias, subject, fingerprint, by, time.GetUtcNow()));

    /// <summary>When set, every operation answers that the repository is
    /// archived — the host's own refusal, outside the contract's — and writes
    /// nothing.</summary>
    public bool AnswersArchived { get; set; }

    public bool Apply(DevbookSubjectState replicated) =>
        throw new InvalidOperationException("An MCP tool called IDevbookSubjectStateStore.Apply, which is replication's.");

    public IReadOnlyList<DevbookSubjectState> ListChangedSince(DateTimeOffset watermark) =>
        throw new InvalidOperationException("An MCP tool called IDevbookSubjectStateStore.ListChangedSince, which is replication's.");

    private DevbookSubjectOutcome Operate(
        string operation,
        string? repositoryAlias,
        DevbookSubject subject,
        string by,
        Func<DevbookSubjectState?, DevbookSubjectOutcome> apply)
    {
        Calls.Add((operation, repositoryAlias, subject, by));

        if (AnswersArchived) return DevbookSubjectOutcome.Archived("Architecture is read-only because its repository is archived.");

        var outcome = apply(_states.GetValueOrDefault(DevbookSubject.IdFor(repositoryAlias, subject)));

        if (outcome.State is { } state)
        {
            _states[state.Id] = state;
            Written.Add(state);
        }

        return outcome;
    }
}
