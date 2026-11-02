using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Devbook;

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
/// <para>
/// The direction map too (<see cref="IDevbookSyncDirectionStore"/>), as the file
/// store keeps both in one file: a panel with no host store sets a direction here
/// and reads it back. No folder is scanned — the one-time import is the file
/// store's — so <see cref="Import"/> takes only what a caller hands it. The map,
/// unlike the subjects, is locked: a pane's sync reading asks it off the
/// dispatcher. Its event is its own, as the file store's is.
/// </para>
/// </summary>
public sealed class SessionDevbookSubjectStateStore : IDevbookSubjectStateStore, IDevbookSyncDirectionStore
{
    private readonly Dictionary<Guid, DevbookSubjectState> _states = [];

    /// <summary>Keyed by <see cref="DevbookSubject.AliasKey"/> and the normalised
    /// address, both compared ordinally. Under <see cref="_directionsGate"/>, unlike
    /// the subjects: a pane's sync reading asks it from <c>Task.Run</c> while the
    /// dispatcher may be writing it.</summary>
    private readonly Dictionary<(string Alias, string Address), DevbookSyncStatement> _directions = [];
    private readonly HashSet<string> _imported = new(StringComparer.Ordinal);
    private readonly Lock _directionsGate = new();
    private readonly TimeProvider _time;

    /// <param name="time">Stamps the operations, so a test can fix them.</param>
    public SessionDevbookSubjectStateStore(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Raised after a subject write, never for a direction.</summary>
    public event Action? Changed;

    /// <summary>The direction port's own event, raised by a direction write and
    /// by <see cref="Import"/> only.</summary>
    event Action? IDevbookSyncDirectionStore.Changed
    {
        add => DirectionsChanged += value;
        remove => DirectionsChanged -= value;
    }

    private event Action? DirectionsChanged;

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

    /// <summary>Every direction stated for the repository, by address. Explicit
    /// because <see cref="IDevbookSubjectStateStore.List"/> takes the same
    /// argument.</summary>
    IReadOnlyList<DevbookSyncStatement> IDevbookSyncDirectionStore.List(string? repositoryAlias)
    {
        var alias = DevbookSubject.AliasKey(repositoryAlias);

        lock (_directionsGate)
        {
            return
            [
                .. _directions
                    .Where(entry => string.Equals(entry.Key.Alias, alias, StringComparison.Ordinal))
                    .Select(entry => entry.Value)
                    .OrderBy(statement => statement.Address, StringComparer.Ordinal),
            ];
        }
    }

    public string? Get(string? repositoryAlias, string address)
    {
        if (!DevbookSync.TryAddress(address, out _, out var normalised)) return null;

        lock (_directionsGate)
        {
            return _directions.GetValueOrDefault((DevbookSubject.AliasKey(repositoryAlias), normalised))?.Direction;
        }
    }

    public DevbookSyncStatement? Set(string? repositoryAlias, string address, string? direction, string by)
    {
        if (!DevbookSync.TryAddress(address, out _, out var normalised))
        {
            throw new ArgumentException($"'{address}' names no devbook folder.", nameof(address));
        }

        if (!string.IsNullOrWhiteSpace(direction) && !DevbookSync.IsDirection(direction))
        {
            throw new ArgumentException(
                $"'{direction}' is not a sync direction: {string.Join(", ", DevbookSync.Directions)}.", nameof(direction));
        }

        var key = (DevbookSubject.AliasKey(repositoryAlias), normalised);
        DevbookSyncStatement? statement = null;

        lock (_directionsGate)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                if (!_directions.Remove(key)) return null;
            }
            else
            {
                statement = new DevbookSyncStatement(normalised, direction.Trim().ToLowerInvariant(), by, _time.GetUtcNow());
                _directions[key] = statement;
            }
        }

        // Raised outside the lock: a handler may read the map back.
        DirectionsChanged?.Invoke();

        return statement;
    }

    public bool IsImported(string? repositoryAlias)
    {
        lock (_directionsGate)
        {
            return _imported.Contains(DevbookSubject.AliasKey(repositoryAlias));
        }
    }

    public void Import(string? repositoryAlias, IEnumerable<(string Address, string Direction)> stated)
    {
        ArgumentNullException.ThrowIfNull(stated);

        var alias = DevbookSubject.AliasKey(repositoryAlias);
        var at = _time.GetUtcNow();

        lock (_directionsGate)
        {
            if (!_imported.Add(alias)) return;

            foreach (var (address, direction) in stated)
            {
                if (!DevbookSync.IsDirection(direction) || !DevbookSync.TryAddress(address, out _, out var normalised)) continue;

                _directions.TryAdd((alias, normalised), new DevbookSyncStatement(normalised, direction.Trim().ToLowerInvariant(), DevbookSyncStatement.ImportedBy, at));
            }
        }

        DirectionsChanged?.Invoke();
    }

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
