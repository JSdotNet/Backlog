using Backlog.SharedKernel;
using Backlog.SharedKernel.Changes;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Keeps every alias-keyed store on the aliases the registry now carries (local ADR
/// 0060 §3).
/// <list type="bullet">
/// <item>The device that relabels re-keys the replicated stores, from
/// <see cref="GitHubSettingsStore.AliasesMoved"/>. Each re-keyed document then travels
/// through its own change path.</item>
/// <item>Every device re-keys its own local stores from
/// <see cref="GitHubSettings.AliasMoves"/>. It applies one move group (one
/// <see cref="AliasMove.At"/>) at a time, oldest first, past each store's own cursor
/// (<see cref="GitHubSettingsStore.AliasMovesAppliedThrough"/>).</item>
/// <item>A backstop moves what a replicated store still holds under an alias no row
/// carries onto the row <see cref="GitHubSettings.Find"/> resolves it to. It repairs a
/// crash between the registry save and the re-key, and the registry arriving before
/// or after the re-keyed documents.</item>
/// </list>
/// <para>
/// <b>Once, whatever the order.</b> The store raises <c>Changed</c> before
/// <c>AliasesMoved</c> on a local edit.
/// <list type="bullet">
/// <item>The <c>Changed</c> of a moving edit is recognized while it is raised
/// (<see cref="GitHubSettingsStore.IsSavingAliasMovesOnThisThread"/>) and left to the
/// edit's own pass.</item>
/// <item>The local stores are re-keyed by the cursor path alone, under one gate, so a
/// group reaches a local store once, whichever pass gets there first.</item>
/// <item>The backstop waits while an edit's pass is still queued. Otherwise it could
/// resolve a chain (<c>x → y</c>, <c>y → z</c> in one edit) ahead of the edit's single
/// mapping and run <c>x</c>'s data into <c>y</c>'s.</item>
/// </list>
/// Each pass starts off the raising thread and is serialized by the gate.
/// </para>
/// <para>
/// <b>Only saved moves.</b> While the registry file does not hold a move the store
/// recorded (<see cref="GitHubSettingsStore.HasUnsavedAliasMoves"/>), the catch-up and
/// the backstop do nothing. A re-key to an alias the next start may not know would
/// orphan the data it moved. The next save that lands raises <c>Changed</c>, and the
/// pass runs then.
/// </para>
/// <para>
/// <b>Failures.</b> A store that throws is logged and passed over: start never fails
/// on one. Each local store has its own cursor, keyed by its type, so a store that
/// fails holds back only itself. It is offered the group again on the next pass.
/// </para>
/// </summary>
public sealed class RepositoryAliasFollower : IDisposable
{
    private readonly GitHubSettingsStore _settings;
    private readonly IReadOnlyList<IRepositoryAliasKeyedStore> _replicated;
    private readonly IReadOnlyList<(IRepositoryAliasKeyedStore Store, string Key)> _local;
    private readonly ILogger<RepositoryAliasFollower> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _passesLock = new();
    private readonly List<Task> _passes = [];

    /// <summary>Cancelled by <see cref="Dispose"/>, so a pass still running at shutdown
    /// stops before its next store rather than reaching one the container has disposed.
    /// Never disposed itself: a pass not yet started still reads its token.</summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>Edit passes announced and not yet past their re-key.</summary>
    private int _pendingEdits;

    private bool _subscribed;
    private bool _started;
    private volatile bool _disposed;

    public RepositoryAliasFollower(
        GitHubSettingsStore settings,
        IEnumerable<IRepositoryAliasKeyedStore> stores,
        ILogger<RepositoryAliasFollower>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(stores);

        _settings = settings;
        var all = stores.ToList();
        _replicated = [.. all.Where(s => s.Kind is AliasKeyedStoreKind.Replicated)];
        _local = KeyedByType(all.Where(s => s.Kind is AliasKeyedStoreKind.Local));
        _logger = logger ?? NullLogger<RepositoryAliasFollower>.Instance;
    }

    /// <summary>
    /// Subscribes, then runs the start pass under the gate:
    /// <list type="number">
    /// <item>the one-time relabel of a repository renamed before local ADR 0060 (§5),
    /// whose moves reach the replicated stores through its own edit pass;</item>
    /// <item>the local stores caught up past their cursors;</item>
    /// <item>the backstop.</item>
    /// </list>
    /// It completes once the passes the start caused have finished. An edit or an
    /// arrival during the start pass queues behind the gate. It never throws for a
    /// store. Cancelled, it can be called again; once it has completed, a second call
    /// does nothing.
    /// <para>
    /// The whole start runs on the thread pool. A host calls this from its UI thread,
    /// and the store loads and the §5 save would otherwise run there until their first
    /// real await. <c>Task.Yield</c> would not help: it resumes on the caller's context.
    /// §5's <c>Changed</c> is therefore raised off the UI thread, as a registry arriving
    /// from another device already is.
    /// </para>
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => StartOnPoolAsync(cancellationToken), cancellationToken);

    private async Task StartOnPoolAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var change = ChangeContext.Begin(ChangeActor.System, "following a repository rename");
        try
        {
            if (_started || _disposed) return;

            if (!_subscribed)
            {
                _settings.AliasesMoved += OnAliasesMoved;
                _settings.Changed += OnChanged;
                _subscribed = true;
            }

            try
            {
                _ = _settings.RelabelRenamedRepositories();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Relabelling the repositories renamed before their alias followed failed.");
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
            try
            {
                await CatchUpLocalAsync(linked.Token).ConfigureAwait(false);
                await BackstopAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                // Shut down part-way: a clean stop, and nothing left to start.
                return;
            }

            _started = true;
        }
        finally
        {
            _gate.Release();
        }

        await WhenIdleAsync().ConfigureAwait(false);
    }

    /// <summary>Completes once every pass an edit or an arrival started has
    /// finished, including passes those passes caused.</summary>
    internal async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] running;
            lock (_passesLock)
            {
                _passes.RemoveAll(p => p.IsCompleted);
                running = [.. _passes];
            }

            if (running.Length == 0) return;
            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopping.Cancel();

        _settings.AliasesMoved -= OnAliasesMoved;
        _settings.Changed -= OnChanged;
    }

    private void OnAliasesMoved(IReadOnlyList<AliasMove> moves)
    {
        // Counted on the raising thread, before the pass is queued, so a backstop
        // already waiting on the gate sees the edit coming.
        Interlocked.Increment(ref _pendingEdits);

        // Started off the registry save's flow, so the pass carries none of its
        // ambient state. The edit's re-key is the person's: one change set for every
        // document the relabel moves (local ADR 0061 §2). What follows is the system's.
        using (ChangeContext.Detach())
        {
            Track(Task.Run(() => PassAsync(
                async cancellationToken =>
                {
                    try
                    {
                        using (ChangeContext.Begin(ChangeActor.Person, "renaming a repository"))
                        {
                            await ReKeyReplicatedAsync(moves, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pendingEdits);
                    }

                    using (ChangeContext.Begin(ChangeActor.System, "following a repository rename"))
                    {
                        await CatchUpLocalAsync(cancellationToken).ConfigureAwait(false);
                        await BackstopAsync(cancellationToken).ConfigureAwait(false);
                    }
                },
                whenSkipped: () => Interlocked.Decrement(ref _pendingEdits))));
        }
    }

    private void OnChanged()
    {
        // A moving edit's own Changed: its AliasesMoved follows once the save held,
        // and its pass does everything this one would.
        if (_settings.IsSavingAliasMovesOnThisThread) return;

        using (ChangeContext.Detach())
        {
            Track(Task.Run(() => PassAsync(async cancellationToken =>
            {
                using var change = ChangeContext.Begin(ChangeActor.System, "following a repository rename");
                await CatchUpLocalAsync(cancellationToken).ConfigureAwait(false);
                await BackstopAsync(cancellationToken).ConfigureAwait(false);
            })));
        }
    }

    private void Track(Task pass)
    {
        lock (_passesLock)
        {
            _passes.RemoveAll(p => p.IsCompleted);
            _passes.Add(pass);
        }
    }

    /// <summary>One pass under the gate. An event handler cannot await, so whatever
    /// escapes a pass is logged here rather than lost; a pass stopped by
    /// <see cref="Dispose"/> is a clean stop and logs nothing.</summary>
    private async Task PassAsync(Func<CancellationToken, Task> pass, Action? whenSkipped = null)
    {
        var ran = false;
        var stopping = _stopping.Token;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                ran = true;
                await pass(stopping).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Shut down while the pass ran: what it did not reach, the next start's
            // catch-up and backstop pick up.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Following a repository alias move failed.");
        }
        finally
        {
            if (!ran) whenSkipped?.Invoke();
        }
    }

    /// <summary>The editing device's half: each move group of the edit, oldest first,
    /// to every replicated store.</summary>
    private async Task ReKeyReplicatedAsync(IReadOnlyList<AliasMove> moves, CancellationToken cancellationToken)
    {
        foreach (var group in Groups(moves))
        {
            var mapping = Mapping(group);
            foreach (var store in _replicated)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await TryMoveAsync(store, mapping, AliasMoveOrigin.Edit, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Every move group past a local store's cursor, oldest first, to that store. Its
    /// cursor moves past a group once it took it, and its walk stops at the first
    /// group it did not, so the order holds for it without holding back the others.
    /// <para>
    /// A store with no cursor starts from what it holds. If it holds data under an
    /// alias a move names and no row carries, that data is from before the moves, and
    /// every group is applied in order. Otherwise it is on the current aliases already,
    /// whether it is a fresh device's or lost its cursor, and it starts at the newest
    /// move: replaying a historical swap would undo it.
    /// </para>
    /// </summary>
    private async Task CatchUpLocalAsync(CancellationToken cancellationToken)
    {
        if (_settings.HasUnsavedAliasMoves) return;

        var current = _settings.Current;
        var groups = Groups(current.AliasMoves).ToList();

        foreach (var (store, key) in _local)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var through = _settings.AliasMovesAppliedThrough(key);

            if (through is null)
            {
                if (HoldsAliasFromBeforeTheMoves(store, current) is not { } holds) continue;

                if (!holds)
                {
                    Mark(key, groups.Count == 0 ? DateTimeOffset.MinValue : groups[^1].Key);
                    continue;
                }

                through = DateTimeOffset.MinValue;
            }

            foreach (var group in groups.Where(g => g.Key > through))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await TryMoveAsync(store, Mapping(group), AliasMoveOrigin.Edit, cancellationToken).ConfigureAwait(false)) break;
                Mark(key, group.Key);
            }
        }
    }

    /// <summary>Whether the store holds data under an alias a move names and no row
    /// carries; null when the store could not say.</summary>
    private bool? HoldsAliasFromBeforeTheMoves(IRepositoryAliasKeyedStore store, GitHubSettings current)
    {
        if (current.AliasMoves.Count == 0) return false;

        if (Held(store) is not { } held) return null;

        return held.Any(alias =>
            !Carried(current, alias)
            && current.AliasMoves.Any(m => string.Equals(m.OldAlias, alias, StringComparison.Ordinal)));
    }

    /// <summary>What a replicated store holds under an alias no row carries but that
    /// resolves to a row, moved to that row's alias. A row always wins: an alias one
    /// carries is never moved.</summary>
    private async Task BackstopAsync(CancellationToken cancellationToken)
    {
        if (_settings.HasUnsavedAliasMoves || Volatile.Read(ref _pendingEdits) > 0) return;

        var current = _settings.Current;

        foreach (var store in _replicated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Held(store) is not { } held) continue;

            var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var alias in held)
            {
                if (Carried(current, alias)) continue;
                if (current.Find(alias) is { } row) mapping[alias] = row.Alias;
            }

            if (mapping.Count > 0) await TryMoveAsync(store, mapping, AliasMoveOrigin.Backstop, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The normalized aliases a store holds, or null when it could not
    /// say.</summary>
    private List<string>? Held(IRepositoryAliasKeyedStore store)
    {
        try
        {
            return [.. store.HeldAliases().Select(GitHubRepositoryRef.NormalizeAlias).Distinct(StringComparer.Ordinal)];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading the aliases {Store} holds failed.", store.GetType().Name);
            return null;
        }
    }

    private static bool Carried(GitHubSettings current, string alias) =>
        current.Repositories.Any(r => string.Equals(r.Alias, alias, StringComparison.Ordinal));

    private void Mark(string key, DateTimeOffset at)
    {
        if (_settings.MarkAliasMovesApplied(key, at) is { } error)
        {
            _logger.LogWarning("The alias move cursor of {Store} could not be saved: {Error}", key, error);
        }
    }

    private async Task<bool> TryMoveAsync(
        IRepositoryAliasKeyedStore store,
        IReadOnlyDictionary<string, string> mapping,
        AliasMoveOrigin origin,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await store.MoveAliasesAsync(mapping, origin, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation(
                    "Moved repository aliases in {Store}: {Mapping}.",
                    store.GetType().Name,
                    string.Join(", ", mapping.Select(m => $"{m.Key} -> {m.Value}")));
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Moving repository aliases in {Store} failed.", store.GetType().Name);
            return false;
        }
    }

    /// <summary>Each local store with the key its cursor is kept under: its type's full
    /// name, stable across starts. A second store of the same type gets an ordinal
    /// suffix, in registration order.</summary>
    private static List<(IRepositoryAliasKeyedStore, string)> KeyedByType(IEnumerable<IRepositoryAliasKeyedStore> stores)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var keyed = new List<(IRepositoryAliasKeyedStore, string)>();

        foreach (var store in stores)
        {
            var name = store.GetType().FullName ?? store.GetType().Name;
            var count = seen[name] = seen.GetValueOrDefault(name) + 1;
            keyed.Add((store, count == 1 ? name : $"{name}#{count}"));
        }

        return keyed;
    }

    private static IEnumerable<IGrouping<DateTimeOffset, AliasMove>> Groups(IEnumerable<AliasMove> moves) =>
        moves.GroupBy(m => m.At).OrderBy(g => g.Key);

    /// <summary>One group as one mapping, so a swap is applied at once. Within a group
    /// the later move of an alias wins, as it does in <see cref="GitHubSettings.Find"/>.</summary>
    private static Dictionary<string, string> Mapping(IEnumerable<AliasMove> group)
    {
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var move in group) mapping[move.OldAlias] = move.NewAlias;
        return mapping;
    }
}
