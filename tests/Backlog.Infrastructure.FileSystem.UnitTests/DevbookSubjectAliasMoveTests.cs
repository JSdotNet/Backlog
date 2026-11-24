using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Devbook subject state follows a relabelled repository (local ADR 0060 §3). A
/// subject's id is derived from its alias (local ADR 0044), so a moved state is a new
/// document under the id its new alias gives it — the only id the replica merge files
/// a state under — stamped so the next push carries it. The old document is dropped
/// here and never pushed as a tombstone: the subject wire has none (the person's
/// answer to the scope's Q1).
/// </summary>
public sealed class DevbookSubjectAliasMoveTests : IDisposable
{
    private const string Host = "DEV-TOWER";
    private const string Old = "project-guidelines-mcp";
    private const string New = "app-defaults";
    private const string Inbox = "domain/inbox/context.md";

    private static readonly DevbookSubject Debt = DevbookSubject.Debt(".devbook/arc42/tdr/0001-almanac.md");
    private static readonly DevbookSubject Change = DevbookSubject.Proposal("pay-by-card");

    private static readonly DateTimeOffset Morning = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("devbook-subject-alias-move-tests");

    private readonly FakeTimeProvider _time = new(Morning);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private DevbookSubjectStateStore Store() => new(_temp.Path, _time);

    private string Folder => Path.Combine(_temp.Path, DevbookSubjectStateStore.FolderName);

    public void Dispose() => _temp.Dispose();

    private static Dictionary<string, string> Moving(params (string From, string To)[] moves) =>
        moves.ToDictionary(move => move.From, move => move.To, StringComparer.Ordinal);

    private static Task<bool> Move(DevbookSubjectStateStore store, params (string From, string To)[] moves) =>
        ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(Moving(moves), Cancellation);

    private static Task<bool> Move(DevbookSubjectStateStore store, AliasMoveOrigin origin, params (string From, string To)[] moves) =>
        ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(Moving(moves), origin, Cancellation);

