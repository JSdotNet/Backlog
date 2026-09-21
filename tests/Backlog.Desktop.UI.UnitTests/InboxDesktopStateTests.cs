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
