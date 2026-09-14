using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;

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
