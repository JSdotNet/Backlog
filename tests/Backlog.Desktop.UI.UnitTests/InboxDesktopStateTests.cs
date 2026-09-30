using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox state on its own, without a pane over it: what it does with two
/// reloads in the air at once. A reload is asked for by the pane, by the shell
/// after a sync, and by every act on an item, and nothing serialises them —
/// so a slow read started first and finished last must not put its older
/// snapshot over the newer one already on screen.
/// </summary>
public sealed class InboxDesktopStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-state-tests", Guid.NewGuid().ToString("n"));

    /// <summary>
    /// A rename applied on another install reaches this one as a record in the
    /// shared registry, and inbox items do not travel by the sync service — so
    /// the pass on start is the only thing that would ever move them. Every
    /// remembered rename is replayed in order, oldest first, which is what
    /// carries an item through two renames to where the second one went.
    /// </summary>
    [Fact]
    public async Task Starting_replays_the_registry_rename_record_over_the_inbox()
    {
        var inbox = new FakeInboxItems();
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Assert.Null(settings.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(settings.RenameRepository("backlog", "JSdotNet/Backlog-2", out _));
        Assert.Null(settings.RenameRepository("backlog", "Someone/Backlog-3", out _));
        var state = new InboxDesktopState(inbox, settings);

        await state.InitializeAsync();

        Assert.Equal(
            [("JSdotNet/Backlog", "JSdotNet/Backlog-2"), ("JSdotNet/Backlog-2", "Someone/Backlog-3")],
            inbox.Renames);
    }

    [Fact]
    public async Task An_older_reload_that_finishes_after_a_newer_one_is_ignored()
    {
        var inbox = new FakeInboxItems();
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")));

        // Each read waits on its own gate, so the test decides which finishes
        // first — the second one, here, as a fast read overtaking a slow one.
        var gates = new Queue<TaskCompletionSource>();
        inbox.BeforeSnapshot = () =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gates.Enqueue(gate);
            return gate.Task;
        };

        var changes = 0;
        state.Changed += () => changes++;

        var older = state.ReloadAsync();
        var olderGate = gates.Dequeue();

        // Something arrives between the two reads, so the snapshots differ.
        inbox.Seed("Arrived by sync", channel: "mobile");

        var newer = state.ReloadAsync();
        var newerGate = gates.Dequeue();

        newerGate.SetResult();
        await newer;

        Assert.True(state.Loaded);
        Assert.Single(state.Items);
        Assert.Equal(1, changes);

        olderGate.SetResult();
        await older;

        // The older snapshot had no items; taking it would empty the pane.
        Assert.Single(state.Items);
        Assert.Equal("Arrived by sync", state.Items[0].Title);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Reloads_that_finish_in_order_each_land()
    {
        var inbox = new FakeInboxItems();
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")));

        await state.ReloadAsync();
        Assert.Empty(state.Items);

        inbox.Seed("First");
        await state.ReloadAsync();
        Assert.Single(state.Items);

        inbox.Seed("Second");
        await state.ReloadAsync();
        Assert.Equal(2, state.Items.Count);
    }

    /// <summary>The Deferred row is a slice like any other: opening it drops a
    /// selection made in the queue, and a capture made from it goes back to the
    /// Inbox and drops one made there.</summary>
    [Fact]
    public async Task Opening_the_deferred_slice_forgets_the_selection()
    {
        var inbox = new FakeInboxItems();
        var picked = inbox.Seed("Picked in the queue");
        inbox.Seed("Put aside", status: Backlog.Modules.Inbox.Abstractions.InboxStatus.Deferred);
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")));
        await state.ReloadAsync();

        state.TogglePicked(picked.Id, picked: true, range: false);
        Assert.Equal(1, state.SelectionCount);

        state.SelectSlice(InboxDesktopState.DeferredSliceId);

        Assert.Equal(0, state.SelectionCount);
        Assert.Equal(["Put aside"], state.VisibleItems.Select(item => item.Title));
    }

    // --- Tag options ----------------------------------------------------------

    /// <summary>The picker offers the words the backlog already uses beside the
    /// inbox's own, once each whichever side they came from, so a tag typed on
    /// an entry last week is a pick rather than a retype here.</summary>
    [Fact]
    public async Task Tag_options_are_the_backlogs_tags_and_the_inboxs_own_united()
    {
        var inbox = new FakeInboxItems();
        inbox.Seed("Tagged here", tags: ["sync", "Deploy"]);
        var backlog = new FakeBacklogTagSource(["infra", "deploy"]);
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")), backlogTags: backlog);

        await state.ReloadAsync();

        Assert.Equal(["Deploy", "infra", "sync"], state.TagOptions.Select(option => option.Value));
    }

    /// <summary>The backlog's tags travel with the reload, so a tag typed on an
    /// entry after the pane opened is offered the next time the pane refreshes
    /// rather than never.</summary>
    [Fact]
    public async Task The_backlogs_tags_are_read_again_on_every_reload()
    {
        var inbox = new FakeInboxItems();
        var backlog = new FakeBacklogTagSource(["infra"]);
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")), backlogTags: backlog);

        await state.ReloadAsync();
        Assert.Equal(["infra"], state.TagOptions.Select(option => option.Value));

        backlog.Tags = ["infra", "sync"];
        await state.ReloadAsync();
        Assert.Equal(["infra", "sync"], state.TagOptions.Select(option => option.Value));
    }

    [Fact]
    public async Task Without_a_backlog_tag_source_the_inboxs_own_tags_are_offered()
    {
        var inbox = new FakeInboxItems();
        inbox.Seed("Tagged here", tags: ["sync"]);
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")));

        await state.ReloadAsync();

        Assert.Equal(["sync"], state.TagOptions.Select(option => option.Value));
    }

    /// <summary>An item's relations are read once while it stays on screen and
    /// read again after any change, because any change may be the one that made
    /// or broke a relation — the suggestions' rule.</summary>
    [Fact]
    public async Task Relations_are_read_once_per_item_and_again_after_a_reload()
    {
        var inbox = new FakeInboxItems();
        var first = inbox.Seed("Read the design doc");
        var second = inbox.Seed("Buy milk");
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")));
        await state.ReloadAsync();

        state.SelectItem(first.Id);
        await state.LoadRelationsAsync();
        await state.LoadRelationsAsync();

        Assert.Equal(1, inbox.RelatedCalls);
        Assert.Equal(first.Id, state.Relations!.ItemId);
        Assert.NotNull(state.RelationsOf(state.SelectedItem!));

        state.SelectItem(second.Id);
        Assert.Null(state.RelationsOf(state.SelectedItem!));
        await state.LoadRelationsAsync();
        Assert.Equal(2, inbox.RelatedCalls);

        await state.ReloadAsync();
        await state.LoadRelationsAsync();
        Assert.Equal(3, inbox.RelatedCalls);
        Assert.Equal(second.Id, state.Relations!.ItemId);
    }

    /// <summary>An item already archived as a duplicate of the selected one —
    /// directly or down a chain — is not offered as what it duplicates: the
    /// module would refuse it as circular.</summary>
    [Fact]
    public async Task The_duplicate_picker_leaves_out_items_that_are_already_duplicates_of_the_selected_one()
    {
        var inbox = new FakeInboxItems();
        var kept = inbox.Seed("Read the post", capturedAt: inbox.Now.AddMinutes(-3));
        var direct = inbox.Seed("Read the post again", capturedAt: inbox.Now.AddMinutes(-2));
        var chained = inbox.Seed("Read the post, third time", capturedAt: inbox.Now.AddMinutes(-1));
        var other = inbox.Seed("Buy milk", capturedAt: inbox.Now);
        await inbox.ArchiveAsDuplicateAsync(direct.Id, kept.Id, TestContext.Current.CancellationToken);
        await inbox.ArchiveAsDuplicateAsync(chained.Id, direct.Id, TestContext.Current.CancellationToken);
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")));
        await state.ReloadAsync();

        state.SelectItem(kept.Id);

        Assert.Equal([other.Id], state.DuplicateCandidates(null).Select(item => item.Id));

        state.SelectItem(other.Id);

        Assert.Equal([chained.Id, direct.Id, kept.Id], state.DuplicateCandidates(null).Select(item => item.Id));
    }

    private sealed class FakeBacklogTagSource(IReadOnlyList<string> tags) : IBacklogTagSource
    {
        public IReadOnlyList<string> Tags { get; set; } = tags;

        public Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Tags);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
