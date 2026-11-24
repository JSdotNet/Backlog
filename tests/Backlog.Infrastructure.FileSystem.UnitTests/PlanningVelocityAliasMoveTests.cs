using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The pace document follows a relabelled repository (local ADR 0060 §3): a
/// repository's own pace moves to its new alias, the target keeps its own where it
/// has one, and the move is a new version of the document, so it travels the way a
/// change made on the slider does.
/// </summary>
public sealed class PlanningVelocityAliasMoveTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("planning-velocity-alias-move-tests");

    private readonly FakeTimeProvider _clock = new(Morning);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FileOf => _temp.Combine("planning-velocity.json");

    private PlanningVelocitySettingsStore Store() => new(FileOf, _clock);

    public void Dispose() => _temp.Dispose();

    private static Dictionary<string, string> Moving(params (string From, string To)[] moves) =>
        moves.ToDictionary(move => move.From, move => move.To, StringComparer.Ordinal);

    [Fact]
    public void The_store_travels_between_devices()
    {
        Assert.Equal(AliasKeyedStoreKind.Replicated, ((IRepositoryAliasKeyedStore)Store()).Kind);
    }

    [Fact]
    public void It_holds_the_aliases_that_keep_a_pace_of_their_own()
    {
        var store = Store();
        store.SetOwn(5m, "backlog");
        store.Choose(PaceSource.LastFourWeeks, "devbook");

        Assert.Equal(["backlog", "devbook"], ((IRepositoryAliasKeyedStore)store).HeldAliases().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_relabelled_repository_keeps_its_pace_under_its_new_alias_and_the_document_travels()
    {
        var store = Store();
        store.SetOwn(5m, "project-guidelines-mcp");
        var before = await ((IRoadmapReplicaStore)store).ReadAsync(Cancellation);
        var changes = 0;
        store.Changed += () => changes++;
        _clock.Advance(TimeSpan.FromMinutes(2));

        var moved = await ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(
            Moving(("project-guidelines-mcp", "app-defaults")), Cancellation);

        Assert.True(moved);
        Assert.True(store.KeepsOwnPace("app-defaults"));
        Assert.False(store.KeepsOwnPace("project-guidelines-mcp"));
        Assert.Equal(5m, Store().StoryPointsPerWeekFor("app-defaults"));
        Assert.Equal(PaceSource.Set, Store().SourceFor("app-defaults"));
        Assert.Equal(1, changes);
        var after = await ((IRoadmapReplicaStore)store).ReadAsync(Cancellation);
        Assert.Equal(Morning, before!.UpdatedAt);
        Assert.Equal(Morning.AddMinutes(2), after!.UpdatedAt);
    }

    /// <summary>Review: slice 5 round 1, blocker 1. A pace this device pulled is moved
    /// at the stamp it holds, so the editing device's newer document still wins.</summary>
    [Fact]
    public async Task A_backstop_move_keeps_the_document_stamp()
    {
        var store = Store();
        store.SetOwn(5m, "project-guidelines-mcp");
        _clock.Advance(TimeSpan.FromMinutes(2));

        var moved = await ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(
            Moving(("project-guidelines-mcp", "app-defaults")), AliasMoveOrigin.Backstop, Cancellation);

        Assert.True(moved);
        Assert.True(Store().KeepsOwnPace("app-defaults"));
        var after = await ((IRoadmapReplicaStore)store).ReadAsync(Cancellation);
        Assert.Equal(Morning, after!.UpdatedAt);
    }

    [Fact]
    public async Task Where_the_new_alias_keeps_a_pace_its_own_wins()
    {
        var store = Store();
        store.SetOwn(5m, "backlog-old");
        store.SetOwn(9m, "backlog");

        var moved = await ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(
            Moving(("backlog-old", "backlog")), Cancellation);

        Assert.True(moved);
        Assert.Equal(9m, store.StoryPointsPerWeekFor("backlog"));
        Assert.False(store.KeepsOwnPace("backlog-old"));
    }

    [Fact]
    public async Task A_swap_exchanges_the_two_paces_rather_than_running_one_move_into_the_other()
    {
        var store = Store();
        store.SetOwn(5m, "a");
        store.SetOwn(9m, "b");

        await ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(Moving(("a", "b"), ("b", "a")), Cancellation);

        Assert.Equal(9m, Store().StoryPointsPerWeekFor("a"));
        Assert.Equal(5m, Store().StoryPointsPerWeekFor("b"));
    }

    [Fact]
    public async Task A_mapping_that_names_no_held_alias_changes_nothing_and_leaves_the_stamp()
    {
        var store = Store();
        store.SetOwn(5m, "backlog");
        var before = File.ReadAllText(FileOf);
        var changes = 0;
        store.Changed += () => changes++;
        _clock.Advance(TimeSpan.FromMinutes(2));

        var moved = await ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(
            Moving(("devbook", "devbook-renamed")), Cancellation);

        Assert.False(moved);
        Assert.Equal(0, changes);
        Assert.Equal(before, File.ReadAllText(FileOf));
    }

    [Fact]
    public async Task A_second_call_with_the_same_mapping_changes_nothing()
    {
        var store = Store();
        store.SetOwn(5m, "backlog");
        var mapping = Moving(("backlog", "backlog-renamed"));

        await ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(mapping, Cancellation);
        var again = await ((IRepositoryAliasKeyedStore)store).MoveAliasesAsync(mapping, Cancellation);

        Assert.False(again);
        Assert.Equal(5m, store.StoryPointsPerWeekFor("backlog-renamed"));
    }
}
