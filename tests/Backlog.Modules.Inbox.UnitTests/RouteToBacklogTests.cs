using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Routing asks Tasks first and marks the item second, so a failure on the far
/// side leaves the item exactly as it was. What Tasks is asked for is the item's
/// facts in the Inbox's words: no entry text crosses this boundary.
/// </summary>
public sealed class RouteToBacklogTests
{
    private static readonly DateTimeOffset Decision = Items.Noon.AddHours(1);

    [Fact]
    public async Task Two_repositories_make_two_entries_and_the_item_remembers_both()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual("Ship the thing");
        item.SetTags([new InboxTag("deploy", false)]);
        item.SetRepoIds(["jsdotnet/backlog", "jsdotnet/other"]);
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TaskIds.Count);

        var request = Assert.Single(target.Requests);
        Assert.Equal(item.Id, request.InboxItemId);
        Assert.Equal("Ship the thing", request.Title);
        Assert.Equal(["deploy"], request.Tags);
        Assert.Equal(["jsdotnet/backlog", "jsdotnet/other"], request.RepoIds);

        Assert.Equal(InboxStatus.Triaged, item.Status);
        Assert.Equal(result.Value.TaskIds, item.Routing!.TaskIds);
        Assert.Equal(["jsdotnet/backlog", "jsdotnet/other"], item.Routing.RepoIds);
        Assert.Equal(Decision, item.Routing.RoutedAt);
    }

    /// <summary>The siblings of a route to several repositories name each other
    /// and share a tag made from the item's id, which only works because every
    /// repository goes to the target in one request with that id — never one
    /// request per repository. The text itself is the adapter's, pinned in
    /// <c>InboxBacklogTargetTests</c>.</summary>
    [Fact]
    public async Task Three_repositories_go_to_the_target_as_one_request_so_the_siblings_can_name_each_other()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual("Ship the thing");
        item.SetRepoIds(["jsdotnet/backlog", "jsdotnet/other", "jsdotnet/third"]);
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(target.Requests);
        Assert.Equal(item.Id, request.InboxItemId);
        Assert.Equal(["jsdotnet/backlog", "jsdotnet/other", "jsdotnet/third"], request.RepoIds);
        Assert.Equal(3, item.Routing!.TaskIds.Count);
    }

    [Fact]
    public async Task No_repository_makes_one_untargeted_entry()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.TaskIds);
        Assert.Empty(Assert.Single(target.Requests).RepoIds);
        Assert.Empty(item.Routing!.RepoIds);
    }

    [Fact]
    public async Task A_failure_from_tasks_leaves_the_item_untouched()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget { FailWith = Error.Validation("entry.needs_title", "nope") };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("entry.needs_title", result.Error.Code);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.False(item.IsRouted);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task Routing_twice_costs_no_second_set_of_entries()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        await Route(store, target, item.Id);
        var again = await Route(store, target, item.Id);

        Assert.True(again.IsFailure);
        Assert.Equal("inbox.item.invalid_transition", again.Error.Code);
        Assert.Single(target.Requests);
    }

    [Fact]
    public async Task An_item_that_is_not_there_is_not_found()
    {
        var result = await Route(new InMemoryInboxStore(), new FakeBacklogTarget(), Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.item.not_found", result.Error.Code);
    }

    [Fact]
    public async Task A_replica_capture_that_routes_owes_the_replica_an_acknowledgement()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone();
        store.Seed(item);

        await Route(store, new FakeBacklogTarget(), item.Id);

        Assert.True(item.ReplicaAckPending);
    }

    /// <summary>The pass shows each single route's entry type and sends it:
    /// the type travels as a fact, in the advisor's lower-case words, and the
    /// adapter writes the type word.</summary>
    [Theory]
    [InlineData("prompt", "prompt")]
    [InlineData("test", "test")]
    [InlineData("Task", "task")]
    [InlineData(" PROMPT ", "prompt")]
    public async Task An_entry_type_reaches_the_target(string entryType, string expected)
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, item.Id, entryType);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, Assert.Single(target.Requests).EntryType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task No_entry_type_leaves_the_type_to_the_target(string? entryType)
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        await Route(store, target, item.Id, entryType);

        Assert.Null(Assert.Single(target.Requests).EntryType);
    }

    /// <summary>Only the types a single route may name reach the target — never
    /// a word the entry grammar would read as something else.</summary>
    [Theory]
    [InlineData("bug")]
    [InlineData("plan")]
    [InlineData("task` `#x")]
    public async Task An_unknown_entry_type_is_refused_before_the_target_is_asked(string entryType)
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, item.Id, entryType);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.route.unknown_entry_type", result.Error.Code);
        Assert.Empty(target.Requests);
        Assert.False(item.IsRouted);
        Assert.Empty(store.ItemWrites);
    }

    /// <summary>"Keep one, attach the other": the captures archived as
    /// duplicates of the kept item travel with it as facts — title and link —
    /// oldest capture first. Ones archived as another item's, or merged into a
    /// task, are not its.</summary>
    [Fact]
    public async Task The_captures_archived_as_its_duplicates_travel_with_the_kept_item()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var kept = Items.Manual("Read the caching article");
        var older = Captured("Caching article", "https://example.com/caching", Items.Noon.AddMinutes(1));
        var newer = Captured("Same article again", null, Items.Noon.AddMinutes(2));
        var other = Items.Manual("Something else");
        var elsewhere = Captured("Not this one", "https://example.com/other", Items.Noon.AddMinutes(3));
        var merged = Captured("Merged into a task", "https://example.com/task", Items.Noon.AddMinutes(4));
        foreach (var seeded in new[] { kept, older, newer, other, elsewhere, merged }) store.Seed(seeded);
        newer.Archive(Items.Noon, kept.Id);
        older.Archive(Items.Noon, kept.Id);
        elsewhere.Archive(Items.Noon, other.Id);
        merged.Archive(Items.Noon, Guid.CreateVersion7(), task: true);

        await Route(store, target, kept.Id);

        var attached = Assert.Single(target.Requests).Attached;
        Assert.NotNull(attached);
        Assert.Equal(
            [("Caching article", (string?)"https://example.com/caching"), ("Same article again", null)],
            attached.Select(duplicate => (duplicate.Title, duplicate.SourceUrl)));
    }

    /// <summary>Archiving names the root only for the item archived: a kept
    /// item that is itself archived later as another's duplicate keeps its own
    /// duplicates pointing at it. Routing the new root still carries every
    /// capture of the thought, however many steps away.</summary>
    [Fact]
    public async Task Captures_a_chain_of_duplicates_away_still_travel_with_the_root()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var root = Captured("Read the caching article", null, Items.Noon);
        var middle = Captured("Caching article", "https://example.com/caching", Items.Noon.AddMinutes(2));
        var leaf = Captured("Same article again", "https://example.com/again", Items.Noon.AddMinutes(1));
        foreach (var seeded in new[] { root, middle, leaf }) store.Seed(seeded);
        leaf.Archive(Items.Noon, middle.Id);
        middle.Archive(Items.Noon, root.Id);

        await Route(store, target, root.Id);

        Assert.Equal(
            ["Same article again", "Caching article"],
            (Assert.Single(target.Requests).Attached ?? []).Select(duplicate => duplicate.Title));
    }

    /// <summary>Undo of the attach is the restore that takes the archive back:
    /// the capture is no longer a duplicate, so the next route carries none.</summary>
    [Fact]
    public async Task A_restored_capture_no_longer_travels_with_the_kept_item()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var kept = Items.Manual("Read the caching article");
        var capture = Captured("Caching article", "https://example.com/caching", Items.Noon.AddMinutes(1));
        store.Seed(kept);
        store.Seed(capture);
        capture.Archive(Items.Noon, kept.Id);
        capture.Restore(Items.Noon);

        await Route(store, target, kept.Id);

        Assert.Empty(Assert.Single(target.Requests).Attached ?? []);
    }

    private static InboxItem Captured(string title, string? sourceUrl, DateTimeOffset capturedAt) =>
        new(Guid.CreateVersion7(), title, string.Empty, sourceUrl, capturedAt, capturedAt,
            ContentKind.Text, new InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);

    private static Task<Result<Abstractions.DataTransferObjects.InboxRoutedDto>> Route(
        InMemoryInboxStore store,
        FakeBacklogTarget target,
        Guid id,
        string? entryType = null) =>
        new RouteToBacklogCommandHandler(store, target, new FakeTimeProvider(Decision))
            .Handle(new RouteToBacklogCommand(id, entryType), TestContext.Current.CancellationToken);
}
