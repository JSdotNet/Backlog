using System.Text.RegularExpressions;
using Backlog.Desktop.UI.Inbox;
using Backlog.Desktop.WebHarness;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Feedback;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The AI triage pass under review (features.md#ai-triage-pass, artboard D;
/// local ADR 0023 §4–5): "Let AI propose the rest" only while the advisor can
/// run; the pass's four groups with their reasons; low confidence starting
/// unaccepted; Discard changing nothing; Apply running the accepted decisions
/// in order, one undo step each, and stopping at the first refusal with a
/// sentence naming the item; and "Triage them" opening triage on what is left.
/// Driven by the harness's own fake advisor over seeded items, so the screen is
/// drawn from a pass the advisor really made.
/// </summary>
public sealed class InboxTriagePassTests
{
    // --- The button -------------------------------------------------------------

    [Fact]
    public async Task Let_AI_propose_the_rest_is_drawn_only_while_the_advisor_can_run()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("One");
        var pane = await harness.StartTriageAsync();

        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-propose']"));

        harness.Inbox.TriageAdvisorAvailable = true;
        await pane.InvokeAsync(harness.State.ReloadAsync);

        pane.WaitForAssertion(() =>
            Assert.Equal("Let AI propose the rest", Squash(pane.Find("[data-testid='inbox-triage-propose']").TextContent)));
    }

    [Fact]
    public async Task While_the_pass_is_read_the_button_says_how_many_items_and_waits()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("One");
        harness.Inbox.Seed("Two");
        harness.Inbox.TriageAdvisorAvailable = true;
        var hold = new TaskCompletionSource();
        harness.Inbox.BeforeTriagePass = () => hold.Task;
        var pane = await harness.StartTriageAsync();

        var asking = pane.Find("[data-testid='inbox-triage-propose']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            var button = pane.Find("[data-testid='inbox-triage-propose']");
            Assert.Equal("Reading 2 items…", Squash(button.TextContent));
            Assert.True(button.HasAttribute("disabled"));
        });

        hold.SetResult();
        await asking;
    }

    [Fact]
    public async Task A_failed_pass_is_one_sentence_under_the_header_that_can_be_put_away()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("One");
        harness.Inbox.TriageAdvisorAvailable = true;
        harness.Inbox.TriagePassAnswer = Result.Failure<InboxTriagePassDto>(InboxErrors.TriageFailed("Azure Foundry returned 503: busy"));
        var pane = await harness.StartTriageAsync();

        await pane.Find("[data-testid='inbox-triage-propose']").ClickAsync(new());

        pane.WaitForAssertion(() =>
            Assert.StartsWith("Azure Foundry returned 503: busy", pane.Find("[data-testid='inbox-triage-pass-error']").TextContent.Trim(), StringComparison.Ordinal));
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass']"));

        await pane.Find("[data-testid='inbox-triage-pass-error-dismiss']").ClickAsync(new());

        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-pass-error']"));
    }

    // --- The review -------------------------------------------------------------

    [Fact]
    public async Task Pressing_it_opens_the_review_with_the_four_groups_and_their_reasons()
    {
        using var harness = Harness.Create();
        var scene = await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();

        await ProposeAsync(pane);

        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-view']"));
        var pass = pane.Find("[data-testid='inbox-pass']");
        Assert.Equal("Read the 8 items still waiting, against your backlog. Nothing moves until you apply.", Squash(pane.Find("[data-testid='inbox-pass-subtitle']").TextContent));

        // Plans to make: title, size and tag preview, members, repositories, why.
        Assert.Equal("Plans to make · 1 · items that belong together go to the backlog as one plan", Squash(pane.Find("#inbox-pass-plans-title").TextContent));
        var plan = pane.Find("[data-testid='inbox-pass-plan']");
        Assert.Equal("Plan: sync", plan.QuerySelector("[data-testid='inbox-pass-plan-title']")!.TextContent.Trim());
        Assert.Equal($"2 items · plan tag {InboxPlanTag.Preview("Plan: sync")}", Squash(plan.QuerySelector("[data-testid='inbox-pass-plan-meta']")!.TextContent));
        Assert.Equal(
            ["Sync conflict banner", "Sync retry backoff"],
            plan.QuerySelectorAll("[data-testid='inbox-pass-plan-members'] li").Select(li => li.TextContent.Trim()));
        var chip = Assert.Single(plan.QuerySelectorAll("[data-testid='inbox-pass-repo']"));
        Assert.Equal("web", chip.TextContent.Trim());
        Assert.Contains("repo-mark--", chip.ClassName, StringComparison.Ordinal);
        Assert.Equal("Why: These captures are all tagged sync.", Squash(plan.QuerySelector(".inbox-pass__why")!.TextContent).TrimStart('↳').Trim());

        // Likely duplicates: both sides, the choices as radios, the reason.
        var duplicate = pane.Find("[data-testid='inbox-pass-duplicate']");
        Assert.StartsWith("Inbox · ", Squash(duplicate.QuerySelector("[data-testid='inbox-pass-duplicate-item'] .inbox-pass__side-label")!.TextContent), StringComparison.Ordinal);
        Assert.Equal(scene.Duplicate.Title, duplicate.QuerySelector("[data-testid='inbox-pass-duplicate-item'] .inbox-pass__side-title")!.TextContent.Trim());
        Assert.Equal("Backlog entry · in progress", duplicate.QuerySelector("[data-testid='inbox-pass-duplicate-target'] .inbox-pass__side-label")!.TextContent.Trim());
        Assert.Equal(scene.Task.Title, duplicate.QuerySelector("[data-testid='inbox-pass-duplicate-target'] .inbox-pass__side-title")!.TextContent.Trim());
        Assert.Equal("radiogroup", duplicate.QuerySelector(".inbox-pass__choices")!.GetAttribute("role"));
        var radios = duplicate.QuerySelectorAll("[role='radio']");
        Assert.Equal(["Merge into the entry", "Keep both", "Archive the capture"], radios.Select(radio => Squash(radio.TextContent)));
        Assert.Equal(["true", "false", "false"], radios.Select(radio => radio.GetAttribute("aria-checked")));
        Assert.Contains("It asks for the same work as the task", duplicate.TextContent, StringComparison.Ordinal);

        // Move to backlog on their own: a table of item and reason, type, repositories.
        Assert.Equal("table", pane.Find(".inbox-pass__table").GetAttribute("role"));
        var routes = pane.FindAll("[data-testid='inbox-pass-route']");
        Assert.Equal(2, routes.Count);
        Assert.Contains("Fix login redirect", routes[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("It names a repository", routes[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("Prompt", routes[0].QuerySelector("[data-testid='inbox-pass-route-type']")!.TextContent.Trim());
        Assert.Equal("web", routes[0].QuerySelector("[data-testid='inbox-pass-repo']")!.TextContent.Trim());
        Assert.Equal("Task", routes[1].QuerySelector("[data-testid='inbox-pass-route-type']")!.TextContent.Trim());

        // The rest: one row per list, one for the archives, what is left.
        Assert.Equal("The rest · 2", Squash(pane.Find("#inbox-pass-rest-title").TextContent));
        Assert.StartsWith("File 1 in Reading", Squash(pane.Find("[data-testid='inbox-pass-filing']").TextContent), StringComparison.Ordinal);
        Assert.StartsWith("Archive 1 — A bare title of two words is too little to act on.", Squash(pane.Find("[data-testid='inbox-pass-archive']").TextContent), StringComparison.Ordinal);
        Assert.StartsWith("Left for you: 1 item AI could not place with confidence", Squash(pane.Find("[data-testid='inbox-pass-left']").TextContent), StringComparison.Ordinal);

        // Show opens the row's items with their reasons.
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass-filing-item']"));
        await pane.Find("[data-testid='inbox-pass-filing-show']").ClickAsync(new());
        var filed = pane.Find("[data-testid='inbox-pass-filing-item']");
        Assert.Contains(".NET 11 performance", filed.TextContent, StringComparison.Ordinal);
        Assert.Contains("It is tagged Reading", filed.TextContent, StringComparison.Ordinal);

        Assert.NotNull(pass);
    }

    [Fact]
    public async Task A_group_with_nothing_in_it_is_not_drawn()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Lunch");
        harness.Inbox.TriageAdvisorAvailable = true;
        harness.Inbox.TriagePassAnswer = Result.Success(InboxTriagePassDto.Nothing with
        {
            Archives = [new InboxTriageArchiveDto(item.Id, "Nothing to act on.", 0.9)],
        });
        var pane = await harness.StartTriageAsync();

        await ProposeAsync(pane);

        Assert.Empty(pane.FindAll("[data-testid='inbox-pass-plans']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass-duplicates']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass-routes']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass-left']"));
        Assert.NotNull(pane.Find("[data-testid='inbox-pass-archive']"));
    }

    [Fact]
    public async Task Low_confidence_starts_unaccepted_and_toggling_changes_the_count_and_Apply()
    {
        using var harness = Harness.Create();
        await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();

        await ProposeAsync(pane);

        Assert.Equal("5 of 6 accepted", Count(pane));
        Assert.Equal("Apply 5 decisions", ApplyLabel(pane));

        var low = pane.FindAll("[data-testid='inbox-pass-route-accept']")[0];
        Assert.Equal("false", low.GetAttribute("aria-pressed"));
        Assert.Equal("Accept", Squash(low.TextContent));
        var high = pane.FindAll("[data-testid='inbox-pass-route-accept']")[1];
        Assert.Equal("true", high.GetAttribute("aria-pressed"));
        Assert.Equal("Accepted", Squash(high.TextContent));

        await low.ClickAsync(new());
        Assert.Equal("6 of 6 accepted", Count(pane));
        Assert.Equal("Apply 6 decisions", ApplyLabel(pane));

        await pane.Find("[data-testid='inbox-pass-plan-accept']").ClickAsync(new());
        Assert.Equal("Apply 5 decisions", ApplyLabel(pane));

        // Keep both is no decision at all.
        await pane.Find("[data-testid='inbox-pass-duplicate-keep-both']").ClickAsync(new());
        Assert.Equal("4 of 6 accepted", Count(pane));
        Assert.Equal("true", pane.Find("[data-testid='inbox-pass-duplicate-keep-both']").GetAttribute("aria-checked"));
    }

    [Fact]
    public async Task A_duplicate_below_the_threshold_starts_on_keep_both_and_an_item_target_offers_keep_the_other()
    {
        using var harness = Harness.Create();
        var kept = harness.Inbox.Seed("Aspire 13 dashboard walkthrough");
        var item = harness.Inbox.Seed("What is new in the Aspire 13 dashboard");
        harness.Inbox.TriageAdvisorAvailable = true;
        harness.Inbox.TriagePassAnswer = Result.Success(InboxTriagePassDto.Nothing with
        {
            Duplicates = [new InboxTriageDuplicatePairDto(item.Id, InboxTriageTargetKind.InboxItem, kept.Id, InboxDuplicateAction.KeepOneAttachOther, "Same talk.", 0.5)],
        });
        var pane = await harness.StartTriageAsync();

        await ProposeAsync(pane);

        var radios = pane.FindAll("[data-testid='inbox-pass-duplicate'] [role='radio']");
        Assert.Equal(["Keep Aspire 13 dashboard walkthrough, archive this one", "Keep both"], radios.Select(radio => Squash(radio.TextContent)));
        Assert.Equal("true", radios[1].GetAttribute("aria-checked"));
        Assert.Equal("0 of 1 accepted", Count(pane));
        Assert.True(pane.Find("[data-testid='inbox-pass-apply']").HasAttribute("disabled"));

        await radios[0].ClickAsync(new());
        await pane.Find("[data-testid='inbox-pass-apply']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(InboxStatus.Archived, harness.Inbox.Find(item.Id)!.Status);
            Assert.Equal(kept.Id, harness.Inbox.Find(item.Id)!.DuplicateOf);
        });
        Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(kept.Id)!.Status);
    }

    [Fact]
    public async Task Editing_a_plan_renames_it_and_leaves_members_out_and_a_plan_of_one_is_not_applied()
    {
        using var harness = Harness.Create();
        var scene = await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);

        await pane.Find("[data-testid='inbox-pass-plan-edit']").ClickAsync(new());
        Assert.Equal("true", pane.Find("[data-testid='inbox-pass-plan-edit']").GetAttribute("aria-expanded"));

        await pane.Find("[data-testid='inbox-pass-plan-title-field'] input").InputAsync(new() { Value = "Conflict work" });
        Assert.Equal($"2 items · plan tag {InboxPlanTag.Preview("Conflict work")}", Squash(pane.Find("[data-testid='inbox-pass-plan-meta']").TextContent));

        await pane.FindAll("[data-testid='inbox-pass-plan-leave-out']")[1].ClickAsync(new());

        Assert.Equal("1 item · plan tag +conflict-work-…", Squash(pane.Find("[data-testid='inbox-pass-plan-meta']").TextContent));
        Assert.NotNull(pane.Find("[data-testid='inbox-pass-plan-too-small']"));
        Assert.True(pane.Find("[data-testid='inbox-pass-plan-accept']").HasAttribute("disabled"));
        Assert.Equal("4 of 6 accepted", Count(pane));

        // Back in, the plan goes under the new title.
        await pane.FindAll("[data-testid='inbox-pass-plan-leave-out']")[1].ClickAsync(new());
        await pane.Find("[data-testid='inbox-pass-apply']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='inbox-pass']")));
        var batch = Assert.Single(harness.Inbox.BatchRoutes);
        Assert.StartsWith("+conflict-work-", batch.PlanTag, StringComparison.Ordinal);
        Assert.Equal([scene.PlanA.Id, scene.PlanB.Id], batch.Ids);
    }

    // --- Discard, Back, Escape ---------------------------------------------------

    [Theory]
    [InlineData("inbox-pass-discard")]
    [InlineData("inbox-pass-back")]
    public async Task Discard_and_Back_put_the_pass_away_change_nothing_and_return_to_triage(string testId)
    {
        using var harness = Harness.Create();
        await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);
        var before = harness.Inbox.Items.ToList();

        await pane.Find($"[data-testid='{testId}']").ClickAsync(new());

        Assert.Empty(pane.FindAll("[data-testid='inbox-pass']"));
        Assert.NotNull(pane.Find("[data-testid='inbox-triage-view']"));
        Assert.True(harness.State.TriageMode);
        Assert.Null(harness.State.TriagePassDraft);
        Assert.Equal(before, harness.Inbox.Items);
        Assert.Empty(harness.Inbox.BatchRoutes);
        Assert.Empty(harness.Inbox.Merges);
        Assert.Equal(0, harness.Inbox.SingleRouteCalls);
        Assert.False(harness.State.CanUndo);
    }

    [Fact]
    public async Task While_the_review_is_shown_the_decision_keys_do_nothing_and_Escape_returns_to_triage()
    {
        using var harness = Harness.Create();
        await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);
        var before = harness.Inbox.Items.ToList();

        foreach (var key in new[] { "a", "r", "d", "l", "g", "x" }) await PressAsync(pane, key);

        Assert.Equal(before, harness.Inbox.Items);
        Assert.NotNull(pane.Find("[data-testid='inbox-pass']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-defer-dialog']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-move-list-dialog']"));
        Assert.False(harness.State.SelectionMode);

        await PressAsync(pane, "Escape");

        Assert.True(harness.State.TriageMode);
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass']"));
        Assert.NotNull(pane.Find("[data-testid='inbox-triage-view']"));
    }

    // --- Apply ------------------------------------------------------------------

    [Fact]
    public async Task Apply_runs_the_accepted_decisions_and_pushes_one_undo_step_each()
    {
        using var harness = Harness.Create();
        var scene = await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);

        await pane.Find("[data-testid='inbox-pass-apply']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='inbox-pass']")));

        // The plan: one batch under a tag built from its title, to its repositories.
        var batch = Assert.Single(harness.Inbox.BatchRoutes);
        Assert.Equal([scene.PlanA.Id, scene.PlanB.Id], batch.Ids);
        Assert.Matches(new Regex(@"^\+plan-sync-[0-9a-f]{8}$"), batch.PlanTag);
        Assert.Equal(["acme/web"], batch.Choices!.Repositories![scene.PlanB.Id]);
        Assert.Equal(InboxStatus.Triaged, Status(harness, scene.PlanA));
        Assert.Equal(InboxStatus.Triaged, Status(harness, scene.PlanB));

        // The duplicate, merged into its task.
        Assert.Equal((scene.Duplicate.Id, scene.Task.Id), Assert.Single(harness.Inbox.Merges));

        // The accepted route went; the low-confidence one did not.
        Assert.Equal(1, harness.Inbox.SingleRouteCalls);
        Assert.Equal(InboxStatus.Triaged, Status(harness, scene.RouteTask));
        Assert.Equal(InboxStatus.Unprocessed, Status(harness, scene.RouteLow));

        // Filed, archived, and the one left for the reader untouched.
        Assert.Equal(scene.Reading.Id, harness.Inbox.Find(scene.Filed.Id)!.ListId);
        Assert.Equal(InboxStatus.Archived, Status(harness, scene.Archived));
        Assert.Equal(InboxStatus.Unprocessed, Status(harness, scene.Left));

        var toast = Assert.Single(harness.Toasts.Visible, toast => toast.TestId == InboxDesktopState.TriagePassResultTestId);
        Assert.Equal("Applied 5 decisions. 1 left for you.", toast.Message);

        Assert.Equal(3, harness.State.DecisionCount(InboxDecisionKind.MoveToBacklog));
        Assert.Equal(1, harness.State.DecisionCount(InboxDecisionKind.MergeIntoTask));
        Assert.Equal(1, harness.State.DecisionCount(InboxDecisionKind.MoveToList));
        Assert.Equal(1, harness.State.DecisionCount(InboxDecisionKind.Archive));

        // Triage carries on from an item still waiting.
        Assert.True(harness.State.TriageMode);
        Assert.Equal(InboxStatus.Unprocessed, harness.State.SelectedItem!.Status);

        // U takes the last decision back, then the one before it.
        await PressAsync(pane, "u");
        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Unprocessed, Status(harness, scene.Archived)));
        Assert.Equal(scene.Reading.Id, harness.Inbox.Find(scene.Filed.Id)!.ListId);

        await PressAsync(pane, "u");
        pane.WaitForAssertion(() => Assert.Null(harness.Inbox.Find(scene.Filed.Id)!.ListId));
        Assert.Equal(InboxStatus.Triaged, Status(harness, scene.RouteTask));
    }

    [Fact]
    public async Task A_refusal_stops_the_rest_names_the_item_and_keeps_what_applied()
    {
        using var harness = Harness.Create();
        var scene = await harness.SceneAsync();
        harness.Inbox.FailAct[("merge", scene.Duplicate.Id)] = Error.Validation("comment.locked", "The entry is locked");
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);

        await pane.Find("[data-testid='inbox-pass-apply']").ClickAsync(new());

        var sentence = $"Couldn't merge \"{scene.Duplicate.Title}\" into the backlog entry: The entry is locked. The decisions before it stay applied; the rest were not tried.";
        pane.WaitForAssertion(() => Assert.Equal(sentence, pane.Find("[data-testid='inbox-pass-error']").TextContent.Trim()));
        Assert.Contains(harness.Toasts.Visible, toast => toast.Message == sentence && toast.Severity == ToastSeverity.Warning);

        // The plan before it landed and is undoable; nothing after it was tried.
        Assert.Equal(InboxStatus.Triaged, Status(harness, scene.PlanA));
        Assert.Equal(InboxStatus.Unprocessed, Status(harness, scene.Duplicate));
        Assert.Equal(0, harness.Inbox.SingleRouteCalls);
        Assert.Null(harness.Inbox.Find(scene.Filed.Id)!.ListId);
        Assert.Equal(InboxStatus.Unprocessed, Status(harness, scene.Archived));
        Assert.Equal(new InboxLatestDecision("2 items", "Move to backlog"), harness.State.LatestDecision);

        // The review stays open on what was not applied.
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass-plans']"));
        Assert.NotNull(pane.Find("[data-testid='inbox-pass-duplicate']"));
        Assert.Equal("4 of 5 accepted", Count(pane));
        Assert.Equal("Apply 4 decisions", ApplyLabel(pane));
    }

    [Fact]
    public async Task Leaving_triage_puts_the_review_away_too()
    {
        using var harness = Harness.Create();
        await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);

        await pane.InvokeAsync(() => harness.State.SetTriageMode(false));

        Assert.Null(harness.State.TriagePassDraft);
        Assert.Null(harness.State.TriagePass);
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass']"));
    }

    [Fact]
    public async Task Escape_Discard_and_Triage_them_during_an_Apply_leave_the_review_to_the_Apply_and_its_result_is_shown()
    {
        using var harness = Harness.Create();
        await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);
        var hold = new TaskCompletionSource();
        harness.Inbox.BeforeRoute = () => hold.Task;

        var applying = pane.Find("[data-testid='inbox-pass-apply']").ClickAsync(new());
        pane.WaitForAssertion(() => Assert.True(harness.State.BulkRunning));

        await PressAsync(pane, "Escape");
        await pane.InvokeAsync(harness.State.DismissTriagePass);
        await pane.InvokeAsync(harness.State.TriageLeftOver);

        Assert.NotNull(harness.State.TriagePassDraft);
        Assert.NotNull(pane.Find("[data-testid='inbox-pass']"));
        Assert.True(pane.Find("[data-testid='inbox-pass-discard']").HasAttribute("disabled"));
        Assert.True(pane.Find("[data-testid='inbox-pass-plan-edit']").HasAttribute("disabled"));

        hold.SetResult();
        await applying;

        pane.WaitForAssertion(() => Assert.Contains(
            harness.Toasts.Visible,
            toast => toast.TestId == InboxDesktopState.TriagePassResultTestId && toast.Message == "Applied 5 decisions. 1 left for you."));
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass']"));
    }

    [Fact]
    public async Task U_during_the_review_takes_the_last_decision_back_and_what_came_back_is_left_for_the_reader()
    {
        using var harness = Harness.Create();
        var scene = await harness.SceneAsync();
        harness.Inbox.FailAct[("merge", scene.Duplicate.Id)] = Error.Validation("comment.locked", "The entry is locked");
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);
        await pane.Find("[data-testid='inbox-pass-apply']").ClickAsync(new());
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-pass-error']")));
        Assert.StartsWith("Left for you: 1 item", Squash(pane.Find("[data-testid='inbox-pass-left']").TextContent), StringComparison.Ordinal);

        await PressAsync(pane, "u");

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(InboxStatus.Unprocessed, Status(harness, scene.PlanA));
            Assert.Equal(InboxStatus.Unprocessed, Status(harness, scene.PlanB));
            Assert.StartsWith("Left for you: 3 items", Squash(pane.Find("[data-testid='inbox-pass-left']").TextContent), StringComparison.Ordinal);
        });
        Assert.NotNull(pane.Find("[data-testid='inbox-pass']"));
    }

    [Fact]
    public async Task A_filing_that_finds_the_item_already_in_its_list_records_no_undo_step()
    {
        using var harness = Harness.Create();
        var scene = await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);

        // Filed by another hand while the review was open.
        await harness.Inbox.MoveToListAsync(scene.Filed.Id, scene.Reading.Id, TestContext.Current.CancellationToken);
        await pane.InvokeAsync(harness.State.ReloadAsync);

        await pane.Find("[data-testid='inbox-pass-apply']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='inbox-pass']")));
        Assert.Equal(0, harness.State.DecisionCount(InboxDecisionKind.MoveToList));
        Assert.Equal(1, harness.State.DecisionCount(InboxDecisionKind.Archive));
    }

    [Fact]
    public async Task A_row_partly_accepted_says_so_and_pressing_it_accepts_them_all()
    {
        using var harness = Harness.Create();
        var one = harness.Inbox.Seed("Lunch");
        var two = harness.Inbox.Seed("Coffee");
        harness.Inbox.TriageAdvisorAvailable = true;
        harness.Inbox.TriagePassAnswer = Result.Success(InboxTriagePassDto.Nothing with
        {
            Archives = [new InboxTriageArchiveDto(one.Id, "Nothing to act on.", 0.9), new InboxTriageArchiveDto(two.Id, "Maybe.", 0.3)],
        });
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);

        var toggle = pane.Find("[data-testid='inbox-pass-archive-accept']");
        Assert.Equal("mixed", toggle.GetAttribute("aria-pressed"));
        Assert.Equal("Partly accepted", Squash(toggle.TextContent));

        await toggle.ClickAsync(new());

        toggle = pane.Find("[data-testid='inbox-pass-archive-accept']");
        Assert.Equal("true", toggle.GetAttribute("aria-pressed"));
        Assert.Equal("2 of 2 accepted", Count(pane));
    }

    [Fact]
    public async Task Opening_another_slice_or_filtering_the_rows_puts_the_review_away()
    {
        using var harness = Harness.Create();
        await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);

        await pane.InvokeAsync(() => harness.State.ToggleKind("text"));
        Assert.Null(harness.State.TriagePassDraft);

        await pane.InvokeAsync(harness.State.ClearKindFilter);
        await ProposeAsync(pane);
        await pane.InvokeAsync(() => harness.State.SelectSlice(InboxDesktopState.DeferredSliceId));

        Assert.Null(harness.State.TriagePassDraft);
        Assert.Empty(pane.FindAll("[data-testid='inbox-pass']"));
    }

    // --- Left for you --------------------------------------------------------------

    [Fact]
    public async Task Triage_them_puts_the_pass_away_and_opens_triage_on_a_left_over_item()
    {
        using var harness = Harness.Create();
        var scene = await harness.SceneAsync();
        var pane = await harness.StartTriageAsync();
        await ProposeAsync(pane);

        await pane.Find("[data-testid='inbox-pass-triage-left']").ClickAsync(new());

        Assert.Empty(pane.FindAll("[data-testid='inbox-pass']"));
        Assert.True(harness.State.TriageMode);
        Assert.Equal(scene.Left.Id, harness.State.SelectedItemId);
        Assert.Equal(scene.Left.Title, pane.Find("#inbox-triage-title").TextContent.Trim());
        Assert.Empty(harness.Inbox.BatchRoutes);
    }

    // --- Helpers ----------------------------------------------------------------

    private static async Task ProposeAsync(IRenderedComponent<InboxPane> pane)
    {
        await pane.Find("[data-testid='inbox-triage-propose']").ClickAsync(new());
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-pass']")));
    }

    private static Task PressAsync(IRenderedComponent<InboxPane> pane, string key) =>
        pane.InvokeAsync(() => pane.Instance.OnShortcutAsync(key));

    private static string Count(IRenderedComponent<InboxPane> pane) =>
        Squash(pane.Find("[data-testid='inbox-pass-count']").TextContent);

    private static string ApplyLabel(IRenderedComponent<InboxPane> pane) =>
        Squash(pane.Find("[data-testid='inbox-pass-apply']").TextContent);

    private static InboxStatus Status(Harness harness, InboxItemDto item) => harness.Inbox.Find(item.Id)!.Status;

    private static string Squash(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>The items the scene seeds, one per kind of proposal the fake
    /// advisor makes, and the task and list it reads them against.</summary>
    private sealed record Scene(
        InboxTriageTaskDto Task,
        InboxListDto Reading,
        InboxItemDto Duplicate,
        InboxItemDto PlanA,
        InboxItemDto PlanB,
        InboxItemDto RouteLow,
        InboxItemDto RouteTask,
        InboxItemDto Filed,
        InboxItemDto Archived,
        InboxItemDto Left);

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

            var settings = new GitHubSettingsStore(
                Path.Combine(Path.GetTempPath(), "backlog-inbox-triage-pass-tests", Guid.NewGuid().ToString("n"), "github.json"));
            Assert.Null(settings.SetRepositories([new GitHubRepositoryRef("web", "acme", "web")]));
            Assert.Null(settings.SetShowRepositoryColours(true));

            context.Services.AddSingleton(settings);
            TasksTestHost.AddToastChannel(context.Services);
            var inbox = InboxTestHost.AddInboxState(context.Services);

            return new Harness(context, inbox);
        }

        /// <summary>
        /// Seeds one item for every kind of proposal and has the harness's fake
        /// advisor make the pass over them, the way the harness app does: a
        /// duplicate of an open task, two items sharing a tag (a plan), one with a
        /// repository and one with notes (two routes, the first of them the
        /// pass's one low-confidence proposal), one tagged with a list's name, a
        /// bare title, and one it cannot place. The duplicate's target title and
        /// status are filled in as the module fills them before the pane sees
        /// the pass.
        /// </summary>
        public async Task<Scene> SceneAsync()
        {
            var task = new InboxTriageTaskDto(Guid.NewGuid(), "Tasks list re-keys on remote change", [], "in progress");
            var reading = Inbox.SeedList("Reading");
            var at = Inbox.Now;

            var duplicate = Inbox.Seed("Tasks list re-keys when a remote change lands", capturedAt: at.AddMinutes(9));
            var planA = Inbox.Seed("Sync conflict banner", tags: ["sync"], repoIds: ["acme/web"], capturedAt: at.AddMinutes(8));
            var planB = Inbox.Seed("Sync retry backoff", tags: ["sync"], capturedAt: at.AddMinutes(7));
            var routeLow = Inbox.Seed("Fix login redirect", repoIds: ["acme/web"], capturedAt: at.AddMinutes(6));
            var routeTask = Inbox.Seed("Write the release notes", bodyMd: "Cover the sync fixes.", capturedAt: at.AddMinutes(5));
            var filed = Inbox.Seed(".NET 11 performance", tags: ["reading"], capturedAt: at.AddMinutes(4));
            var archived = Inbox.Seed("Lunch", capturedAt: at.AddMinutes(3));
            var left = Inbox.Seed("Think about the roadmap for next year", capturedAt: at.AddMinutes(2));
            var waiting = new[] { duplicate, planA, planB, routeLow, routeTask, filed, archived, left };

            var context = new InboxTriageContextDto([task], [], ["acme/web"], [new InboxTriageListDto(reading.Id, reading.Name)]);
            var pass = (await new FakeInboxTriageAdvisor().ProposePassAsync(
                [.. waiting.Select(InboxTriageItemDto.From)],
                context,
                TestContext.Current.CancellationToken)).Value;

            Inbox.TriageAdvisorAvailable = true;
            Inbox.TriagePassAnswer = Result.Success(pass with
            {
                Duplicates = [.. pass.Duplicates.Select(pair => pair with { TargetTitle = task.Title, TargetStatus = task.Status })],
            });

            return new Scene(task, reading, duplicate, planA, planB, routeLow, routeTask, filed, archived, left);
        }

        /// <summary>Renders the pane, loads it and presses t.</summary>
        public async Task<IRenderedComponent<InboxPane>> StartTriageAsync()
        {
            var pane = Context.Render<InboxPane>();
            await pane.InvokeAsync(State.InitializeAsync);
            await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("t"));
            Assert.True(State.TriageMode);
            return pane;
        }

        public void Dispose() => Context.Dispose();
    }
}
