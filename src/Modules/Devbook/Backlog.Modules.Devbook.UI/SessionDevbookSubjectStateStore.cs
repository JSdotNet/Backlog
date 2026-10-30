using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The subject store a panel falls back on when its host composed none — the
/// storybook, and the unit tests that render a panel with the handful of services
/// it asks for by name.
/// <para>
/// In memory for the life of the object, as <see cref="SessionDevbookAnnotationStore"/>
/// is for remarks, and unlocked like it: no replication runs against a store no
/// host composed, so its newer-than check and its write are one step without one.
/// It keeps the panels on one path through
/// <see cref="IDevbookSubjectStateStore"/> whether or not a host chose a real
/// store. The rules are <see cref="DevbookSubjectRules"/>', the same copy the file
/// store applies, and stamps come from the clock it is given so a test can fix
/// them.
/// </para>
/// </summary>
public sealed class SessionDevbookSubjectStateStore : IDevbookSubjectStateStore
{
    private readonly Dictionary<Guid, DevbookSubjectState> _states = [];
    private readonly TimeProvider _time;

    /// <param name="time">Stamps the operations, so a test can fix them.</param>
    public SessionDevbookSubjectStateStore(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public event Action? Changed;

    public DevbookSubjectState? Get(string? repositoryAlias, DevbookSubject subject) =>
        _states.GetValueOrDefault(DevbookSubject.IdFor(repositoryAlias, subject));

    public IReadOnlyList<DevbookSubjectState> List(string? repositoryAlias)
    {
        var alias = DevbookSubject.AliasKey(repositoryAlias);

        return
        [
            .. _states.Values
                .Where(state => string.Equals(state.RepositoryAlias, alias, StringComparison.Ordinal))
                .OrderBy(state => state.Subject.CanonicalKey, StringComparer.Ordinal),
        ];
    }

    public DevbookSubjectState? Find(Guid id) => _states.GetValueOrDefault(id);

    public DevbookSubjectOutcome SetState(string? repositoryAlias, DevbookSubject subject, string state, string by) =>
        Operate(repositoryAlias, subject, (current, at) => DevbookSubjectRules.SetState(current, repositoryAlias, subject, state, by, at));

    public DevbookSubjectOutcome Approve(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by) =>
        Operate(repositoryAlias, subject, (current, at) => DevbookSubjectRules.Approve(current, repositoryAlias, subject, fingerprint, by, at));

    public DevbookSubjectOutcome Accept(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by) =>
        Operate(repositoryAlias, subject, (current, at) => DevbookSubjectRules.Accept(current, repositoryAlias, subject, fingerprint, by, at));

    /// <summary>Writes a document as replication hands it over, stamps
    /// untouched, when it supersedes the one held.</summary>
    public bool Apply(DevbookSubjectState replicated)
    {
        ArgumentNullException.ThrowIfNull(replicated);

        if (!DevbookSubjectRules.Supersedes(replicated, _states.GetValueOrDefault(replicated.Id))) return false;

        Put(replicated);

        return true;
    }

    public IReadOnlyList<DevbookSubjectState> ListChangedSince(DateTimeOffset watermark) =>
    [
        .. _states.Values
            .Where(state => state.UpdatedAt > watermark)
            .OrderBy(state => state.UpdatedAt)
            .ThenBy(state => state.Id),
    ];

    private DevbookSubjectOutcome Operate(
        string? repositoryAlias,
        DevbookSubject subject,
        Func<DevbookSubjectState?, DateTimeOffset, DevbookSubjectOutcome> operation)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var current = Get(repositoryAlias, subject);
        var outcome = operation(current, Stamp(current));

        if (outcome.State is { } state) Put(state);

        return outcome;
    }

    /// <summary>One document in, under the alias as every lookup folds it, and
    /// announced.</summary>
    private void Put(DevbookSubjectState state)
    {
        _states[state.Id] = state with { RepositoryAlias = DevbookSubject.AliasKey(state.RepositoryAlias) };
        Changed?.Invoke();
    }

    /// <summary>Now, or one tick past the stamp being changed when that is later
    /// — the rule the file store keeps, for the same reason: a local operation is
    /// later than the state it was made to, whatever the clock that stamped that
    /// state thought.</summary>
    private DateTimeOffset Stamp(DevbookSubjectState? current)
    {
        var now = _time.GetUtcNow();
        return current is null || now > current.UpdatedAt ? now : current.UpdatedAt.AddTicks(1);
    }
}
