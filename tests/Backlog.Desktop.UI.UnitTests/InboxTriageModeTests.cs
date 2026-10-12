using System.Text.RegularExpressions;
using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Triage mode's own screen (features.md#triage-mode, #ai-triage-cards): the
/// session's progress, the item, the AI cards and their numbering, the
/// repository picker, the four decisions, Up next and the last decision with
/// Undo — and, without an advisor, no AI surface at all (local ADR 0023).
/// </summary>
public sealed class InboxTriageModeTests
{
    // --- Progress -------------------------------------------------------------

    [Fact]
    public async Task The_header_counts_the_session_against_the_rows_triage_began_with_and_k_and_j_step()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("One", "Two", "Three");
        var pane = await harness.StartTriageAsync();

        Assert.Equal("Triage · Inbox", pane.Find(".inbox-triage__slice").TextContent.Trim());
        Assert.Equal("1 of 3 · 0 decided this session", Progress(pane));
        var bar = pane.Find("[data-testid='inbox-triage-bar']");
        Assert.Equal("progressbar", bar.GetAttribute("role"));
        Assert.Equal("Triage progress", bar.GetAttribute("aria-label"));
        Assert.Equal("0", bar.GetAttribute("aria-valuemin"));
        Assert.Equal("3", bar.GetAttribute("aria-valuemax"));
        Assert.Equal("0", bar.GetAttribute("aria-valuenow"));

        await pane.Find("[data-testid='inbox-triage-skip']").ClickAsync(new());
        Assert.Equal(items[1].Id, harness.State.SelectedItemId);
        Assert.Equal("2 of 3 · 0 decided this session", Progress(pane));