    /// <summary>The direction map is no part of the replicated store: its aliases are
    /// <see cref="DevbookSyncDirectionAliases"/>'.</summary>
    [Fact]
    public void The_states_travel_between_devices_and_only_the_aliases_with_a_state_are_held()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);
        ((IDevbookSyncDirectionStore)store).Set("devbook", Inbox, "pull", Host);

        var keyed = (IRepositoryAliasKeyedStore)store;

        Assert.Equal(AliasKeyedStoreKind.Replicated, keyed.Kind);
        Assert.Equal([Old], keyed.HeldAliases());
    }

    /// <summary>The backstop moves what this device holds without making it a newer
    /// version: a copy it pulled must not be pushed back over the editing device's
    /// re-keyed one (review: slice 5 round 1, blocker 1).</summary>
    [Fact]
    public async Task A_backstop_move_keeps_the_state_own_stamp()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);
        _time.Advance(TimeSpan.FromMinutes(5));

        var moved = await Move(store, AliasMoveOrigin.Backstop, (Old, New));

        Assert.True(moved);
        var state = store.Get(New, Debt)!;
        Assert.Equal(DevbookSubject.IdFor(New, Debt), state.Id);
        Assert.Equal(Morning, state.UpdatedAt);
        Assert.Empty(store.ListChangedSince(Morning));
    }

    /// <summary>A state set here under the old alias after the editing device moved
    /// the subject is later than that device's re-keyed copy, and the backstop keeps
    /// it rather than losing it to the copy that arrived first.</summary>
    [Fact]
    public async Task A_backstop_move_keeps_the_later_of_two_states_for_one_subject()
    {
        var store = Store();
        store.Apply(new DevbookSubjectState(DevbookSubject.IdFor(New, Debt), New, Debt, "identified", "OTHER-PC", Morning, null, null, Morning));
        _time.Advance(TimeSpan.FromMinutes(1));
        store.SetState(Old, Debt, "planned", Host);

        await Move(store, AliasMoveOrigin.Backstop, (Old, New));

        Assert.Equal("planned", Store().Get(New, Debt)!.State);
    }

    [Fact]
    public async Task A_backstop_move_keeps_the_target_state_when_it_is_the_later()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);
        _time.Advance(TimeSpan.FromMinutes(1));
        store.SetState(New, Debt, "identified", "OTHER-PC");

        await Move(store, AliasMoveOrigin.Backstop, (Old, New));

        Assert.Equal("identified", Store().Get(New, Debt)!.State);
    }

    /// <summary>A target that cannot be written leaves the source on disk as it was,
    /// so nothing is lost and the next start's backstop moves it again (review: slice 5
    /// round 1, blocker 2).</summary>
    [Fact]
    public async Task A_target_that_cannot_be_written_leaves_the_old_file_as_it_was()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);
        ((IDevbookSyncDirectionStore)store).Set(Old, Inbox, "pull", Host);
        var oldFile = Assert.Single(Directory.GetFiles(Folder, "*.json"));
        var before = File.ReadAllText(oldFile);
        // A folder where the target's file goes: the write fails as a locked file does.
        Directory.CreateDirectory(Path.Combine(Folder, CachePaths.Safe(New) + ".json"));

        await Move(store, (Old, New));

        Assert.Equal(before, File.ReadAllText(oldFile));
        var reread = Store();
        Assert.Equal("planned", reread.Get(Old, Debt)!.State);
        Assert.Equal("pull", ((IDevbookSyncDirectionStore)reread).Get(Old, Inbox));
    }

    /// <summary>Moving the states rewrites the old file without them, keeping the
    /// direction map that stays until its own store moves it.</summary>
    [Fact]
    public async Task Moving_the_states_leaves_the_direction_map_where_it_is()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);
        ((IDevbookSyncDirectionStore)store).Set(Old, Inbox, "pull", Host);

        await Move(store, (Old, New));

        var reread = Store();
        Assert.Equal("planned", reread.Get(New, Debt)!.State);
        Assert.Empty(reread.List(Old));
        Assert.Equal("pull", ((IDevbookSyncDirectionStore)reread).Get(Old, Inbox));
        Assert.Null(((IDevbookSyncDirectionStore)reread).Get(New, Inbox));
    }

    [Fact]
    public async Task A_moved_state_is_a_new_document_under_the_id_its_new_alias_gives_it_and_the_next_push_carries_it()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);
        var changes = 0;
        store.Changed += () => changes++;
        _time.Advance(TimeSpan.FromMinutes(5));

        var moved = await Move(store, (Old, New));

        Assert.True(moved);
        Assert.Equal(1, changes);

        var pushed = Assert.Single(store.ListChangedSince(Morning));
        // The replica merge files a state only under IdFor(its alias, its subject).
        Assert.Equal(DevbookSubject.IdFor(New, Debt), pushed.Id);
        Assert.Equal(New, pushed.RepositoryAlias);
        Assert.Equal("planned", pushed.State);
        Assert.Equal(Host, pushed.By);
        Assert.Equal(Morning, pushed.At);
        Assert.Equal(Morning.AddMinutes(5), pushed.UpdatedAt);
    }

    [Fact]
    public async Task The_old_document_is_dropped_from_this_device_and_its_file_with_it()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);

        await Move(store, (Old, New));

        Assert.Null(store.Find(DevbookSubject.IdFor(Old, Debt)));
        Assert.Empty(store.List(Old));

        var reread = Store();
        Assert.Equal("planned", reread.Get(New, Debt)!.State);
        Assert.Null(reread.Get(Old, Debt));
        Assert.Single(Directory.GetFiles(Folder, "*.json"));
    }

    [Fact]
    public async Task Where_the_new_alias_already_holds_the_subject_its_state_wins()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);
        store.SetState(New, Debt, "identified", "OTHER-PC");
        store.SetState(Old, Change, "draft", Host);

        await Move(store, (Old, New));

        var reread = Store();
        Assert.Equal("identified", reread.Get(New, Debt)!.State);
        Assert.Equal("draft", reread.Get(New, Change)!.State);
        Assert.Empty(reread.List(Old));
    }

    [Fact]
    public async Task A_swap_exchanges_the_two_repositories_states()
    {
        var store = Store();
        store.SetState("a", Debt, "planned", Host);
        store.SetState("b", Debt, "identified", Host);

        await Move(store, ("a", "b"), ("b", "a"));

        var reread = Store();
        Assert.Equal("identified", reread.Get("a", Debt)!.State);
        Assert.Equal("planned", reread.Get("b", Debt)!.State);
        Assert.Equal(DevbookSubject.IdFor("a", Debt), reread.Get("a", Debt)!.Id);
    }

    /// <summary>No feed carries the direction map, so every device moves its own from
    /// the registry's move groups (review: slice 5 round 1, blocker 3).</summary>
    [Fact]
    public void The_direction_map_is_a_local_store_holding_the_aliases_with_a_direction_or_the_mark()
    {
        var store = Store();
        store.SetState("backlog", Debt, "planned", Host);
        ((IDevbookSyncDirectionStore)store).Set(Old, Inbox, "pull", Host);
        ((IDevbookSyncDirectionStore)store).Import("devbook", []);

        var directions = new DevbookSyncDirectionAliases(store);

        Assert.Equal(AliasKeyedStoreKind.Local, directions.Kind);
        Assert.Equal(["devbook", Old], directions.HeldAliases().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_direction_map_and_its_imported_mark_move_the_target_winning_and_the_states_stay()
    {
        var store = Store();
        var directions = (IDevbookSyncDirectionStore)store;
        store.SetState(Old, Debt, "planned", Host);
        directions.Set(Old, Inbox, "pull", Host);
        directions.Set(Old, "domain/tasks/domain.md#task", "push", Host);
        directions.Set(New, Inbox, "push", Host);
        directions.Import(Old, []);
        var directionChanges = 0;
        var stateChanges = 0;
        directions.Changed += () => directionChanges++;
        store.Changed += () => stateChanges++;

        var moved = await new DevbookSyncDirectionAliases(store).MoveAliasesAsync(Moving((Old, New)), Cancellation);

        Assert.True(moved);
        var reread = Store();
        var rereadDirections = (IDevbookSyncDirectionStore)reread;
        Assert.Equal("push", rereadDirections.Get(New, Inbox));
        Assert.Equal("push", rereadDirections.Get(New, "domain/tasks/domain.md#task"));
        Assert.True(rereadDirections.IsImported(New));
        Assert.Empty(rereadDirections.List(Old));
        Assert.False(rereadDirections.IsImported(Old));
        Assert.Equal("planned", reread.Get(Old, Debt)!.State);
        Assert.Equal(1, directionChanges);
        Assert.Equal(0, stateChanges);
    }

    [Fact]
    public async Task A_swap_exchanges_the_two_direction_maps()
    {
        var store = Store();
        var directions = (IDevbookSyncDirectionStore)store;
        directions.Set("a", Inbox, "pull", Host);
        directions.Set("b", Inbox, "push", Host);

        await new DevbookSyncDirectionAliases(store).MoveAliasesAsync(Moving(("a", "b"), ("b", "a")), Cancellation);

        var reread = (IDevbookSyncDirectionStore)Store();
        Assert.Equal("push", reread.Get("a", Inbox));
        Assert.Equal("pull", reread.Get("b", Inbox));
    }

    [Fact]
    public async Task A_second_direction_move_with_the_same_mapping_changes_nothing()
    {
        var store = Store();
        ((IDevbookSyncDirectionStore)store).Set(Old, Inbox, "pull", Host);
        var directions = new DevbookSyncDirectionAliases(store);

        await directions.MoveAliasesAsync(Moving((Old, New)), Cancellation);
        var again = await directions.MoveAliasesAsync(Moving((Old, New)), Cancellation);

        Assert.False(again);
        Assert.Equal("pull", ((IDevbookSyncDirectionStore)store).Get(New, Inbox));
    }

    [Fact]
    public async Task A_mapping_that_names_no_held_alias_changes_nothing()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);
        var changes = 0;
        store.Changed += () => changes++;

        var moved = await Move(store, ("devbook", "devbook-renamed"));

        Assert.False(moved);
        Assert.Equal(0, changes);
        Assert.Empty(store.ListChangedSince(Morning));
    }

    [Fact]
    public async Task A_second_call_with_the_same_mapping_changes_nothing()
    {
        var store = Store();
        store.SetState(Old, Debt, "planned", Host);

        await Move(store, (Old, New));
        var again = await Move(store, (Old, New));

        Assert.False(again);
        Assert.Equal("planned", store.Get(New, Debt)!.State);
    }
}
