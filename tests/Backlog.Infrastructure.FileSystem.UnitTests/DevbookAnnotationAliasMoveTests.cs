using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Remarks follow a relabelled repository (local ADR 0060 §3): each keeps its id,
/// takes the new alias and a fresh stamp, so the next push carries it and the replica
/// merge, which goes by id and <c>UpdatedAt</c> (ADR 0011), replaces the old copy on
/// every other device. A copy that arrives under another alias than the one it is
/// held under moves between the files rather than doubling.
/// </summary>
public sealed class DevbookAnnotationAliasMoveTests : IDisposable
{
    private const string Old = "project-guidelines-mcp";
    private const string New = "app-defaults";
    private const string Chapter = ".domain/devbook/features.md";

    private static readonly DateTimeOffset Morning = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("devbook-annotation-alias-move-tests");

    private readonly FakeTimeProvider _time = new(Morning);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private DevbookAnnotationStore Store(string device = "a") => new(_temp.Combine(device), _time);

    private string FolderOf(string device = "a") => Path.Combine(_temp.Combine(device), DevbookAnnotationStore.FolderName);

    public void Dispose() => _temp.Dispose();

    private static Dictionary<string, string> Moving(params (string From, string To)[] moves) =>
        moves.ToDictionary(move => move.From, move => move.To, StringComparer.Ordinal);

    private static Task<bool> Move(DevbookAnnotationStore store, params (string From, string To)[] moves) =>
        ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(Moving(moves), Cancellation);

    /// <summary>A remark past its draft, so a push selects it.</summary>
    private static DevbookAnnotation Remark(DevbookAnnotationStore store, string alias, string body = "Check this.")
    {
        var added = store.Add(alias, Chapter, 2, "jos");
        store.Edit(added.Id, body);

        return store.Find(added.Id)!;
    }

    [Fact]
    public void The_store_travels_between_devices_and_holds_the_aliases_with_remarks()
    {
        var store = Store();
        Remark(store, Old);

        var keyed = (IRepositoryAliasKeyedStore)store;

        Assert.Equal(AliasKeyedStoreKind.Replicated, keyed.Kind);
        Assert.Equal([Old], keyed.HeldAliases());
    }

    [Fact]
    public async Task A_moved_remark_keeps_its_id_takes_the_new_alias_and_the_next_push_carries_it()
    {
        var store = Store();
        var remark = Remark(store, Old);
        var changes = 0;
        store.Changed += () => changes++;
        _time.Advance(TimeSpan.FromMinutes(5));

        var moved = await Move(store, (Old, New));

        Assert.True(moved);
        Assert.Equal(1, changes);
        var pushed = Assert.Single(store.ListChangedSince(Morning));
        Assert.Equal(remark.Id, pushed.Id);
        Assert.Equal(New, pushed.RepositoryAlias);
        Assert.Equal("Check this.", pushed.Body);
        Assert.Equal(Morning.AddMinutes(5), pushed.UpdatedAt);

        var reread = Store();
        Assert.Single(reread.List(New, Chapter));
        Assert.Empty(reread.List(Old, Chapter));
        Assert.Single(Directory.GetFiles(FolderOf(), "*.json"));
    }

    [Fact]
    public async Task A_swap_exchanges_the_two_repositories_remarks()
    {
        var store = Store();
        var a = Remark(store, "a", "on a");
        var b = Remark(store, "b", "on b");

        await Move(store, ("a", "b"), ("b", "a"));

        var reread = Store();
        Assert.Equal(b.Id, Assert.Single(reread.List("a", Chapter)).Id);
        Assert.Equal(a.Id, Assert.Single(reread.List("b", Chapter)).Id);
    }

    [Fact]
    public async Task Remarks_already_under_the_new_alias_stay_beside_the_moved_ones()
    {
        var store = Store();
        Remark(store, Old, "old");
        Remark(store, New, "new");

        await Move(store, (Old, New));

        Assert.Equal(["new", "old"], Store().List(New, Chapter).Select(r => r.Body).Order(StringComparer.Ordinal));
    }

    /// <summary>Another device moved the remark first; its newer copy arrives here,
    /// where the remark is still held under the old alias.</summary>
    [Fact]
    public async Task A_moved_copy_arriving_from_another_device_replaces_the_old_alias_copy_without_a_duplicate()
    {
        var mine = Store("a");
        var remark = Remark(mine, Old);
        var theirs = Store("b");
        theirs.Apply(remark);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Move(theirs, (Old, New));

        mine.Apply(Assert.Single(theirs.ListChangedSince(Morning)));

        Assert.Equal(New, mine.Find(remark.Id)!.RepositoryAlias);
        Assert.Empty(mine.List(Old, Chapter));
        Assert.Single(mine.List(New, Chapter));
        Assert.Single(mine.ListChangedSince(DateTimeOffset.MinValue));

        var reread = Store("a");
        Assert.Empty(reread.List(Old, Chapter));
        Assert.Single(reread.List(New, Chapter));
    }