        await pane.Find("[data-testid='inbox-triage-previous']").ClickAsync(new());
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);
        Assert.Equal("Previous item", pane.Find("[data-testid='inbox-triage-previous']").GetAttribute("aria-label"));

        await pane.Find("[data-testid='inbox-archive']").ClickAsync(new());
        pane.WaitForAssertion(() =>
        {
            Assert.Equal("2 of 3 · 1 decided this session", Progress(pane));
            Assert.Equal("1", pane.Find("[data-testid='inbox-triage-bar']").GetAttribute("aria-valuenow"));
            Assert.Contains("width: 33.33%", pane.Find(".inbox-triage__bar-fill").GetAttribute("style"), StringComparison.Ordinal);
        });

        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-propose']"));
        Assert.DoesNotContain("Let AI propose the rest", pane.Markup, StringComparison.Ordinal);
    }

    /// <summary>The line's m counts the app session's decisions; the bar fills
    /// with progress through the rows triage began with, so a decision taken
    /// before triage does not start it part full, and an undo empties it again.</summary>
    [Fact]
    public async Task The_bar_fills_with_the_rows_decided_since_triage_began_not_the_session_count()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Before", "One", "Two");
        var pane = harness.Render();
        await pane.InvokeAsync(harness.State.InitializeAsync);
        await pane.Find($"[data-testid='inbox-item-{items[0].Id:D}']").ClickAsync(new());
        await PressAsync(pane, "a");
        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Archived, harness.Inbox.Find(items[0].Id)!.Status));

        await PressAsync(pane, "t");

        Assert.Equal("1 of 2 · 1 decided this session", Progress(pane));
        var bar = pane.Find("[data-testid='inbox-triage-bar']");
        Assert.Equal("2", bar.GetAttribute("aria-valuemax"));
        Assert.Equal("0", bar.GetAttribute("aria-valuenow"));

        await PressAsync(pane, "a");
        pane.WaitForAssertion(() =>
        {
            Assert.Equal("2 of 2 · 2 decided this session", Progress(pane));
            Assert.Equal("1", pane.Find("[data-testid='inbox-triage-bar']").GetAttribute("aria-valuenow"));
            Assert.Contains("width: 50%", pane.Find(".inbox-triage__bar-fill").GetAttribute("style"), StringComparison.Ordinal);
        });

        await PressAsync(pane, "u");
        pane.WaitForAssertion(() => Assert.Equal("0", pane.Find("[data-testid='inbox-triage-bar']").GetAttribute("aria-valuenow")));
    }

    /// <summary>A capture that arrives during triage is numbered after the rows
    /// triage began with and grows the total — never "0 of N", never another
    /// item's number.</summary>
    [Fact]
    public async Task An_item_that_arrives_during_triage_is_numbered_after_the_started_rows()
    {
        using var harness = Harness.Create();
        harness.SeedNewestFirst("One", "Two");
        var pane = await harness.StartTriageAsync();
        Assert.Equal("1 of 2 · 0 decided this session", Progress(pane));

        // Newer than both, so it lands first in the rows.
        var arrival = harness.Inbox.Seed("Arrived", capturedAt: harness.Inbox.Now.AddMinutes(10));
        await pane.InvokeAsync(harness.State.ReloadAsync);
        pane.WaitForAssertion(() => Assert.Equal("1 of 3 · 0 decided this session", Progress(pane)));

        await PressAsync(pane, "k");

        Assert.Equal(arrival.Id, harness.State.SelectedItemId);
        Assert.Equal("3 of 3 · 0 decided this session", Progress(pane));
    }

    [Fact]
    public async Task Back_to_the_list_leaves_triage()
    {
        using var harness = Harness.Create();
        harness.SeedNewestFirst("One");
        var pane = await harness.StartTriageAsync();

        await pane.Find("[data-testid='inbox-triage-header'] [data-testid='inbox-triage-leave']").ClickAsync(new());

        Assert.False(harness.State.TriageMode);
        Assert.NotNull(pane.Find("[data-testid='inbox-pane-body']"));
    }

    // --- The item -----------------------------------------------------------

    [Fact]
    public async Task The_item_card_shows_its_kind_source_age_title_notes_and_tags()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed(
            "Tasks pane loses scroll",
            channel: "mobile",
            bodyMd: "Happens after a **sync**.",
            sourceUrl: "https://example.com/issue",
            tags: ["bug"]);
        var pane = await harness.StartTriageAsync();

        var card = pane.Find("[data-testid='inbox-triage-item']");
        Assert.Equal(InboxLabels.Kind(item.KindSlug), card.QuerySelector("[data-testid='inbox-triage-kind']")!.TextContent.Trim());
        Assert.Equal("Mobile", card.QuerySelector("[data-testid='inbox-triage-source']")!.TextContent.Trim());
        Assert.Equal($"captured {harness.State.Age(item.CapturedAt)}", card.QuerySelector("[data-testid='inbox-triage-age']")!.TextContent.Trim());
        Assert.Equal("Tasks pane loses scroll", pane.Find("h2#inbox-triage-title").TextContent.Trim());
        Assert.Contains("Happens after a", card.QuerySelector("[data-testid='inbox-triage-notes']")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("https://example.com/issue", card.QuerySelector("[data-testid='inbox-triage-link']")!.GetAttribute("href"));
        Assert.Contains("bug", card.QuerySelector("[data-testid='inbox-triage-tags']")!.TextContent, StringComparison.Ordinal);
        Assert.Equal(InboxItemDetail.TagsInputId, pane.Find("[data-testid='inbox-triage-tags'] input").Id);
    }

    [Fact]
    public async Task An_item_with_no_notes_says_only_the_title_was_captured()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("Just a title");
        var pane = await harness.StartTriageAsync();

        Assert.NotNull(pane.Find("[data-testid='inbox-triage-no-notes']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-notes']"));
    }

    // --- Without Foundry ------------------------------------------------------

    [Fact]
    public async Task Without_the_advisor_no_ai_surface_is_drawn_and_the_suggestions_number_from_one()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Sync drops edits");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Tag, "sync");
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-suggestion-tag-sync']")));
        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-ai']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-card-duplicate']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-card-plan']"));
        Assert.Empty(pane.FindAll(".inbox-triage__ai-mark"));
        Assert.Empty(harness.Inbox.AdviceRequests);
        Assert.Equal("1", pane.Find("[data-testid='inbox-suggestion-tag-sync'] kbd").TextContent.Trim());

        await PressDigitAsync(pane, "1");

        pane.WaitForAssertion(() => Assert.Contains(harness.Inbox.Find(item.Id)!.Tags, tag => tag.Name == "sync"));
    }

    // --- The duplicate card -------------------------------------------------

    [Fact]
    public async Task The_duplicate_card_merges_into_the_backlog_entry_and_moves_on()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Scroll jumps", "Next one");
        var taskId = Guid.NewGuid();
        harness.Advise(items[0].Id, duplicate: new InboxTriageDuplicateDto(InboxTriageTargetKind.Task, taskId, "Tasks list re-keys", "same pane, same symptom"));
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-triage-card-duplicate']")));
        Assert.Contains("AI noticed", pane.Find("[data-testid='inbox-triage-ai']").TextContent, StringComparison.Ordinal);
        var card = pane.Find("[data-testid='inbox-triage-card-duplicate']");
        Assert.Equal("1", card.QuerySelector("kbd")!.TextContent.Trim());
        Assert.Contains("Probably a duplicate", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("same pane, same symptom", card.TextContent, StringComparison.Ordinal);
        var target = card.QuerySelector("[data-testid='inbox-triage-duplicate-target']")!.TextContent;
        Assert.Contains("Backlog entry", target, StringComparison.Ordinal);
        Assert.Contains("Tasks list re-keys", target, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-merge-all']"));
        Assert.Equal("Merge into the entry", pane.Find("[data-testid='inbox-triage-merge']").TextContent.Trim());

        await pane.Find("[data-testid='inbox-triage-merge']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Contains((items[0].Id, taskId), harness.Inbox.Merges);
            Assert.Equal(items[1].Id, harness.State.SelectedItemId);
        });
    }

    [Fact]
    public async Task Merge_all_three_appears_with_a_related_undecided_item_and_merges_both_as_one_undo_step()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Scroll jumps", "Scroll jumps on phone edit", "Unrelated");
        var taskId = Guid.NewGuid();
        harness.Advise(items[0].Id, duplicate: new InboxTriageDuplicateDto(InboxTriageTargetKind.Task, taskId, "Tasks list re-keys", "same symptom"));
        harness.Inbox.SeedRelations(items[0].Id, items: [new InboxRelatedItemDto(items[1].Id, items[1].Title, InboxStatus.Unprocessed, InboxRelationKind.SameLink, "same link")]);
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-triage-merge-all']")));
        var companion = pane.Find("[data-testid='inbox-triage-duplicate-companion']").TextContent;
        Assert.Contains("Scroll jumps on phone edit", companion, StringComparison.Ordinal);
        Assert.Contains("Also unprocessed", companion, StringComparison.Ordinal);

        await pane.Find("[data-testid='inbox-triage-merge-all']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { (items[1].Id, taskId), (items[0].Id, taskId) }, harness.Inbox.Merges);
            Assert.Equal(InboxStatus.Archived, harness.Inbox.Find(items[0].Id)!.Status);
            Assert.Equal(InboxStatus.Archived, harness.Inbox.Find(items[1].Id)!.Status);
            Assert.Equal(items[2].Id, harness.State.SelectedItemId);
        });

        var step = Assert.IsType<InboxGroupUndoStep>(harness.State.UndoHistory.Latest);
        Assert.Equal(2, step.Steps.Count);
        Assert.Equal(2, harness.State.DecisionCount(InboxDecisionKind.MergeIntoTask));
        Assert.Equal("2 items → Merge all into a task", JustDecided(pane));

        await pane.Find("[data-testid='inbox-triage-undo']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(items[0].Id)!.Status);
            Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(items[1].Id)!.Status);
        });
    }

    [Fact]
    public async Task Compare_opens_the_item_and_the_duplicate_side_by_side()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Scroll jumps", "Scroll jumps again");
        harness.Advise(items[0].Id, duplicate: new InboxTriageDuplicateDto(InboxTriageTargetKind.InboxItem, items[1].Id, items[1].Title, "same words"));
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-triage-compare']")));
        await pane.Find("[data-testid='inbox-triage-compare']").ClickAsync(new());

        var dialog = pane.Find("[data-testid='inbox-triage-compare-dialog']");
        Assert.Contains("Scroll jumps", dialog.QuerySelector("[data-testid='inbox-triage-compare-this']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Scroll jumps again", dialog.QuerySelector("[data-testid='inbox-triage-compare-target']")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_a_duplicate_hides_the_card_and_the_suggestions_renumber_from_one()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Scroll jumps");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Tag, "sync");
        harness.Advise(item.Id, duplicate: new InboxTriageDuplicateDto(InboxTriageTargetKind.Task, Guid.NewGuid(), "Tasks list re-keys", "same symptom"));
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.Equal("2", pane.Find("[data-testid='inbox-suggestion-tag-sync'] kbd").TextContent.Trim()));

        await pane.Find("[data-testid='inbox-triage-not-duplicate']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='inbox-triage-ai']"));
            Assert.Equal("1", pane.Find("[data-testid='inbox-suggestion-tag-sync'] kbd").TextContent.Trim());
        });
        Assert.Empty(harness.Inbox.Merges);
        Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(item.Id)!.Status);
    }

    [Fact]
    public async Task A_duplicate_of_another_inbox_item_is_archived_as_its_duplicate()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Scroll jumps", "Scroll jumps again");
        harness.Advise(items[0].Id, duplicate: new InboxTriageDuplicateDto(InboxTriageTargetKind.InboxItem, items[1].Id, items[1].Title, "same words"));
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.Equal("Archive as a duplicate", pane.Find("[data-testid='inbox-triage-merge']").TextContent.Trim()));
        Assert.StartsWith("Inbox · ", pane.Find("[data-testid='inbox-triage-duplicate-target'] .inbox-triage__side-label").TextContent.Trim(), StringComparison.Ordinal);

        await pane.Find("[data-testid='inbox-triage-merge']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            var archived = harness.Inbox.Find(items[0].Id)!;
            Assert.Equal(InboxStatus.Archived, archived.Status);
            Assert.Equal(items[1].Id, archived.DuplicateOf);
        });
        Assert.Equal("Scroll jumps → Archived as a duplicate", JustDecided(pane));
    }

    // --- The plan card --------------------------------------------------------

    [Fact]
    public async Task The_plan_card_names_the_plan_and_its_members_and_makes_it_through_the_route_panel()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Scroll jumps", "Saved after a failed save", "MCP writes do not refresh", "Unrelated");
        harness.Advise(items[0].Id, plan: new InboxTriagePlanGroupingDto("Tasks sync polish", [items[0].Id, items[1].Id, items[2].Id], "all about live updates"));
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-triage-card-plan']")));
        var card = pane.Find("[data-testid='inbox-triage-card-plan']");
        Assert.Equal("1", card.QuerySelector("kbd")!.TextContent.Trim());
        Assert.Equal("Tasks sync polish", card.QuerySelector("[data-testid='inbox-triage-plan-title']")!.TextContent.Trim());
        Assert.Contains("this and 2 others", card.TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ["Saved after a failed save", "MCP writes do not refresh"],
            pane.FindAll("[data-testid='inbox-triage-plan-members'] li").Select(li => li.TextContent.Trim()));
        Assert.Equal("Make the plan · 3 items", pane.Find("[data-testid='inbox-triage-make-plan']").TextContent.Trim());

        var addToPlan = pane.Find("[data-testid='inbox-triage-add-to-plan']");
        Assert.True(addToPlan.HasAttribute("disabled"));
        Assert.Equal("Adding to an existing plan is not available yet", addToPlan.GetAttribute("title"));

        await pane.Find("[data-testid='inbox-triage-make-plan']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-route-panel']")));
        Assert.Equal([items[0].Id, items[1].Id, items[2].Id], harness.Inbox.Proposals.Single().Ids);

        // Confirming the batch is a decision on the item shown: triage moves on,
        // past the members the batch routed with it, to the next item still open.
        await pane.InvokeAsync(() => harness.State.ConfirmRouteAsync());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(InboxStatus.Triaged, harness.Inbox.Find(items[0].Id)!.Status);
            Assert.Equal(InboxStatus.Triaged, harness.Inbox.Find(items[1].Id)!.Status);
            Assert.Equal(items[3].Id, harness.State.SelectedItemId);
        });
    }

    [Fact]
    public async Task Dismiss_hides_the_plan_card()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Scroll jumps", "Saved after a failed save");
        harness.Advise(items[0].Id, plan: new InboxTriagePlanGroupingDto("Tasks sync polish", [items[0].Id, items[1].Id], "same area"));
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='inbox-triage-dismiss-plan']")));
        await pane.Find("[data-testid='inbox-triage-dismiss-plan']").ClickAsync(new());

        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-card-plan']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-ai']"));
        Assert.Empty(harness.Inbox.Proposals);
    }

    // --- Numbering --------------------------------------------------------------

    [Fact]
    public async Task With_two_cards_the_suggestions_number_from_three_and_one_takes_the_duplicate()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Scroll jumps", "Saved after a failed save", "Third");
        var taskId = Guid.NewGuid();
        harness.Advise(
            items[0].Id,
            duplicate: new InboxTriageDuplicateDto(InboxTriageTargetKind.Task, taskId, "Tasks list re-keys", "same symptom"),
            plan: new InboxTriagePlanGroupingDto("Tasks sync polish", [items[0].Id, items[1].Id], "same area"));
        harness.Inbox.SeedSuggestion(items[0].Id, InboxSuggestionKind.Tag, "sync");
        harness.Inbox.SeedSuggestion(items[0].Id, InboxSuggestionKind.Tag, "ui");
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["1", "2"], pane.FindAll("[data-testid='inbox-triage-ai'] .inbox-triage__card-head kbd").Select(key => key.TextContent.Trim()));
            Assert.Equal("3", pane.Find("[data-testid='inbox-suggestion-tag-sync'] kbd").TextContent.Trim());
            Assert.Equal("4", pane.Find("[data-testid='inbox-suggestion-tag-ui'] kbd").TextContent.Trim());
        });

        await PressDigitAsync(pane, "3");
        pane.WaitForAssertion(() => Assert.Contains(harness.Inbox.Find(items[0].Id)!.Tags, tag => tag.Name == "sync"));
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);

        await PressDigitAsync(pane, "1");
        pane.WaitForAssertion(() =>
        {
            Assert.Contains((items[0].Id, taskId), harness.Inbox.Merges);
            Assert.Equal(items[1].Id, harness.State.SelectedItemId);
        });
    }

    // --- The repository picker -------------------------------------------------

    [Fact]
    public async Task The_picker_has_a_box_per_repository_with_its_edge_preselected_from_the_advisor()
    {
        using var harness = Harness.Create(repositories: true, colours: true);
        var item = harness.Inbox.Seed("Sync drops edits", repoIds: ["acme/web"]);
        harness.Inbox.TriageAdvisorAvailable = true;
        harness.Inbox.TriageAdvice[item.Id] = Result.Success(new InboxTriageAdviceDto(item.Id, null, null, ["acme/sync"]));
        var pane = await harness.StartTriageAsync();

        pane.WaitForAssertion(() => Assert.True(Box(pane, "acme-sync").HasAttribute("checked")));
        Assert.Equal(2, pane.FindAll("[data-testid='inbox-triage-repos'] input[type='checkbox']").Count);
        Assert.Equal("group", pane.Find("[data-testid='inbox-triage-repos']").GetAttribute("role"));
        Assert.False(Box(pane, "acme-web").HasAttribute("checked"));
        foreach (var label in pane.FindAll("[data-testid='inbox-triage-repos'] label"))
        {
            Assert.Matches(new Regex(@"\brepo-mark repo-mark--[1-5]\b"), label.GetAttribute("class") ?? string.Empty);
        }

        Assert.NotNull(pane.Find("[data-testid='inbox-triage-repo-acme-sync'] .inbox-triage__ai-mark"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-repo-acme-web'] .inbox-triage__ai-mark"));
        Assert.Equal("one entry", pane.Find("[data-testid='inbox-triage-route-hint']").TextContent.Trim());
    }

    [Fact]
    public async Task Without_advice_the_picker_shows_the_item_repositories_and_no_edge_while_colours_are_off()
    {
        using var harness = Harness.Create(repositories: true);
        harness.Inbox.Seed("Sync drops edits", repoIds: ["acme/web"]);
        var pane = await harness.StartTriageAsync();

        Assert.True(Box(pane, "acme-web").HasAttribute("checked"));
        Assert.False(Box(pane, "acme-sync").HasAttribute("checked"));
        Assert.DoesNotContain("repo-mark", pane.Find("[data-testid='inbox-triage-repos']").InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ticking_writes_the_repositories_and_the_hint_counts_the_entries()
    {
        using var harness = Harness.Create(repositories: true);
        var item = harness.Inbox.Seed("Sync drops edits");
        var pane = await harness.StartTriageAsync();

        Assert.Equal("one entry, no repository", Hint(pane));

        await Box(pane, "acme-sync").ChangeAsync(new ChangeEventArgs { Value = true });
        pane.WaitForAssertion(() => Assert.Equal(["acme/sync"], harness.Inbox.Find(item.Id)!.RepoIds));
        Assert.Equal("one entry", Hint(pane));

        await Box(pane, "acme-web").ChangeAsync(new ChangeEventArgs { Value = true });
        pane.WaitForAssertion(() => Assert.Equal(["acme/web", "acme/sync"], harness.Inbox.Find(item.Id)!.RepoIds));
        Assert.Equal("2 linked entries", Hint(pane));
        Assert.Equal("2 linked entries", pane.Find("[data-testid='inbox-move-to-backlog'] .inbox-triage__decision-hint").TextContent.Trim());

        await Box(pane, "acme-web").ChangeAsync(new ChangeEventArgs { Value = false });
        pane.WaitForAssertion(() => Assert.Equal(["acme/sync"], harness.Inbox.Find(item.Id)!.RepoIds));
    }

    [Fact]
    public async Task Two_ticked_repositories_and_r_make_one_task_in_each()
    {
        using var harness = Harness.Create(repositories: true);
        var item = harness.Inbox.Seed("Sync drops edits");
        var pane = await harness.StartTriageAsync();

        await Box(pane, "acme-web").ChangeAsync(new ChangeEventArgs { Value = true });
        await Box(pane, "acme-sync").ChangeAsync(new ChangeEventArgs { Value = true });
        pane.WaitForAssertion(() => Assert.Equal(2, harness.Inbox.Find(item.Id)!.RepoIds.Count));

        await PressAsync(pane, "r");

        pane.WaitForAssertion(() =>
        {
            var routed = harness.Inbox.Find(item.Id)!;
            Assert.Equal(InboxStatus.Triaged, routed.Status);
            Assert.Equal(["acme/web", "acme/sync"], routed.Routing!.RepoIds);
            Assert.Equal(2, routed.Routing.TaskIds.Count);
        });
    }

    [Fact]
    public async Task R_after_the_advisor_preselection_routes_with_those_repositories()
    {
        using var harness = Harness.Create(repositories: true);
        var item = harness.Inbox.Seed("Sync drops edits");
        harness.Inbox.TriageAdvisorAvailable = true;
        harness.Inbox.TriageAdvice[item.Id] = Result.Success(new InboxTriageAdviceDto(item.Id, null, null, ["acme/web", "acme/sync"]));
        var pane = await harness.StartTriageAsync();
        pane.WaitForAssertion(() => Assert.Equal("2 linked entries", Hint(pane)));
        Assert.Empty(harness.Inbox.Find(item.Id)!.RepoIds);

        await PressAsync(pane, "r");

        pane.WaitForAssertion(() =>
        {
            var routed = harness.Inbox.Find(item.Id)!;
            Assert.Equal(InboxStatus.Triaged, routed.Status);
            Assert.Equal(["acme/web", "acme/sync"], routed.Routing!.RepoIds);
        });
    }

    [Fact]
    public async Task With_no_configured_repository_the_picker_says_so()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("Anything");
        var pane = await harness.StartTriageAsync();

        Assert.NotNull(pane.Find("[data-testid='inbox-triage-no-repos']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-repos']"));
    }

    // --- The decisions ------------------------------------------------------------

    [Fact]
    public async Task The_defer_button_offers_the_review_dates_as_d_does()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Later");
        var pane = await harness.StartTriageAsync();

        await pane.Find("[data-testid='inbox-decide-defer']").ClickAsync(new());
        var choices = pane.FindAll("[data-testid='inbox-defer-choices'] [role='menuitem']");
        Assert.Equal(4, choices.Count);

        await choices[0].ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Equal(harness.State.Today.AddDays(1), harness.Inbox.Find(item.Id)!.DeferredUntil));
    }

    // --- The aside ------------------------------------------------------------------

    [Fact]
    public async Task Up_next_lists_the_next_four_and_moves_on_after_a_decision()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("A", "B", "C", "D", "E", "F");
        var pane = await harness.StartTriageAsync();

        Assert.Equal(["B", "C", "D", "E"], UpNext(pane));
        Assert.Contains(" · ", pane.Find(".inbox-triage__next-meta").TextContent, StringComparison.Ordinal);

        await PressAsync(pane, "a");

        pane.WaitForAssertion(() => Assert.Equal(["C", "D", "E", "F"], UpNext(pane)));
        Assert.Equal(items[1].Id, harness.State.SelectedItemId);
    }

    [Fact]
    public async Task On_the_last_row_up_next_lists_the_items_skipped_on_the_way()
    {
        using var harness = Harness.Create();
        harness.SeedNewestFirst("A", "B", "C");
        var pane = await harness.StartTriageAsync();

        await PressAsync(pane, "j");
        await PressAsync(pane, "j");

        Assert.Equal(["A", "B"], UpNext(pane));
    }

    [Fact]
    public async Task Just_decided_names_the_item_and_the_list_and_undo_takes_it_back()
    {
        using var harness = Harness.Create();
        var reading = harness.Inbox.SeedList("Reading");
        var items = harness.SeedNewestFirst(".NET 11 performance", "Next");
        var pane = await harness.StartTriageAsync();

        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-just-decided']"));

        await PressAsync(pane, "l");
        var target = pane.FindAll("[data-testid='inbox-move-list'] [role='menuitem']").Single(row => row.TextContent.Contains("Reading", StringComparison.Ordinal));
        await target.ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(reading.Id, harness.Inbox.Find(items[0].Id)!.ListId);
            Assert.Equal(".NET 11 performance → Reading", JustDecided(pane));
        });

        await pane.Find("[data-testid='inbox-triage-undo']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Null(harness.Inbox.Find(items[0].Id)!.ListId));
        Assert.Empty(pane.FindAll("[data-testid='inbox-triage-just-decided']"));
    }

    [Fact]
    public void The_route_hint_counts_the_entries_move_to_backlog_makes()
    {
        Assert.Equal("one entry, no repository", InboxDesktopState.RouteHint(0));
        Assert.Equal("one entry", InboxDesktopState.RouteHint(1));
        Assert.Equal("3 linked entries", InboxDesktopState.RouteHint(3));
    }

    // --- Helpers ------------------------------------------------------------------

    private static Task PressAsync(IRenderedComponent<InboxPane> pane, string key) =>
        pane.InvokeAsync(() => pane.Instance.OnShortcutAsync(key));

    private static Task PressDigitAsync(IRenderedComponent<InboxPane> pane, string digit) =>
        pane.Find("[data-testid='inbox-triage-view']").KeyDownAsync(new KeyboardEventArgs { Key = digit });

    private static string Progress(IRenderedComponent<InboxPane> pane) =>
        Squash(pane.Find("[data-testid='inbox-triage-progress']").TextContent);

    private static string JustDecided(IRenderedComponent<InboxPane> pane) =>
        Squash(pane.Find("[data-testid='inbox-triage-just-decided-line']").TextContent);

    private static string Hint(IRenderedComponent<InboxPane> pane) =>
        pane.Find("[data-testid='inbox-triage-route-hint']").TextContent.Trim();

    private static AngleSharp.Dom.IElement Box(IRenderedComponent<InboxPane> pane, string repository) =>
        pane.Find($"[data-testid='inbox-triage-repo-{repository}'] input");

    private static IReadOnlyList<string> UpNext(IRenderedComponent<InboxPane> pane) =>
        [.. pane.FindAll("[data-testid='inbox-triage-up-next'] .inbox-triage__next-title").Select(title => title.TextContent.Trim())];

    private static string Squash(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class Harness : IDisposable
    {
        private Harness(BunitContext context, FakeInboxItems inbox, GitHubSettingsStore settings)
        {
            Context = context;
            Inbox = inbox;
            Settings = settings;
        }

        public BunitContext Context { get; }

        public FakeInboxItems Inbox { get; }

        public GitHubSettingsStore Settings { get; }

        public InboxDesktopState State => Context.Services.GetRequiredService<InboxDesktopState>();

        public static Harness Create(bool repositories = false, bool colours = false)
        {
            var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;

            var settings = new GitHubSettingsStore(
                Path.Combine(Path.GetTempPath(), "backlog-inbox-triage-mode-tests", Guid.NewGuid().ToString("n"), "github.json"));
            if (repositories)
            {
                Assert.Null(settings.SetRepositories([new GitHubRepositoryRef("web", "acme", "web"), new GitHubRepositoryRef("sync", "acme", "sync")]));
            }

            if (colours) Assert.Null(settings.SetShowRepositoryColours(true));

            context.Services.AddSingleton(settings);
            TasksTestHost.AddToastChannel(context.Services);
            var inbox = InboxTestHost.AddInboxState(context.Services);

            return new Harness(context, inbox, settings);
        }

        public IReadOnlyList<InboxItemDto> SeedNewestFirst(params string[] titles) =>
            [.. titles.Select((title, index) => Inbox.Seed(title, capturedAt: Inbox.Now.AddMinutes(titles.Length - index)))];

        /// <summary>Turns the advisor on and gives the item its cards.</summary>
        public void Advise(Guid itemId, InboxTriageDuplicateDto? duplicate = null, InboxTriagePlanGroupingDto? plan = null)
        {
            Inbox.TriageAdvisorAvailable = true;
            Inbox.TriageAdvice[itemId] = Result.Success(new InboxTriageAdviceDto(itemId, duplicate, plan, []));
        }

        public IRenderedComponent<InboxPane> Render() => Context.Render<InboxPane>();

        /// <summary>Renders the pane, loads it and presses t.</summary>
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
