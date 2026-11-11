using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.UI;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Feedback;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Someday (<c>s</c>) and Done now (<c>n</c>) decisions on the Inbox pane
/// (local ADR 0054; <c>.devbook/domain/inbox/features.md#per-item-triage-actions</c>):
/// on the decision bar, in triage mode and from the keyboard, each routing the
/// item once and undone like every other decision, listed in the Shortcuts
/// dialog, and proposed by the AI triage card.
/// </summary>
public sealed class InboxSomedayDoneNowTests
{
    [Fact]
    public async Task Someday_in_triage_files_the_item_in_the_someday_area_and_moves_on()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Learn the cello", "Next one");
        var pane = await harness.StartTriageAsync();

        var button = pane.Find("[data-testid='inbox-triage-decisions'] [data-testid='inbox-someday']");
        Assert.Contains("Someday", button.TextContent, StringComparison.Ordinal);
        await button.ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([items[0].Id], harness.Inbox.BrainRoutes);
            Assert.Equal(["someday"], harness.Inbox.BrainFolders);
            Assert.Equal(RoutingDomain.Devbook, harness.Inbox.Find(items[0].Id)!.Routing!.Domain);
            Assert.Equal(items[1].Id, harness.State.SelectedItemId);
            Assert.Contains("Learn the cello → Someday", pane.Find("[data-testid='inbox-triage-just-decided-line']").TextContent, StringComparison.Ordinal);
        });
        Assert.Contains(harness.Toasts.Visible, toast => toast.TestId == "inbox-brain-routed" && toast.Message.Contains("Someday area", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S_files_it_without_a_picker_and_u_takes_it_back()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Learn the cello");
        var pane = await harness.StartTriageAsync();

        await PressAsync(pane, "s");

        pane.WaitForAssertion(() => Assert.NotNull(harness.Inbox.Find(items[0].Id)!.Routing));
        Assert.Empty(pane.FindAll("[data-testid='inbox-brain-dialog']"));
        Assert.Equal(["someday"], harness.Inbox.BrainFolders);

        await PressAsync(pane, "u");

        pane.WaitForAssertion(() => Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing));
        // The note is the person's file now: the undo asks nothing to be deleted.
        Assert.Contains(("unlink", items[0].Id), harness.Inbox.Undos);
    }

    [Fact]
    public async Task Someday_is_disabled_with_the_brain_s_reason_and_s_does_nothing_while_no_brain_is_set()
    {
        using var harness = Harness.Create();
        const string Reason = "Pick a folder for your Brain in Settings › Storage.";
        harness.Inbox.BrainUnavailableReason = Reason;
        harness.SeedNewestFirst("Learn the cello");
        var pane = await harness.StartTriageAsync();

        var button = pane.Find("[data-testid='inbox-triage-decisions'] [data-testid='inbox-someday']");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal(Reason, button.GetAttribute("title"));

        await PressAsync(pane, "s");

        Assert.Empty(harness.Inbox.BrainRoutes);
    }

    /// <summary>A Brain that declares its own areas and no <c>someday</c> offers
    /// Someday disabled, saying so — in triage and on the item — and <c>s</c> does
    /// nothing.</summary>
    [Fact]
    public async Task A_brain_with_no_someday_area_offers_someday_disabled_with_the_reason()
    {
        using var harness = Harness.Create();
        harness.Inbox.BrainDestinations = [new("_inbox", "Inbox", IsInbox: true), new("work", "Work"), new("home", "Home")];
        harness.SeedNewestFirst("Learn the cello", "Still waiting");
        var pane = await harness.StartTriageAsync();

        var button = pane.Find("[data-testid='inbox-triage-decisions'] [data-testid='inbox-someday']");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal(InboxDesktopState.NoSomedayArea, button.GetAttribute("title"));

        await PressAsync(pane, "s");
        Assert.Empty(harness.Inbox.BrainRoutes);

        await PressAsync(pane, "Escape");
        var onItem = pane.Find("[data-testid='inbox-detail-decisions'] [data-testid='inbox-someday']");
        Assert.True(onItem.HasAttribute("disabled"));
        Assert.Equal(InboxDesktopState.NoSomedayArea, onItem.GetAttribute("title"));
    }

    /// <summary>The pass does not offer what the decision bar cannot take: with
    /// no someday area its Someday proposals are left for the reader, so Apply
    /// is never stopped by them.</summary>
    [Fact]
    public async Task With_no_someday_area_the_pass_leaves_its_someday_proposals_for_the_reader()
    {
        using var harness = Harness.Create();
        harness.Inbox.BrainDestinations = [new("_inbox", "Inbox", IsInbox: true), new("work", "Work")];
        var someday = harness.Inbox.Seed("Learn the cello");
        var doneNow = harness.Inbox.Seed("Reply to Sam");
        harness.Inbox.TriageAdvisorAvailable = true;
        harness.Inbox.TriagePassAnswer = Result.Success(InboxTriagePassDto.Nothing with
        {
            Outcomes =
            [
                new InboxTriageOutcomeProposalDto(someday.Id, InboxClarifyOutcome.Someday, "A possibility.", 0.9),
                new InboxTriageOutcomeProposalDto(doneNow.Id, InboxClarifyOutcome.DoneNow, "Two minutes.", 0.9),
            ],
        });
        var pane = await harness.StartTriageAsync();

        await pane.InvokeAsync(harness.State.ProposeTriagePassAsync);

        var draft = harness.State.TriagePassDraft!;
        Assert.Equal([doneNow.Id], draft.Outcomes.Select(outcome => outcome.ItemId));
        Assert.Contains(someday.Id, draft.Unplaced);
    }

    [Fact]
    public async Task A_brain_that_refuses_the_note_says_so_in_its_own_words_and_the_item_stays()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Learn the cello", "Next one");
        harness.Inbox.FailAct[("brain", items[0].Id)] = InboxErrors.BrainUnavailable("Your Brain has no someday area.");
        var pane = await harness.StartTriageAsync();

        await PressAsync(pane, "s");

        pane.WaitForAssertion(() => Assert.Contains(
            harness.Toasts.Visible,
            toast => toast.TestId == "inbox-brain-error" && toast.Message == "Your Brain has no someday area."));
        Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);
    }

    [Fact]
    public async Task Done_now_in_triage_routes_the_item_to_one_task_and_moves_on()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Reply to Sam", "Next one");
        var routed = new List<InboxRoutedDto>();
        harness.State.Routed += routed.Add;
        var pane = await harness.StartTriageAsync();

        var button = pane.Find("[data-testid='inbox-triage-decisions'] [data-testid='inbox-done-now']");
        Assert.Contains("Done now", button.TextContent, StringComparison.Ordinal);
        await button.ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([items[0].Id], harness.Inbox.DoneNowRoutes);
            Assert.Equal(RoutingDomain.Tasks, harness.Inbox.Find(items[0].Id)!.Routing!.Domain);
            Assert.Equal(items[1].Id, harness.State.SelectedItemId);
            Assert.Contains("Reply to Sam → Done", pane.Find("[data-testid='inbox-triage-just-decided-line']").TextContent, StringComparison.Ordinal);
        });

        // Not the plain route: Move to backlog was never asked.
        Assert.Equal(0, harness.Inbox.SingleRouteCalls);
        Assert.Single(routed);
        Assert.Contains(harness.Toasts.Visible, toast => toast.TestId == InboxDesktopState.DoneNowToastTestId);
    }

    [Fact]
    public async Task N_routes_done_now_and_u_deletes_its_ticked_task()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Reply to Sam");
        var pane = await harness.StartTriageAsync();

        await PressAsync(pane, "n");
        pane.WaitForAssertion(() => Assert.NotNull(harness.Inbox.Find(items[0].Id)!.Routing));

        await PressAsync(pane, "u");

        pane.WaitForAssertion(() =>
        {
            Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
            Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(items[0].Id)!.Status);
        });
        Assert.Contains(("return-done", items[0].Id), harness.Inbox.Undos);
    }

    [Fact]
    public async Task Done_now_refused_by_tasks_leaves_the_item_open()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Reply to Sam", "Next one");
        harness.Inbox.FailAct[("done-now", items[0].Id)] = InboxErrors.ItemNotFound;
        var pane = await harness.StartTriageAsync();

        await PressAsync(pane, "n");

        pane.WaitForAssertion(() => Assert.Equal([items[0].Id], harness.Inbox.DoneNowRoutes));
        Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);
    }

    [Fact]
    public async Task The_item_detail_offers_someday_and_done_now_on_its_decision_bar()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Reply to Sam", "Learn the cello", "Still waiting");
        var pane = harness.Render();
        await pane.InvokeAsync(harness.State.InitializeAsync);

        await pane.Find($"[data-testid='inbox-item-{items[0].Id:D}']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-detail-decisions'] [data-testid='inbox-done-now']").ClickAsync(new());
        pane.WaitForAssertion(() => Assert.Equal([items[0].Id], harness.Inbox.DoneNowRoutes));

        await pane.Find($"[data-testid='inbox-item-{items[1].Id:D}']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-detail-decisions'] [data-testid='inbox-someday']").ClickAsync(new());
        pane.WaitForAssertion(() => Assert.Equal(["someday"], harness.Inbox.BrainFolders));
    }

    [Fact]
    public async Task A_decided_item_is_not_routed_again_by_s_or_n()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Reply to Sam", "Still waiting");
        var pane = harness.Render();
        await pane.InvokeAsync(harness.State.InitializeAsync);
        await pane.Find($"[data-testid='inbox-item-{items[0].Id:D}']").ClickAsync(new());
        await PressAsync(pane, "n");
        pane.WaitForAssertion(() => Assert.NotNull(harness.Inbox.Find(items[0].Id)!.Routing));

        await PressAsync(pane, "n");
        await PressAsync(pane, "s");

        Assert.Single(harness.Inbox.DoneNowRoutes);
        Assert.Empty(harness.Inbox.BrainRoutes);
    }

    [Fact]
    public void The_shortcuts_list_names_s_and_n()
    {
        Assert.Contains(InboxPane.Shortcuts, shortcut => shortcut.Keys == "s" && shortcut.Action.StartsWith("Someday", StringComparison.Ordinal));
        Assert.Contains(InboxPane.Shortcuts, shortcut => shortcut.Keys == "n" && shortcut.Action.StartsWith("Done now", StringComparison.Ordinal));
    }

    [Fact]
    public void Components_js_hands_s_and_n_to_the_pane()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"));
        var keys = script[script.IndexOf("const SHORTCUT_PANE_KEYS", StringComparison.Ordinal)..];
        keys = keys[..keys.IndexOf('\n', StringComparison.Ordinal)];

        Assert.Contains("'s'", keys, StringComparison.Ordinal);
        Assert.Contains("'n'", keys, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(InboxClarifyOutcome.Someday, "Someday, not now")]
    [InlineData(InboxClarifyOutcome.DoneNow, "Done in two minutes")]
    public async Task The_ai_outcome_card_is_numbered_and_its_number_takes_it(InboxClarifyOutcome outcome, string title)
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Learn the cello", "Next one");
        harness.Inbox.TriageAdvisorAvailable = true;
        harness.Inbox.TriageAdvice[items[0].Id] = Result.Success(InboxTriageAdviceDto.None(items[0].Id) with
        {
            Outcome = new InboxTriageOutcomeDto(outcome, "It reads that way."),
        });
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.Contains(title, pane.Find("[data-testid='inbox-triage-card-outcome']").TextContent, StringComparison.Ordinal));
        Assert.Equal([InboxTriageCardKind.Outcome], harness.State.TriageCards);

        await pane.InvokeAsync(() => harness.State.TakeTriageCardAsync(0));

        pane.WaitForAssertion(() => Assert.NotNull(harness.Inbox.Find(items[0].Id)!.Routing));
        if (outcome == InboxClarifyOutcome.Someday) Assert.Equal(["someday"], harness.Inbox.BrainFolders);
        else Assert.Equal([items[0].Id], harness.Inbox.DoneNowRoutes);
    }

    [Fact]
    public async Task Dismissing_the_outcome_card_hides_it_for_the_session()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Learn the cello", "Next one");
        harness.Inbox.TriageAdvisorAvailable = true;
        harness.Inbox.TriageAdvice[items[0].Id] = Result.Success(InboxTriageAdviceDto.None(items[0].Id) with
        {
            Outcome = new InboxTriageOutcomeDto(InboxClarifyOutcome.Someday, "A possibility."),
        });
        var pane = await harness.StartTriageAsync();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='inbox-triage-card-outcome']")));

        await pane.Find("[data-testid='inbox-triage-dismiss-outcome']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='inbox-triage-card-outcome']")));
        Assert.Empty(harness.State.TriageCards);
        Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
    }

    private static Task PressAsync(IRenderedComponent<InboxPane> pane, string key) =>
        pane.InvokeAsync(() => pane.Instance.OnShortcutAsync(key));

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
                Path.Combine(Path.GetTempPath(), "backlog-inbox-someday-tests", Guid.NewGuid().ToString("n"), "github.json")));
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