    /// <summary>Review: slice 5 round 1, blocker 1. A copy this device pulled is moved
    /// without becoming the newest version, so it never beats the editing device's
    /// re-keyed copy.</summary>
    [Fact]
    public async Task A_backstop_move_keeps_the_remark_own_stamp()
    {
        var store = Store();
        var remark = Remark(store, Old);
        _time.Advance(TimeSpan.FromMinutes(5));

        await ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(Moving((Old, New)), AliasMoveOrigin.Backstop, Cancellation);

        var moved = store.Find(remark.Id)!;
        Assert.Equal(New, moved.RepositoryAlias);
        Assert.Equal(remark.UpdatedAt, moved.UpdatedAt);
    }

    /// <summary>Where a copy of the remark already arrived under another alias, the
    /// backstop keeps whichever copy is later, as the replica merge does.</summary>
    [Fact]
    public async Task A_backstop_move_keeps_the_later_copy_of_a_remark_held_twice()
    {
        var store = Store();
        var remark = Remark(store, Old, "edited here later");
        // The editing device's earlier copy, already under the new alias. Written
        // straight into a file of its own, as a failed delete leaves it.
        var arrived = remark with { RepositoryAlias = New, Body = "theirs", UpdatedAt = remark.UpdatedAt.AddMinutes(-1) };
        var other = Store("b");
        other.Apply(arrived);
        File.Copy(
            Path.Combine(FolderOf("b"), CachePaths.Safe(New) + ".json"),
            Path.Combine(FolderOf(), CachePaths.Safe(New) + ".json"));
        var reopened = Store();

        await ((IRepositoryAliasKeyedStore)reopened).MoveAliasesAsync(Moving((Old, New)), AliasMoveOrigin.Backstop, Cancellation);

        var kept = Assert.Single(Store().List(New, Chapter));
        Assert.Equal("edited here later", kept.Body);
        Assert.Empty(Store().List(Old, Chapter));
    }

    /// <summary>Review: slice 5 round 1, blocker 2.</summary>
    [Fact]
    public async Task A_target_that_cannot_be_written_leaves_the_old_file_as_it_was()
    {
        var store = Store();
        Remark(store, Old);
        var oldFile = Assert.Single(Directory.GetFiles(FolderOf(), "*.json"));
        var before = File.ReadAllText(oldFile);
        Directory.CreateDirectory(Path.Combine(FolderOf(), CachePaths.Safe(New) + ".json"));

        await Move(store, (Old, New));

        Assert.Equal(before, File.ReadAllText(oldFile));
        Assert.Single(Store().List(Old, Chapter));
    }

    /// <summary>Two files holding one remark — a relabel whose old file would not go —
    /// read as one remark, the later copy, so an edit never lands on the stale one.</summary>
    [Fact]
    public void A_remark_two_files_hold_reads_as_its_later_copy()
    {
        var store = Store();
        var remark = Remark(store, Old, "stale");
        var other = Store("b");
        other.Apply(remark with { RepositoryAlias = New, Body = "current", UpdatedAt = remark.UpdatedAt.AddMinutes(1) });
        File.Copy(
            Path.Combine(FolderOf("b"), CachePaths.Safe(New) + ".json"),
            Path.Combine(FolderOf(), CachePaths.Safe(New) + ".json"));

        var reopened = Store();

        Assert.Equal("current", reopened.Find(remark.Id)!.Body);
        Assert.Empty(reopened.List(Old, Chapter));
        Assert.Single(reopened.ListChangedSince(DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task A_mapping_that_names_no_held_alias_changes_nothing()
    {
        var store = Store();
        Remark(store, Old);
        var changes = 0;
        store.Changed += () => changes++;

        var moved = await Move(store, ("devbook", "devbook-renamed"));

        Assert.False(moved);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task A_second_call_with_the_same_mapping_changes_nothing()
    {
        var store = Store();
        Remark(store, Old);

        await Move(store, (Old, New));
        var again = await Move(store, (Old, New));

        Assert.False(again);
        Assert.Single(store.List(New, Chapter));
    }
}
