using Backlog.Modules.Inbox.UI;
using Backlog.UI.Components.Feedback;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// To Brain on the Inbox pane (<c>.devbook/domain/inbox/features.md#route-to-brain</c>):
/// the decision beside Move to backlog, in triage and on the item, its key, the
/// destination chip that now takes it, its undo, and the words a Brain that is not
/// set up answers with.
/// </summary>
public sealed class InboxToBrainTests
{
    [Fact]
    public async Task To_brain_in_triage_routes_the_item_says_where_it_went_and_moves_on()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Standup", "Next one");
        var pane = await harness.StartTriageAsync();

        var button = pane.Find("[data-testid='inbox-triage-decisions'] [data-testid='inbox-to-brain']");
        Assert.Contains("To Brain", button.TextContent, StringComparison.Ordinal);
        await button.ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([items[0].Id], harness.Inbox.BrainRoutes);
            Assert.Equal(RoutingDomain.Devbook, harness.Inbox.Find(items[0].Id)!.Routing!.Domain);
            Assert.Equal(items[1].Id, harness.State.SelectedItemId);
            Assert.Contains("Standup → Brain", pane.Find("[data-testid='inbox-triage-just-decided-line']").TextContent, StringComparison.Ordinal);
        });
        Assert.Contains(harness.Toasts.Visible, toast => toast.TestId == "inbox-brain-routed" && toast.Message.Contains("Standup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task B_routes_to_the_brain_and_u_takes_it_back()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Keep this");
        var pane = await harness.StartTriageAsync();

        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("b"));
        pane.WaitForAssertion(() => Assert.NotNull(harness.Inbox.Find(items[0].Id)!.Routing));

        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("u"));

        pane.WaitForAssertion(() =>
        {
            Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
            Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(items[0].Id)!.Status);
        });

        // The note is the person's file now: the undo asks nothing to be deleted.
        Assert.Contains(("unlink", items[0].Id), harness.Inbox.Undos);
    }

    /// <summary>The route's third requirement, on screen: the Brain's words, and
    /// the item still waiting.</summary>
    [Fact]
    public async Task A_brain_that_is_not_set_up_is_said_and_the_item_stays()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Keep this", "Next one");
        harness.Inbox.FailAct[("brain", items[0].Id)] = InboxErrors.BrainUnavailable("Pick a folder for your Brain in Settings › Storage.");
        var pane = await harness.StartTriageAsync();

        await pane.Find("[data-testid='inbox-to-brain']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Contains(
            harness.Toasts.Visible,
            toast => toast.TestId == "inbox-brain-error" && toast.Message == "Pick a folder for your Brain in Settings › Storage."));
        Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);
    }

    [Fact]
    public async Task The_item_detail_offers_to_brain_too_and_says_where_the_item_went()
    {
        using var harness = Harness.Create();
        // A second item still waiting keeps the inbox-zero summary from taking
        // the detail away.
        var items = harness.SeedNewestFirst("Article", "Still waiting");
        var pane = harness.Render();
        await pane.InvokeAsync(harness.State.InitializeAsync);
        await pane.Find($"[data-testid='inbox-item-{items[0].Id:D}']").ClickAsync(new());

        await pane.Find("[data-testid='inbox-detail-decisions'] [data-testid='inbox-to-brain']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.StartsWith(
            "Kept in your Brain on",
            pane.Find("[data-testid='inbox-detail-state']").TextContent.Trim(),
            StringComparison.Ordinal));
    }

    /// <summary>The destination chip for collected material was shown and could
    /// not be taken; it is the To Brain decision now.</summary>
    [Fact]
    public async Task Taking_the_brain_destination_chip_routes_to_the_brain()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Local-first patterns");
        var pane = await harness.StartTriageAsync();

        await pane.InvokeAsync(() => harness.State.AcceptSuggestionAsync(
            new InboxSuggestionDto("destination:devbook", InboxSuggestionKind.Destination, "devbook", "It is collected material.")));

        pane.WaitForAssertion(() => Assert.Equal([items[0].Id], harness.Inbox.BrainRoutes));
    }

    /// <summary>
    /// While the Brain cannot take a note, To Brain is shown disabled with the
    /// Brain's own reason — in triage and on the item — rather than offered and then
    /// refused; once it can, it is the act again.
    /// </summary>
    [Fact]
    public async Task To_brain_is_disabled_with_the_brain_s_reason_while_no_brain_is_set()
    {
        using var harness = Harness.Create();
        const string Reason = "Pick a folder for your Brain in Settings › Storage.";
        harness.Inbox.BrainUnavailableReason = Reason;
        harness.SeedNewestFirst("Keep this", "Still waiting");
        var pane = await harness.StartTriageAsync();

        var inTriage = pane.Find("[data-testid='inbox-triage-decisions'] [data-testid='inbox-to-brain']");
        Assert.True(inTriage.HasAttribute("disabled"));
        Assert.Equal(Reason, inTriage.GetAttribute("title"));

        harness.Inbox.BrainUnavailableReason = null;
        pane.Render();

        var enabled = pane.Find("[data-testid='inbox-triage-decisions'] [data-testid='inbox-to-brain']");
        Assert.False(enabled.HasAttribute("disabled"));
        Assert.Equal(InboxDesktopState.ToBrainTitle, enabled.GetAttribute("title"));
        Assert.Empty(harness.Inbox.BrainRoutes);
    }

    [Fact]
    public async Task The_item_detail_disables_to_brain_with_the_brain_s_reason_too()
    {
        using var harness = Harness.Create();
        const string Reason = "The Brain folder was not found at D:\\Brain.";
        harness.Inbox.BrainUnavailableReason = Reason;
        var items = harness.SeedNewestFirst("Article", "Still waiting");
        var pane = harness.Render();
        await pane.InvokeAsync(harness.State.InitializeAsync);
        await pane.Find($"[data-testid='inbox-item-{items[0].Id:D}']").ClickAsync(new());

        var button = pane.Find("[data-testid='inbox-detail-decisions'] [data-testid='inbox-to-brain']");

        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal(Reason, button.GetAttribute("title"));
    }

    [Fact]
    public void The_shortcuts_list_names_b()
    {
        Assert.Contains(("b", "To Brain"), InboxPane.Shortcuts);
    }

    private sealed class Harness : IDisposable
    {
        private Harness(BunitContext context, FakeInboxItems inbox)
        {
            Context = context;
            Inbox = inbox;
        }

        public BunitContext Context { get; }

        public FakeInboxItems Inbox { get; }

        public InboxDesktopState State => Context.Services.GetRequiredService<InboxDesktopState>();

        public ToastChannel Toasts => Context.Services.GetRequiredService<ToastChannel>();

        public static Harness Create()
        {
            var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddSingleton(new Backlog.Infrastructure.GitHub.GitHubSettingsStore(
                Path.Combine(Path.GetTempPath(), "backlog-inbox-to-brain-tests", Guid.NewGuid().ToString("n"), "github.json")));
            TasksTestHost.AddToastChannel(context.Services);
            var inbox = InboxTestHost.AddInboxState(context.Services);

            return new Harness(context, inbox);
        }

        public IReadOnlyList<InboxItemDto> SeedNewestFirst(params string[] titles) =>
            [.. titles.Select((title, index) => Inbox.Seed(title, capturedAt: Inbox.Now.AddMinutes(titles.Length - index)))];

        public IRenderedComponent<InboxPane> Render() => Context.Render<InboxPane>();

        public async Task<IRenderedComponent<InboxPane>> StartTriageAsync()
        {
            var pane = Render();
            await pane.InvokeAsync(State.InitializeAsync);
            await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("t"));
            Assert.True(State.TriageMode);
            return pane;
        }

        public void Dispose() => Context.Dispose();
    }
}
