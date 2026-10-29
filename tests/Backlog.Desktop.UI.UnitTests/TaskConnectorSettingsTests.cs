using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static Backlog.Desktop.UI.UnitTests.LinkedTaskPaneTests;
using static Backlog.Desktop.UI.UnitTests.LinkedTaskSourcesTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Connectors settings page (ADR 0020 §9): connect a repository or a product,
/// edit how it syncs, disconnect it. Drawn from the connectors' descriptors and the
/// connected targets alone, so it is rendered here with a fake connector.
/// </summary>
public sealed class TaskConnectorSettingsTests
{
    private static (BunitContext Context, InMemoryTargets Targets, RecordingSync Sync) Compose(bool withConnector = true, params ConnectedTarget[] targets)
    {
        var context = new BunitContext();
        var store = new InMemoryTargets(targets);
        var sync = new RecordingSync();
        context.Services.AddSingleton(new LinkedTaskSources(withConnector ? [new FakeConnector()] : [], store));
        context.Services.AddSingleton<ILinkedTaskSync>(sync);
        return (context, store, sync);
    }

    [Fact]
    public void The_page_is_registered_as_a_settings_section()
    {
        var services = new ServiceCollection().AddTaskConnectorSettings();

        var section = Assert.Single(services.BuildServiceProvider().GetServices<SettingsSection>());
        Assert.Equal("Connectors", section.Title);
        Assert.Equal(typeof(TaskConnectorSettings), section.Component);
    }

    /// <summary>Inside the settings frame the page's headline counts the connected
    /// targets, and moves when one is connected.</summary>
    [Fact]
    public async Task The_headline_counts_the_connected_sources()
    {
        var (context, _, _) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/one"));
        using var _context = context;

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "Connectors")
            .AddChildContent<TaskConnectorSettings>());

        string Headline() => frame.Find("[data-testid='settings-headline']").TextContent.Trim();

        Assert.Equal("1 task source connected.", Headline());

        await frame.Find("input#task-connector-target").InputAsync(new() { Value = "owner/two" });
        await frame.Find("[data-testid='task-connector-connect']").ClickAsync(new());

        frame.WaitForAssertion(() => Assert.Equal("2 task sources connected.", Headline()));
    }

    [Fact]
    public void With_nothing_connected_the_headline_says_so()
    {
        Assert.Equal("No task sources connected.", TaskConnectorSettings.Headline(0));
        Assert.Equal("5 task sources connected.", TaskConnectorSettings.Headline(5));
    }

    /// <summary>The cards the page draws are the ones its section declares, in that
    /// order: connect, connected, and disconnected once a source left tasks behind.</summary>
    [Fact]
    public void The_cards_on_the_page_are_the_ones_its_section_declares()
    {
        var section = Assert.Single(new ServiceCollection().AddTaskConnectorSettings().BuildServiceProvider().GetServices<SettingsSection>());
        Assert.Equal(["Connect a task source", "Connected task sources", "Disconnected sources"], section.CardTitles!);

        var (context, _, _) = ComposeDisconnected(new DisconnectedSource(FakeConnector.Id, "owner/old", 3, 0));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Equal(section.CardTitles!, page.FindAll(".settings-card__title").Select(title => title.TextContent.Trim()));
        Assert.NotNull(page.Find("section.settings-card[data-testid='task-connectors-connect-card'] [data-testid='task-connectors-connect']"));
        Assert.NotNull(page.Find("section.settings-card[data-testid='task-connectors-connected-card'] [data-testid='task-connectors-none']"));
        Assert.NotNull(page.Find("section.settings-card[data-testid='task-connectors-disconnected-card'] [data-testid='task-connectors-disconnected']"));
    }

    /// <summary>With no connector installed only the connect card is drawn, saying
    /// so; there is nothing connected to list.</summary>
    [Fact]
    public void With_no_connector_installed_only_the_connect_card_is_drawn()
    {
        var (context, _, _) = Compose(withConnector: false);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Equal(["Connect a task source"], page.FindAll(".settings-card__title").Select(title => title.TextContent.Trim()));
        Assert.NotNull(page.Find("section.settings-card [data-testid='task-connectors-empty']"));
    }

    [Fact]
    public void With_no_connector_installed_it_says_so_and_offers_nothing_to_connect()
    {
        var (context, _, _) = Compose(withConnector: false);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.NotNull(page.Find("[data-testid='task-connectors-empty']"));
        Assert.Empty(page.FindAll("[data-testid='task-connectors-connect']"));
    }

    [Fact]
    public async Task Connecting_a_repository_stores_a_target_and_draws_its_card()
    {
        var (context, targets, _) = Compose();
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        Assert.NotNull(page.Find("[data-testid='task-connectors-none']"));

        await page.Find("input#task-connector-target").InputAsync(new() { Value = " owner/repo " });
        await page.Find("[data-testid='task-connector-connect']").ClickAsync(new());

        var stored = Assert.Single(targets.List());
        Assert.Equal(new ConnectedTarget(FakeConnector.Id, "owner/repo"), stored);
        Assert.Equal("owner/repo", page.Find("[data-testid='task-connector-target-card-name']").TextContent);
    }

    [Fact]
    public async Task Connecting_the_same_target_twice_is_refused_in_words()
    {
        var (context, targets, _) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("input#task-connector-target").InputAsync(new() { Value = "owner/repo" });
        await page.Find("[data-testid='task-connector-connect']").ClickAsync(new());

        Assert.Single(targets.List());
        Assert.Contains("already connected", page.Find("[data-testid='task-connector-target']").TextContent);
    }

    [Fact]
    public async Task An_option_changed_on_the_card_is_stored_on_the_target()
    {
        var (context, targets, _) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-title-follows'] button[role='switch']").ClickAsync(new());
        await page.Find("[data-testid='task-connector-target-card-interval'] select").ChangeAsync(new() { Value = "60" });

        var stored = Assert.Single(targets.List());
        Assert.False(stored.TitleFollowsSource);
        Assert.Equal(TimeSpan.FromHours(1), stored.SyncInterval);
    }

    /// <summary>"Complete at the source" is drawn only for a connector whose
    /// capabilities say it can, and what is switched is stored on the target.</summary>
    [Fact]
    public async Task Complete_at_the_source_is_offered_for_a_connector_that_can_and_stored_on_the_target()
    {
        var context = new BunitContext();
        using var _context = context;
        var targets = new InMemoryTargets(
            new ConnectedTarget(FakeConnector.Id, "owner/plain"),
            new ConnectedTarget(CompletingConnector.Id, "owner/repo"));
        context.Services.AddSingleton(new LinkedTaskSources([new FakeConnector(), new CompletingConnector()], targets));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());

        var page = context.Render<TaskConnectorSettings>();

        var toggle = Assert.Single(page.FindAll("[data-testid='task-connector-target-card-complete-at-source']"));
        Assert.Contains("owner/repo", toggle.Closest(".connected-target")!.TextContent);

        await page.Find("[data-testid='task-connector-target-card-complete-at-source'] button[role='switch']").ClickAsync(new());

        Assert.True(targets.Get(CompletingConnector.Id, "owner/repo")!.CompleteAtSource);
        Assert.False(targets.Get(FakeConnector.Id, "owner/plain")!.CompleteAtSource);
        Assert.Equal("true", page.Find("[data-testid='task-connector-target-card-complete-at-source'] button[role='switch']").GetAttribute("aria-checked"));
    }

    [Fact]
    public async Task Sync_now_syncs_the_cards_own_target_and_says_what_it_did()
    {
        var (context, _, sync) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/other"), new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;
        sync.Outcome = new LinkedTaskSyncOutcome(true, "Synced owner/repo: 2 new.");

        var page = context.Render<TaskConnectorSettings>();
        var card = page.FindAll("[data-testid='task-connector-target-card']").Single(card => card.TextContent.Contains("owner/repo", StringComparison.Ordinal));
        await card.QuerySelector("[data-testid='task-connector-target-card-sync']")!.ClickAsync(new());

        Assert.Equal([(FakeConnector.Id, "owner/repo")], sync.Requests);
        var message = Assert.Single(page.FindAll("[data-testid='task-connector-target-card-sync-message']"));
        Assert.Equal("Synced owner/repo: 2 new.", message.TextContent);
        Assert.Equal("owner/repo", message.Closest(".connected-target")!.QuerySelector("[data-testid='task-connector-target-card-name']")!.TextContent);
    }

    /// <summary>A press on a second card while the first still syncs goes through —
    /// the sync queues it — and each card shows its own answer, so neither reads the
    /// other's.</summary>
    [Fact]
    public void Two_cards_synced_at_once_each_run_and_each_show_their_own_answer()
    {
        var (context, _, sync) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/a"), new ConnectedTarget(FakeConnector.Id, "owner/b"));
        using var _context = context;
        var a = new TaskCompletionSource<LinkedTaskSyncOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var b = new TaskCompletionSource<LinkedTaskSyncOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        sync.Answer = (_, target) => target == "owner/a" ? a.Task : b.Task;

        var page = context.Render<TaskConnectorSettings>();
        Card(page, "owner/a").QuerySelector("[data-testid='task-connector-target-card-sync']")!.Click();
        Card(page, "owner/b").QuerySelector("[data-testid='task-connector-target-card-sync']")!.Click();

        Assert.Equal([(FakeConnector.Id, "owner/a"), (FakeConnector.Id, "owner/b")], sync.Requests);
        Assert.Equal("true", SyncButton(page, "owner/a").GetAttribute("aria-busy"));
        Assert.Equal("true", SyncButton(page, "owner/b").GetAttribute("aria-busy"));

        b.SetResult(new LinkedTaskSyncOutcome(false, "Fake has no target named owner/b.", "linked_tasks.fetch_not_found"));
        page.WaitForAssertion(() => Assert.Contains("Fake has no target named owner/b.", Card(page, "owner/b").TextContent, StringComparison.Ordinal));
        Assert.Equal("true", SyncButton(page, "owner/a").GetAttribute("aria-busy"));
        Assert.Empty(Card(page, "owner/a").QuerySelectorAll("[data-testid='task-connector-target-card-sync-message']"));

        a.SetResult(new LinkedTaskSyncOutcome(true, "Synced owner/a: nothing changed."));
        page.WaitForAssertion(() => Assert.Contains("Synced owner/a: nothing changed.", Card(page, "owner/a").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("owner/a", Card(page, "owner/b").QuerySelector("[data-testid='task-connector-target-card-sync-message']")!.TextContent, StringComparison.Ordinal);

        static AngleSharp.Dom.IElement Card(IRenderedComponent<TaskConnectorSettings> page, string target) =>
            page.FindAll("[data-testid='task-connector-target-card']")
                .Single(card => card.QuerySelector("[data-testid='task-connector-target-card-name']")!.TextContent == target);

        static AngleSharp.Dom.IElement SyncButton(IRenderedComponent<TaskConnectorSettings> page, string target) =>
            Card(page, target).QuerySelector("[data-testid='task-connector-target-card-sync']")!;
    }

    /// <summary>The defect this answers: the page said "Synced." over a sync that
    /// had failed. It says the sync's own sentence instead, which names the
    /// target.</summary>
    [Fact]
    public async Task Sync_now_that_failed_says_why_rather_than_synced()
    {
        var (context, _, sync) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;
        sync.Outcome = new LinkedTaskSyncOutcome(false, "Fake has no target named owner/repo.", "linked_tasks.fetch_not_found");

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-sync']").ClickAsync(new());

        var message = page.Find("[data-testid='task-connector-target-card-sync-message']").TextContent;
        Assert.Equal("Fake has no target named owner/repo.", message);
        Assert.DoesNotContain("Synced", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_target_whose_last_sync_failed_says_why_on_its_card()
    {
        var failed = new ConnectedTarget(FakeConnector.Id, "owner/repo")
        {
            LastSyncError = "Fake has no target named owner/repo.",
            LastSyncFailedAt = new DateTimeOffset(2026, 10, 7, 20, 9, 17, TimeSpan.Zero),
        };
        var (context, _, _) = Compose(true, failed, new ConnectedTarget(FakeConnector.Id, "owner/fine"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        var error = Assert.Single(page.FindAll("[data-testid='task-connector-target-card-error']"));
        Assert.Contains("Fake has no target named owner/repo.", error.TextContent, StringComparison.Ordinal);
        Assert.Contains("owner/repo", error.Closest(".connected-target")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disconnect_removes_the_target()
    {
        var (context, targets, _) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-remove']").ClickAsync(new());

        Assert.Empty(targets.List());
        Assert.Contains("Disconnected owner/repo", page.Find("[data-testid='task-connectors-message']").TextContent);
    }

    // --- Disconnected sources -------------------------------------------------

    private static (BunitContext Context, InMemoryTargets Targets, FakeDisconnected Disconnected) ComposeDisconnected(
        params DisconnectedSource[] sources)
    {
        var (context, targets, _) = Compose();

        // The confirmation is a modal, which moves focus through JS interop.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var disconnected = new FakeDisconnected(targets, sources);
        context.Services.AddSingleton<IDisconnectedLinkedTasks>(disconnected);
        return (context, targets, disconnected);
    }

    /// <summary>A source whose connector this build no longer has is still listed,
    /// under its stored id, so its tasks can still be cleared.</summary>
    [Fact]
    public void Each_disconnected_source_is_listed_with_its_connector_target_and_task_count()
    {
        var (context, _, _) = ComposeDisconnected(
            new DisconnectedSource(FakeConnector.Id, "owner/old", 3, 0),
            new DisconnectedSource("gone", "acme/widgets", 1, 0));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.NotNull(page.Find("[data-testid='task-connectors-disconnected']"));
        Assert.Equal(["Fake", "gone"], page.FindAll("[data-testid='task-connector-disconnected-connector']").Select(e => e.TextContent));
        Assert.Equal(["owner/old", "acme/widgets"], page.FindAll("[data-testid='task-connector-disconnected-target']").Select(e => e.TextContent));
        Assert.Equal(["3 tasks", "1 task"], page.FindAll("[data-testid='task-connector-disconnected-count']").Select(e => e.TextContent));
        Assert.Equal(2, page.FindAll("[data-testid='task-connector-disconnected-delete']").Count);
    }

    [Fact]
    public void The_section_is_hidden_when_no_source_is_disconnected()
    {
        var (context, _, _) = ComposeDisconnected();
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Empty(page.FindAll("[data-testid='task-connectors-disconnected']"));
        Assert.DoesNotContain("Disconnected sources", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_is_hidden_in_a_host_without_the_port()
    {
        var (context, _, _) = Compose();
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Empty(page.FindAll("[data-testid='task-connectors-disconnected']"));
    }

    [Fact]
    public async Task Disconnecting_a_target_lists_it_as_a_disconnected_source()
    {
        var (context, targets, _) = ComposeDisconnected(new DisconnectedSource(FakeConnector.Id, "owner/repo", 2, 0));
        targets.Save(new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        Assert.Empty(page.FindAll("[data-testid='task-connectors-disconnected']"));

        await page.Find("[data-testid='task-connector-target-card-remove']").ClickAsync(new());

        page.WaitForAssertion(() =>
            Assert.Equal("owner/repo", page.Find("[data-testid='task-connector-disconnected-target']").TextContent));
    }

    /// <summary>A sync records its progress on the target every run; that moves no
    /// source on or off the list, so it is not listed again. Connecting one
    /// elsewhere does.</summary>
    [Fact]
    public void Only_a_change_to_which_targets_are_connected_lists_the_sources_again()
    {
        var (context, targets, disconnected) = ComposeDisconnected(new DisconnectedSource(FakeConnector.Id, "owner/old", 3, 0));
        targets.Save(new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        var listed = disconnected.Lists;

        targets.Update(FakeConnector.Id, "OWNER/REPO", target => target with { LastSyncedAt = DateTimeOffset.UnixEpoch });
        page.WaitForAssertion(() => Assert.Equal(listed, disconnected.Lists));

        targets.Save(new ConnectedTarget(FakeConnector.Id, "owner/old"));
        page.WaitForAssertion(() =>
        {
            Assert.Equal(listed + 1, disconnected.Lists);
            Assert.Empty(page.FindAll("[data-testid='task-connectors-disconnected']"));
        });
    }

    [Fact]
    public async Task Cancelling_the_confirmation_deletes_nothing()
    {
        var (context, _, disconnected) = ComposeDisconnected(new DisconnectedSource(FakeConnector.Id, "owner/old", 3, 0));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-disconnected-delete']").ClickAsync(new());
        Assert.NotNull(page.Find("[data-testid='task-connector-delete-dialog']"));

        await page.Find("[data-testid='task-connector-delete-cancel']").ClickAsync(new());

        Assert.Empty(page.FindAll("[data-testid='task-connector-delete-dialog']"));
        Assert.Empty(disconnected.Deleted);
        Assert.Single(page.FindAll("[data-testid='task-connector-disconnected']"));
    }

    [Fact]
    public async Task The_confirmation_says_what_is_deleted_what_stays_and_that_connecting_again_brings_them_back()
    {
        var (context, _, _) = ComposeDisconnected(new DisconnectedSource(FakeConnector.Id, "owner/old", 3, 0));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-disconnected-delete']").ClickAsync(new());

        var dialog = page.Find("[data-testid='task-connector-delete-dialog']");
        var message = dialog.QuerySelector(".confirm-dialog__message")!.TextContent;
        Assert.Contains("This deletes 3 tasks from owner/old.", message, StringComparison.Ordinal);
        Assert.Contains("The items at the source stay untouched", message, StringComparison.Ordinal);
        Assert.Contains("the deletion reaches your other devices.", message, StringComparison.Ordinal);
        Assert.Contains("Connecting owner/old again brings these tasks back.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("will not bring", message, StringComparison.Ordinal);
        Assert.DoesNotContain("blocked", message, StringComparison.Ordinal);

        // Destructive: the confirm is the danger button, and it names the act.
        var confirm = page.Find("[data-testid='task-connector-delete-confirm']");
        Assert.Contains("btn--danger", confirm.ClassName, StringComparison.Ordinal);
        Assert.Equal("Delete tasks", confirm.TextContent.Trim());
    }

    [Theory]
    [InlineData(1, "1 task that waits on these will show as blocked on a missing task.")]
    [InlineData(2, "2 tasks that wait on these will show as blocked on a missing task.")]
    public async Task The_confirmation_warns_about_the_tasks_left_waiting_on_a_missing_one(int dependents, string expected)
    {
        var (context, _, _) = ComposeDisconnected(new DisconnectedSource(FakeConnector.Id, "owner/old", 3, dependents));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-disconnected-delete']").ClickAsync(new());

        Assert.EndsWith(expected, page.Find(".confirm-dialog__message").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirming_deletes_the_tasks_says_how_many_and_drops_the_source_from_the_list()
    {
        var (context, _, disconnected) = ComposeDisconnected(
            new DisconnectedSource(FakeConnector.Id, "owner/old", 3, 0),
            new DisconnectedSource(FakeConnector.Id, "owner/older", 1, 0));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.FindAll("[data-testid='task-connector-disconnected-delete']")[0].ClickAsync(new());
        await page.Find("[data-testid='task-connector-delete-confirm']").ClickAsync(new());

        Assert.Equal([(FakeConnector.Id, "owner/old")], disconnected.Deleted);
        page.WaitForAssertion(() =>
        {
            Assert.Equal("Deleted 3 tasks from owner/old.", page.Find("[data-testid='task-connectors-message']").TextContent.Trim());
            Assert.Equal("owner/older", page.Find("[data-testid='task-connector-disconnected-target']").TextContent);
        });
    }

    [Fact]
    public async Task A_source_connected_again_before_the_confirmation_is_refused_in_words()
    {
        var (context, targets, disconnected) = ComposeDisconnected(new DisconnectedSource(FakeConnector.Id, "owner/old", 3, 0));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-disconnected-delete']").ClickAsync(new());

        // Connected elsewhere while the question is open.
        targets.Save(new ConnectedTarget(FakeConnector.Id, "owner/old"));
        await page.Find("[data-testid='task-connector-delete-confirm']").ClickAsync(new());

        Assert.Empty(disconnected.Deleted);
        page.WaitForAssertion(() =>
        {
            Assert.Equal(
                "owner/old is connected again, so its tasks were not deleted.",
                page.Find("[data-testid='task-connectors-message']").TextContent.Trim());
            Assert.Empty(page.FindAll("[data-testid='task-connectors-disconnected']"));
        });
        Assert.NotNull(targets.Get(FakeConnector.Id, "owner/old"));
    }

    /// <summary>The field a target is typed into is named by the chosen connector:
    /// a connector that says nothing gets a neutral label and no example, and one
    /// whose targets are products asks for a product's slug, not an
    /// <c>owner/repository</c> it would answer with a 404.</summary>
    [Fact]
    public async Task The_target_field_is_named_by_the_chosen_connector()
    {
        var context = new BunitContext();
        using var _context = context;
        context.Services.AddSingleton(new LinkedTaskSources([new FakeConnector(), new ProductConnector()], new InMemoryTargets()));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());

        var page = context.Render<TaskConnectorSettings>();

        Assert.Equal(TaskConnectorDescriptor.DefaultTargetLabel, page.Find("label[for='task-connector-target']").TextContent);
        Assert.Null(page.Find("input#task-connector-target").GetAttribute("placeholder"));

        await page.Find("select#task-connector-choice").ChangeAsync(new() { Value = ProductConnector.Id });

        Assert.Equal("Product", page.Find("label[for='task-connector-target']").TextContent);
        Assert.Equal("product-slug", page.Find("input#task-connector-target").GetAttribute("placeholder"));
        Assert.Contains("spec-manager URL", page.Find("[data-testid='task-connector-target']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_target_whose_connector_is_not_installed_stays_listed_under_its_id()
    {
        var (context, _, _) = Compose(true, new ConnectedTarget("jira", "PROJ"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Contains("jira", page.Find(".connected-target__connector").TextContent);
    }

    // --- Restoring deleted tasks ------------------------------------------------

    private static (BunitContext Context, InMemoryTargets Targets, FakeDeleted Deleted) ComposeDeleted(
        ConnectedTarget[] connected,
        params DeletedLinkedSource[] sources)
    {
        var (context, targets, _) = Compose(true, connected);

        // The confirmation is a modal, which moves focus through JS interop.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var deleted = new FakeDeleted(targets, sources);
        context.Services.AddSingleton<IDeletedLinkedTasks>(deleted);
        return (context, targets, deleted);
    }

    /// <summary>Offered on a connected target's own card, and only on one with
    /// deleted tasks: the pair is matched the way a target is, without regard to
    /// case, and a source with deleted tasks that is not connected gets nothing
    /// here.</summary>
    [Fact]
    public void Only_a_connected_target_with_deleted_tasks_offers_to_restore_them()
    {
        var (context, _, _) = ComposeDeleted(
            [new ConnectedTarget(FakeConnector.Id, "owner/one"), new ConnectedTarget(FakeConnector.Id, "owner/two")],
            new DeletedLinkedSource(FakeConnector.Id, "OWNER/TWO", 34),
            new DeletedLinkedSource(FakeConnector.Id, "owner/gone", 2));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        var restore = Assert.Single(page.FindAll("[data-testid='task-connector-target-card-restore']"));
        Assert.Equal("Fake owner/two", restore.Closest("[data-testid='task-connector-target-card']")!.GetAttribute("aria-label"));
    }

    [Fact]
    public void Nothing_offers_a_restore_in_a_host_without_the_port()
    {
        var (context, _, _) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/one"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Empty(page.FindAll("[data-testid='task-connector-target-card-restore']"));
    }

    [Fact]
    public async Task The_restore_confirmation_names_the_count_and_says_it_includes_tasks_deleted_one_by_one()
    {
        var (context, _, _) = ComposeDeleted(
            [new ConnectedTarget(FakeConnector.Id, "owner/repo")],
            new DeletedLinkedSource(FakeConnector.Id, "owner/repo", 34));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-restore']").ClickAsync(new());

        var dialog = page.Find("[data-testid='task-connector-restore-dialog']");
        var message = dialog.QuerySelector(".confirm-dialog__message")!.TextContent;
        Assert.Contains("This restores 34 tasks deleted from owner/repo", message, StringComparison.Ordinal);
        Assert.Contains("including tasks you deleted one by one", message, StringComparison.Ordinal);
        Assert.Contains("the next sync", message, StringComparison.Ordinal);

        // Not destructive: it brings work back rather than taking it away.
        var confirm = page.Find("[data-testid='task-connector-restore-confirm']");
        Assert.DoesNotContain("btn--danger", confirm.ClassName, StringComparison.Ordinal);
        Assert.Equal("Restore tasks", confirm.TextContent.Trim());
    }

    [Fact]
    public async Task Cancelling_the_restore_confirmation_restores_nothing()
    {
        var (context, _, deleted) = ComposeDeleted(
            [new ConnectedTarget(FakeConnector.Id, "owner/repo")],
            new DeletedLinkedSource(FakeConnector.Id, "owner/repo", 34));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-restore']").ClickAsync(new());
        await page.Find("[data-testid='task-connector-restore-cancel']").ClickAsync(new());

        Assert.Empty(page.FindAll("[data-testid='task-connector-restore-dialog']"));
        Assert.Empty(deleted.Restored);
        Assert.Single(page.FindAll("[data-testid='task-connector-target-card-restore']"));
    }

    [Fact]
    public async Task Confirming_restores_the_tasks_says_how_many_and_takes_the_offer_away()
    {
        var (context, _, deleted) = ComposeDeleted(
            [new ConnectedTarget(FakeConnector.Id, "owner/repo")],
            new DeletedLinkedSource(FakeConnector.Id, "owner/repo", 34));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-restore']").ClickAsync(new());
        await page.Find("[data-testid='task-connector-restore-confirm']").ClickAsync(new());

        Assert.Equal([(FakeConnector.Id, "owner/repo")], deleted.Restored);
        page.WaitForAssertion(() =>
        {
            Assert.Equal(
                "Restored 34 tasks from owner/repo. The next sync brings them up to date.",
                page.Find("[data-testid='task-connectors-message']").TextContent.Trim());
            Assert.Empty(page.FindAll("[data-testid='task-connector-target-card-restore']"));
        });
    }

    /// <summary>One task reads as one throughout: the confirmation, the line that
    /// says how it went, and what comes back.</summary>
    [Fact]
    public async Task Restoring_one_task_says_so_in_the_singular()
    {
        var (context, _, _) = ComposeDeleted(
            [new ConnectedTarget(FakeConnector.Id, "owner/repo")],
            new DeletedLinkedSource(FakeConnector.Id, "owner/repo", 1));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-restore']").ClickAsync(new());

        Assert.Equal(
            "This restores 1 task deleted from owner/repo, even if you deleted it on its own. "
            + "It comes back as it was, the next sync brings it up to date with the source, and the restore reaches your other devices.",
            page.Find(".confirm-dialog__message").TextContent.Trim());

        await page.Find("[data-testid='task-connector-restore-confirm']").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal(
            "Restored 1 task from owner/repo. The next sync brings it up to date.",
            page.Find("[data-testid='task-connectors-message']").TextContent.Trim()));
    }

    /// <summary>Deleting one task reads as one too, and so does the warning about
    /// the tasks left waiting on it.</summary>
    [Fact]
    public async Task Deleting_one_task_says_so_in_the_singular()
    {
        var (context, _, _) = ComposeDisconnected(new DisconnectedSource(FakeConnector.Id, "owner/old", 1, 2));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-disconnected-delete']").ClickAsync(new());

        Assert.Equal(
            "This deletes 1 task from owner/old. The item at the source stays untouched, and the deletion reaches your other devices. "
            + "Connecting owner/old again brings this task back. 2 tasks that wait on it will show as blocked on a missing task.",
            page.Find(".confirm-dialog__message").TextContent.Trim());
    }

    /// <summary>A refusal — the target disconnected meanwhile — is said in the
    /// module's own words rather than as a count.</summary>
    [Fact]
    public async Task A_target_disconnected_before_the_confirmation_is_refused_in_words()
    {
        var (context, targets, deleted) = ComposeDeleted(
            [new ConnectedTarget(FakeConnector.Id, "owner/repo")],
            new DeletedLinkedSource(FakeConnector.Id, "owner/repo", 34));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-restore']").ClickAsync(new());

        // Disconnected elsewhere while the question is open.
        targets.Remove(FakeConnector.Id, "owner/repo");
        await page.Find("[data-testid='task-connector-restore-confirm']").ClickAsync(new());

        Assert.Empty(deleted.Restored);
        page.WaitForAssertion(() => Assert.Equal(
            "owner/repo is no longer connected, so its tasks were not restored.",
            page.Find("[data-testid='task-connectors-message']").TextContent.Trim()));
    }

    // --- Picking a target ---------------------------------------------------------

    private static (BunitContext Context, InMemoryTargets Targets) ComposeChoosing(ITaskConnector connector, params ConnectedTarget[] targets)
    {
        var context = new BunitContext();
        var store = new InMemoryTargets(targets);
        context.Services.AddSingleton(new LinkedTaskSources([connector], store));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        return (context, store);
    }

    /// <summary>The defect this answers: a spec-manager product typed by its name
    /// 404s, because the source takes only its slug. A connector that can list its
    /// targets is picked from — the name shown, the target stored — and one already
    /// connected is not offered again.</summary>
    [Fact]
    public async Task A_connector_that_lists_its_targets_is_picked_from_by_name_and_stores_the_target()
    {
        var connector = new ChoosingConnector(new ConnectorTargetChoices(
            [new("fincent", "Fincent"), new("backlog-demo", "Backlog demo")]));
        var (context, targets) = ComposeChoosing(connector, new ConnectedTarget(ChoosingConnector.Id, "backlog-demo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Empty(page.FindAll("input#task-connector-target"));
        var options = page.FindAll("select#task-connector-target option").Where(option => option.GetAttribute("value") != string.Empty).ToList();
        Assert.Equal(["fincent"], options.Select(option => option.GetAttribute("value")));
        Assert.Equal(["Fincent"], options.Select(option => option.TextContent));
        Assert.Equal("Product", page.Find("label[for='task-connector-target']").TextContent);
        Assert.True(page.Find("[data-testid='task-connector-connect']").HasAttribute("disabled"));

        await page.Find("select#task-connector-target").ChangeAsync(new() { Value = "fincent" });
        Assert.False(page.Find("[data-testid='task-connector-connect']").HasAttribute("disabled"));
        await page.Find("[data-testid='task-connector-connect']").ClickAsync(new());

        Assert.NotNull(targets.Get(ChoosingConnector.Id, "fincent"));
        Assert.Equal(2, targets.List().Count);
        Assert.DoesNotContain(page.FindAll("select#task-connector-target option"), option => option.GetAttribute("value") == "fincent");
    }

    /// <summary>A connector that cannot list today — spec-manager refuses the app's
    /// token on its product list — falls back to the typed field, and the field
    /// says why and what to type.</summary>
    [Fact]
    public void A_connector_that_cannot_list_falls_back_to_typing_and_says_why()
    {
        var connector = new ChoosingConnector(ConnectorTargetChoices.Unavailable(
            "spec-manager would not list your products. Type the product's slug instead, as it appears in its spec-manager URL."));
        var (context, _) = ComposeChoosing(connector);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.NotNull(page.Find("input#task-connector-target"));
        Assert.Empty(page.FindAll("select#task-connector-target"));
        Assert.Contains("would not list your products", page.Find("[data-testid='task-connector-target']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connector_that_throws_while_listing_falls_back_to_typing_and_the_page_still_works()
    {
        var connector = new ChoosingConnector(new InvalidOperationException("boom"));
        var (context, _) = ComposeChoosing(connector);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.NotNull(page.Find("input#task-connector-target"));
        Assert.Contains("could not list", page.Find("[data-testid='task-connector-target']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void While_the_choices_load_the_picker_says_so_and_offers_nothing()
    {
        var pending = new TaskCompletionSource<ConnectorTargetChoices>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connector = new ChoosingConnector(pending.Task);
        var (context, _) = ComposeChoosing(connector);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        var loading = page.Find("select#task-connector-target");
        Assert.True(loading.HasAttribute("disabled"));
        Assert.Contains("Loading", loading.TextContent, StringComparison.Ordinal);
        Assert.True(page.Find("[data-testid='task-connector-connect']").HasAttribute("disabled"));

        pending.SetResult(new ConnectorTargetChoices([new("fincent", "Fincent")]));

        page.WaitForAssertion(() => Assert.False(page.Find("select#task-connector-target").HasAttribute("disabled")));
    }

    /// <summary>Each connector's choices are its own: switching the connector loads
    /// the new one's list, and a connector that lists nothing is typed into.</summary>
    [Fact]
    public async Task Switching_the_connector_loads_that_connectors_choices()
    {
        var context = new BunitContext();
        using var _context = context;
        var choosing = new ChoosingConnector(new ConnectorTargetChoices([new("fincent", "Fincent")]));
        context.Services.AddSingleton(new LinkedTaskSources([new FakeConnector(), choosing], new InMemoryTargets()));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());

        // Connectors are listed by name, so Choosing is the one chosen first.
        var page = context.Render<TaskConnectorSettings>();
        Assert.NotNull(page.Find("select#task-connector-target option[value='fincent']"));

        await page.Find("select#task-connector-choice").ChangeAsync(new() { Value = FakeConnector.Id });
        page.WaitForAssertion(() => Assert.NotNull(page.Find("input#task-connector-target")));

        await page.Find("select#task-connector-choice").ChangeAsync(new() { Value = ChoosingConnector.Id });
        page.WaitForAssertion(() => Assert.NotNull(page.Find("select#task-connector-target option[value='fincent']")));
    }

    /// <summary>The defect the review found: a product name typed while signed out
    /// survived the sign-in that brought the picker, and Connect stayed enabled
    /// with it — saving the very name that 404s. A typed value the picker does not
    /// offer is dropped, and Connect waits for a pick.</summary>
    [Fact]
    public async Task A_name_typed_before_signing_in_is_dropped_when_the_picker_arrives()
    {
        var connector = new SigningChoosingConnector(new ConnectorTargetChoices([new("fincent-bv", "Fincent")]));
        var (context, targets) = ComposeChoosing(connector);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("input#task-connector-target").InputAsync(new() { Value = "fincent" });
        Assert.False(page.Find("[data-testid='task-connector-connect']").HasAttribute("disabled"));

        page.Find($"[data-testid='task-connector-sign-in-{SigningChoosingConnector.Id}-sign-in']").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("select#task-connector-target option[value='fincent-bv']")));
        Assert.True(page.Find("[data-testid='task-connector-connect']").HasAttribute("disabled"));

        await page.Find("select#task-connector-target").ChangeAsync(new() { Value = "fincent-bv" });
        await page.Find("[data-testid='task-connector-connect']").ClickAsync(new());

        Assert.NotNull(targets.Get(SigningChoosingConnector.Id, "fincent-bv"));
        Assert.Null(targets.Get(SigningChoosingConnector.Id, "fincent"));
    }

    /// <summary>A sign-in the connector reports on its own — one that finished after
    /// the page stopped waiting — brings the picker too.</summary>
    [Fact]
    public void An_account_change_the_connector_raises_on_its_own_reloads_the_choices()
    {
        var connector = new SigningChoosingConnector(new ConnectorTargetChoices([new("fincent-bv", "Fincent")]));
        var (context, _) = ComposeChoosing(connector);
        using var _context = context;
        var page = context.Render<TaskConnectorSettings>();
        Assert.NotNull(page.Find("input#task-connector-target"));

        connector.Account = new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch);
        connector.RaiseAccountChanged();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("select#task-connector-target option[value='fincent-bv']")));
    }

    /// <summary>An answer for a connector the person has since left is not shown
    /// under the one they chose.</summary>
    [Fact]
    public async Task Choices_that_arrive_after_the_connector_was_switched_are_dropped()
    {
        var context = new BunitContext();
        using var _context = context;
        var pending = new TaskCompletionSource<ConnectorTargetChoices>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Services.AddSingleton(new LinkedTaskSources([new FakeConnector(), new ChoosingConnector(pending.Task)], new InMemoryTargets()));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("select#task-connector-choice").ChangeAsync(new() { Value = FakeConnector.Id });
        Assert.NotNull(page.Find("input#task-connector-target"));

        pending.SetResult(new ConnectorTargetChoices([new("fincent", "Fincent")]));

        await Task.Delay(50, TestContext.Current.CancellationToken);
        page.Render();
        Assert.NotNull(page.Find("input#task-connector-target"));
        Assert.Empty(page.FindAll("select#task-connector-target"));
    }

    /// <summary>Only the latest request for the same connector lands: an older,
    /// slower answer arriving after it does not replace it.</summary>
    [Fact]
    public async Task An_older_answer_for_the_same_connector_does_not_replace_a_newer_one()
    {
        var context = new BunitContext();
        using var _context = context;
        var first = new TaskCompletionSource<ConnectorTargetChoices>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connector = new ChoosingConnector(call => call == 0
            ? first.Task
            : Task.FromResult(new ConnectorTargetChoices([new("newer", "Newer")])));
        context.Services.AddSingleton(new LinkedTaskSources([new FakeConnector(), connector], new InMemoryTargets()));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("select#task-connector-choice").ChangeAsync(new() { Value = FakeConnector.Id });
        await page.Find("select#task-connector-choice").ChangeAsync(new() { Value = ChoosingConnector.Id });
        page.WaitForAssertion(() => Assert.NotNull(page.Find("select#task-connector-target option[value='newer']")));

        first.SetResult(new ConnectorTargetChoices([new("older", "Older")]));

        await Task.Delay(50, TestContext.Current.CancellationToken);
        page.Render();
        Assert.NotNull(page.Find("select#task-connector-target option[value='newer']"));
        Assert.Empty(page.FindAll("select#task-connector-target option[value='older']"));
    }

    /// <summary>A connector whose targets are picked, answering what the test
    /// gives it, or throwing.</summary>
    private sealed class ChoosingConnector : ITaskConnector
    {
        public const string Id = "choosing";

        private readonly Func<int, Task<ConnectorTargetChoices>> _choices;
        private int _calls;

        public ChoosingConnector(ConnectorTargetChoices choices) => _choices = _ => Task.FromResult(choices);

        public ChoosingConnector(Exception failure) => _choices = _ => Task.FromException<ConnectorTargetChoices>(failure);

        public ChoosingConnector(Task<ConnectorTargetChoices> pending) => _choices = _ => pending;

        /// <summary>Answers each call by its number, from zero.</summary>
        public ChoosingConnector(Func<int, Task<ConnectorTargetChoices>> byCall) => _choices = byCall;

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Choosing", "choosing", "color-primary")
        {
            TargetLabel = "Product",
            TargetPlaceholder = "product-slug",
        };

        public TaskConnectorCapabilities Capabilities { get; init; } = new();

        public Task<ConnectorTargetChoices> ListTargetChoicesAsync(CancellationToken cancellationToken) => _choices(_calls++);

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);
    }

    /// <summary>A connector a person signs in to that lists its targets only once
    /// signed in, the way spec-manager does. Its sign-in succeeds at once.</summary>
    private sealed class SigningChoosingConnector(ConnectorTargetChoices signedIn) : ITaskConnector, ITaskConnectorSignIn
    {
        public const string Id = "signing-choosing";

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Signing", "signing", "color-primary")
        {
            TargetLabel = "Product",
        };

        public TaskConnectorCapabilities Capabilities { get; } = new();

        public TaskConnectorAccount? Account { get; set; }

        public event Action? AccountChanged;

        public Task<ConnectorTargetChoices> ListTargetChoicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Account is null ? ConnectorTargetChoices.Unavailable("Sign in to pick a product.") : signedIn);

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);

        public Task<string?> SignInAsync(CancellationToken cancellationToken)
        {
            Account = new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch);
            AccountChanged?.Invoke();
            return Task.FromResult<string?>(null);
        }

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            Account = null;
            AccountChanged?.Invoke();
            return Task.CompletedTask;
        }

        public void RaiseAccountChanged() => AccountChanged?.Invoke();
    }

    // --- Signing in ------------------------------------------------------------

    private static BunitContext ComposeSigning(SigningConnector connector, bool withPlain = false)
    {
        var context = new BunitContext();
        ITaskConnector[] connectors = withPlain ? [new FakeConnector(), connector] : [connector];
        context.Services.AddSingleton(new LinkedTaskSources(connectors, new InMemoryTargets()));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        return context;
    }

    [Fact]
    public void Only_a_connector_that_signs_in_gets_a_sign_in_line()
    {
        using var context = ComposeSigning(new SigningConnector(), withPlain: true);

        var page = context.Render<TaskConnectorSettings>();

        var line = Assert.Single(page.FindAll(".connector-sign-in"));
        Assert.Equal("Tracker", line.QuerySelector(".connector-sign-in__name")!.TextContent);
        Assert.Equal("Not signed in", page.Find(Part("account")).TextContent.Trim());
    }

    [Fact]
    public void With_no_connector_that_signs_in_there_is_no_sign_in_block()
    {
        var (context, _, _) = Compose();
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Empty(page.FindAll("[data-testid='task-connectors-sign-ins']"));
    }

    [Fact]
    public void Sign_in_is_busy_while_it_runs_and_then_names_the_account()
    {
        var connector = new SigningConnector();
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();

        page.Find(Part("sign-in")).Click();

        Assert.Equal("true", page.Find(Part("sign-in")).GetAttribute("aria-busy"));

        connector.CompleteSignIn(new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch), error: null);

        page.WaitForAssertion(() => Assert.Equal("Signed in as Sam", page.Find(Part("account")).TextContent.Trim()));
        Assert.NotNull(page.Find(Part("sign-out")));
    }

    [Fact]
    public void A_sign_in_that_did_not_work_shows_the_connector_s_own_words()
    {
        var connector = new SigningConnector();
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();

        page.Find(Part("sign-in")).Click();
        connector.CompleteSignIn(account: null, error: "The window was closed before it finished.");

        page.WaitForAssertion(() =>
            Assert.Equal("The window was closed before it finished.", page.Find(Part("error")).TextContent));
        Assert.Null(page.Find(Part("sign-in")).GetAttribute("aria-busy"));
    }

    [Fact]
    public async Task Leaving_the_page_lets_a_sign_in_under_way_finish_and_coming_back_shows_it()
    {
        // The browser is still open on the sign-in, waiting to redirect to the
        // connector's loopback listener: cancelling here would send it to a dead port.
        var connector = new SigningConnector();
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();
        page.Find(Part("sign-in")).Click();

        await context.DisposeComponentsAsync();

        Assert.False(connector.SignInToken.CanBeCanceled);

        connector.CompleteSignIn(new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch), error: null);
        await connector.SignInFinished;

        var back = context.Render<TaskConnectorSettings>();
        Assert.Equal("Signed in as Sam", back.Find(Part("account")).TextContent.Trim());
    }

    [Fact]
    public void Sign_out_forgets_the_account()
    {
        var connector = new SigningConnector { Account = new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch) };
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();

        page.Find(Part("sign-out")).Click();

        page.WaitForAssertion(() => Assert.Equal("Not signed in", page.Find(Part("account")).TextContent.Trim()));
        Assert.Equal(1, connector.SignOuts);
    }

    [Fact]
    public void An_account_change_the_connector_raises_on_its_own_re_renders_the_page()
    {
        var connector = new SigningConnector();
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();

        connector.Account = new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch);
        connector.RaiseAccountChanged();

        page.WaitForAssertion(() => Assert.Equal("Signed in as Sam", page.Find(Part("account")).TextContent.Trim()));
    }

    /// <summary>What a connector asks of the person beside its sign-in — an answer waiting to
    /// be sent — is said on its line, and goes when the connector stops asking.</summary>
    [Fact]
    public void A_notice_the_connector_raises_is_said_on_its_sign_in_line()
    {
        var connector = new SigningConnector();
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();

        connector.Notice = "Sign in to spec-manager to send answers.";
        connector.RaiseAccountChanged();

        page.WaitForAssertion(() => Assert.Equal("Sign in to spec-manager to send answers.", page.Find(Part("error")).TextContent));

        connector.Notice = null;
        connector.RaiseAccountChanged();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(Part("error"))));
    }

    private static string Part(string part) => $"[data-testid='task-connector-sign-in-{SigningConnector.Id}-{part}']";

    /// <summary>A connector a person signs in to, whose sign-in waits for the test
    /// to finish it.</summary>
    private sealed class SigningConnector : ITaskConnector, ITaskConnectorSignIn
    {
        public const string Id = "tracker";

        private TaskCompletionSource<string?>? _signIn;
        private TaskConnectorAccount? _pending;

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Tracker", "tracker", "color-primary");

        public TaskConnectorCapabilities Capabilities { get; } = new();

        public TaskConnectorAccount? Account { get; set; }

        public string? Notice { get; set; }

        public event Action? AccountChanged;

        public CancellationToken SignInToken { get; private set; }

        public int SignOuts { get; private set; }

        /// <summary>The last sign-in, completed once it has applied its account and
        /// raised <see cref="AccountChanged"/>.</summary>
        public Task<string?> SignInFinished { get; private set; } = Task.FromResult<string?>(null);

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);

        public Task<string?> SignInAsync(CancellationToken cancellationToken)
        {
            SignInToken = cancellationToken;
            _signIn = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            SignInFinished = RunSignInAsync(_signIn, cancellationToken);
            return SignInFinished;
        }

        private async Task<string?> RunSignInAsync(TaskCompletionSource<string?> signIn, CancellationToken cancellationToken)
        {
            await using var registration = cancellationToken.Register(() => signIn.TrySetCanceled(cancellationToken));

            var error = await signIn.Task;
            if (error is null)
            {
                Account = _pending;
                AccountChanged?.Invoke();
            }

            return error;
        }

        public void CompleteSignIn(TaskConnectorAccount? account, string? error)
        {
            _pending = account;
            _signIn!.SetResult(error);
        }

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            SignOuts++;
            Account = null;
            AccountChanged?.Invoke();
            return Task.CompletedTask;
        }

        public void RaiseAccountChanged() => AccountChanged?.Invoke();
    }

    private static readonly IReadOnlyList<SourceStatus> ProductStatuses =
    [
        new("Idee", NormalisedSourceState.Open),
        new("Verfijnd", NormalisedSourceState.Active),
        new("Gereed", NormalisedSourceState.Done),
    ];

    private static (BunitContext Context, InMemoryTargets Targets) ComposeListing(ListingConnector connector, params ConnectedTarget[] targets)
    {
        var context = new BunitContext();
        var store = new InMemoryTargets(targets);
        context.Services.AddSingleton(new LinkedTaskSources([connector], store));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        context.Services.AddSingleton<IRepositoryDirectory>(new TwoRepositories());
        return (context, store);
    }

    /// <summary>The registry's repositories are offered by alias and stored by
    /// <c>owner/name</c>; the empty choice, None for a product, clears it.</summary>
    [Fact]
    public async Task Picking_a_repository_stores_it_on_the_target_and_none_clears_it()
    {
        var (context, targets) = ComposeListing(new ListingConnector(_ => Task.FromResult(ProductStatuses)), new ConnectedTarget(ListingConnector.Id, "backlog"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        Assert.Equal(
            ["None", "backlog", "marketplace"],
            page.FindAll("[data-testid='task-connector-target-card-repository'] select option").Select(option => option.TextContent));

        await page.Find("[data-testid='task-connector-target-card-repository'] select").ChangeAsync(new() { Value = "JSdotNet/Marketplace" });
        Assert.Equal("JSdotNet/Marketplace", targets.Get(ListingConnector.Id, "backlog")!.RepositoryId);
        Assert.Equal("JSdotNet/Marketplace", page.Find("[data-testid='task-connector-target-card-repository'] select").GetAttribute("value"));

        await page.Find("[data-testid='task-connector-target-card-repository'] select").ChangeAsync(new() { Value = string.Empty });
        Assert.Null(targets.Get(ListingConnector.Id, "backlog")!.RepositoryId);
    }

    [Fact]
    public void A_repository_targets_empty_choice_is_its_own_repository()
    {
        var (context, _) = ComposeListing(
            new ListingConnector(_ => Task.FromResult<IReadOnlyList<SourceStatus>>([]), targetIsRepository: true),
            new ConnectedTarget(ListingConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Equal("Its own repository (owner/repo)", page.FindAll("[data-testid='task-connector-target-card-repository'] select option")[0].TextContent);
    }

    /// <summary>One row per status the source lists, its empty choice the default its
    /// state gives; a row mapped is stored under the status's name, and Default
    /// removes it again.</summary>
    [Fact]
    public async Task Each_status_the_source_lists_is_a_row_and_mapping_one_stores_it()
    {
        var connector = new ListingConnector(_ => Task.FromResult(ProductStatuses));
        var (context, targets) = ComposeListing(connector, new ConnectedTarget(ListingConnector.Id, "backlog"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='task-connector-target-card-statuses']")));

        Assert.Equal(["backlog"], connector.Asked);
        Assert.Equal(
            ["Idee", "Verfijnd", "Gereed"],
            page.FindAll("[data-testid='task-connector-target-card-statuses'] label.field__label").Select(label => label.TextContent));
        Assert.Equal(
            ["Default (Ready)", "Default (In progress)", "Default (Done)"],
            Enumerable.Range(0, 3).Select(index => page.FindAll($"[data-testid='task-connector-target-card-status-{index}'] select option")[0].TextContent));
        Assert.Equal(
            ["Draft", "Ready", "In progress", "Done", "Archived"],
            page.FindAll("[data-testid='task-connector-target-card-status-1'] select option").Skip(1).Select(option => option.TextContent));

        await page.Find("[data-testid='task-connector-target-card-status-1'] select").ChangeAsync(new() { Value = "Ready" });
        var stored = targets.Get(ListingConnector.Id, "backlog")!;
        var mapping = Assert.Single(stored.StatusMap);
        Assert.Equal("Verfijnd", mapping.Key);
        Assert.Equal(EntryStatus.Ready, mapping.Value);
        Assert.Equal("Ready", page.Find("[data-testid='task-connector-target-card-status-1'] select").GetAttribute("value"));

        await page.Find("[data-testid='task-connector-target-card-status-1'] select").ChangeAsync(new() { Value = string.Empty });
        Assert.Empty(targets.Get(ListingConnector.Id, "backlog")!.StatusMap);
    }

    /// <summary>A mapping stored for a status the source no longer lists is still a
    /// row, after the listed ones, so it can be cleared.</summary>
    [Fact]
    public async Task A_stored_mapping_the_source_no_longer_lists_is_still_a_row_to_clear()
    {
        var target = new ConnectedTarget(ListingConnector.Id, "backlog")
        {
            StatusMap = new Dictionary<string, EntryStatus> { ["Oud"] = EntryStatus.Done, ["Idee"] = EntryStatus.Draft },
        };
        var (context, targets) = ComposeListing(
            new ListingConnector(_ => Task.FromResult<IReadOnlyList<SourceStatus>>([new("Idee", NormalisedSourceState.Open)])),
            target);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Equal(
            ["Idee", "Oud"],
            page.FindAll("[data-testid='task-connector-target-card-statuses'] label.field__label").Select(label => label.TextContent));
        Assert.Equal("Draft", page.Find("[data-testid='task-connector-target-card-status-0'] select").GetAttribute("value"));
        Assert.Equal("Done", page.Find("[data-testid='task-connector-target-card-status-1'] select").GetAttribute("value"));
        Assert.Equal("Default (no longer listed)", page.FindAll("[data-testid='task-connector-target-card-status-1'] select option")[0].TextContent);

        await page.Find("[data-testid='task-connector-target-card-status-1'] select").ChangeAsync(new() { Value = string.Empty });

        var stored = targets.Get(ListingConnector.Id, "backlog")!;
        Assert.Equal(EntryStatus.Draft, Assert.Single(stored.StatusMap).Value);
        Assert.Equal(["Idee"], page.FindAll("[data-testid='task-connector-target-card-statuses'] label.field__label").Select(label => label.TextContent));
    }

    [Fact]
    public void A_source_that_lists_no_status_shows_no_statuses_group()
    {
        var (context, _, _) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.NotNull(page.Find("[data-testid='task-connector-target-card']"));
        Assert.Empty(page.FindAll("[data-testid='task-connector-target-card-statuses']"));
    }

    /// <summary>A connector that fails to list is answered as one that lists none:
    /// no group, no error on the page, and the rest of the card as it was.</summary>
    [Fact]
    public void A_source_whose_listing_throws_shows_no_group_and_no_error()
    {
        var (context, _) = ComposeListing(
            new ListingConnector(_ => Task.FromException<IReadOnlyList<SourceStatus>>(new InvalidOperationException("The source is down."))),
            new ConnectedTarget(ListingConnector.Id, "backlog"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.NotNull(page.Find("[data-testid='task-connector-target-card-title-follows']"));
        Assert.Empty(page.FindAll("[data-testid='task-connector-target-card-statuses']"));
        Assert.Empty(page.FindAll("[data-testid='task-connectors-message']"));
        Assert.DoesNotContain("The source is down.", page.Markup, StringComparison.Ordinal);
    }

    /// <summary>A switch flipped while the source is still being asked draws no row
    /// for the stored mapping, and must not clear it either.</summary>
    [Fact]
    public async Task A_switch_flipped_before_the_statuses_arrive_keeps_the_stored_map()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<SourceStatus>>();
        var target = new ConnectedTarget(ListingConnector.Id, "backlog")
        {
            RepositoryId = "JSdotNet/Backlog",
            StatusMap = new Dictionary<string, EntryStatus> { ["Verfijnd"] = EntryStatus.Ready },
        };
        var (context, targets) = ComposeListing(new ListingConnector(_ => pending.Task), target);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        Assert.Empty(page.FindAll("[data-testid='task-connector-target-card-statuses']"));

        await page.Find("[data-testid='task-connector-target-card-title-follows'] button[role='switch']").ClickAsync(new());

        var stored = targets.Get(ListingConnector.Id, "backlog")!;
        Assert.False(stored.TitleFollowsSource);
        Assert.Equal("JSdotNet/Backlog", stored.RepositoryId);
        Assert.Equal(EntryStatus.Ready, Assert.Single(stored.StatusMap).Value);

        pending.SetResult(ProductStatuses);
        page.WaitForAssertion(() => Assert.Equal("Ready", page.Find("[data-testid='task-connector-target-card-status-1'] select").GetAttribute("value")));
    }

    /// <summary>A target connected on the page asks its source for its statuses, so
    /// its card has rows without the page being opened again.</summary>
    [Fact]
    public async Task A_target_connected_on_the_page_asks_for_its_statuses()
    {
        var connector = new ListingConnector(_ => Task.FromResult(ProductStatuses));
        var (context, _) = ComposeListing(connector);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("input#task-connector-target").InputAsync(new() { Value = "backlog" });
        await page.Find("[data-testid='task-connector-connect']").ClickAsync(new());

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='task-connector-target-card-statuses']")));
        Assert.Equal(["backlog"], connector.Asked);
    }

    /// <summary>A source slow to list its statuses holds up neither the connect
    /// form's choices nor the disconnected list: each is drawn as soon as its own
    /// answer is in, and the card draws its rows when the statuses land.</summary>
    [Fact]
    public void A_status_listing_that_never_answers_holds_up_neither_the_choices_nor_the_disconnected_list()
    {
        var context = new BunitContext();
        using var _context = context;
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var targets = new InMemoryTargets(new ConnectedTarget(ListingConnector.Id, "backlog"));
        var never = new TaskCompletionSource<IReadOnlyList<SourceStatus>>();
        context.Services.AddSingleton(new LinkedTaskSources(
            [new ChoosingConnector(new ConnectorTargetChoices([new("fincent", "Fincent")])), new ListingConnector(_ => never.Task)],
            targets));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        context.Services.AddSingleton<IDisconnectedLinkedTasks>(
            new FakeDisconnected(targets, [new DisconnectedSource(FakeConnector.Id, "owner/old", 3, 0)]));

        var page = context.Render<TaskConnectorSettings>();

        page.WaitForAssertion(() => Assert.False(page.Find("select#task-connector-target").HasAttribute("disabled")));
        Assert.NotNull(page.Find("select#task-connector-target option[value='fincent']"));
        Assert.NotNull(page.Find("[data-testid='task-connectors-disconnected']"));
        Assert.Empty(page.FindAll("[data-testid='task-connector-target-card-statuses']"));
    }

    /// <summary>A source that lists nothing — signed out, refused, failing — has not
    /// stopped listing anything, so its stored mappings are neither drawn as rows
    /// nor cleared by another change.</summary>
    [Fact]
    public async Task A_source_that_lists_nothing_draws_no_row_for_a_stored_mapping_and_keeps_it()
    {
        var target = new ConnectedTarget(ListingConnector.Id, "backlog")
        {
            StatusMap = new Dictionary<string, EntryStatus> { ["Verfijnd"] = EntryStatus.Ready },
        };
        var (context, targets) = ComposeListing(new ListingConnector(_ => Task.FromResult<IReadOnlyList<SourceStatus>>([])), target);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        Assert.Empty(page.FindAll("[data-testid='task-connector-target-card-statuses']"));

        await page.Find("[data-testid='task-connector-target-card-title-follows'] button[role='switch']").ClickAsync(new());

        Assert.Equal(EntryStatus.Ready, Assert.Single(targets.Get(ListingConnector.Id, "backlog")!.StatusMap).Value);
    }

    /// <summary>Two cards draw their selects under ids of their own, so each label
    /// points at its own card's select.</summary>
    [Fact]
    public void Each_card_draws_its_selects_under_ids_of_its_own()
    {
        var (context, _) = ComposeListing(
            new ListingConnector(_ => Task.FromResult(ProductStatuses)),
            new ConnectedTarget(ListingConnector.Id, "backlog"),
            new ConnectedTarget(ListingConnector.Id, "fincent"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        var ids = page.FindAll(".connected-target select").Select(select => select.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var card in page.FindAll("[data-testid='task-connector-target-card']"))
        {
            foreach (var label in card.QuerySelectorAll("label.field__label[for]"))
            {
                Assert.NotNull(card.QuerySelector($"select#{label.GetAttribute("for")}"));
            }
        }
    }

    /// <summary>A sign-in asks the connector's targets for their statuses again,
    /// since a source signed out lists none, and the rows it then lists are drawn.</summary>
    [Fact]
    public void A_sign_in_asks_for_the_statuses_again_and_draws_them()
    {
        var connector = new SigningListingConnector(call => Task.FromResult(call == 0 ? [] : ProductStatuses));
        var (context, _) = ComposeSigningListing(connector, new ConnectedTarget(SigningListingConnector.Id, "backlog"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        Assert.Empty(page.FindAll("[data-testid='task-connector-target-card-statuses']"));

        connector.RaiseAccountChanged();

        page.WaitForAssertion(() => Assert.Equal(
            ["Idee", "Verfijnd", "Gereed"],
            page.FindAll("[data-testid='task-connector-target-card-statuses'] label.field__label").Select(label => label.TextContent)));
        Assert.Equal(2, connector.Calls);
    }

    /// <summary>Only the latest request for a target lands: an answer asked for
    /// before a sign-in, arriving after the newer one, is dropped.</summary>
    [Fact]
    public void An_answer_overtaken_by_a_newer_request_is_dropped()
    {
        var first = new TaskCompletionSource<IReadOnlyList<SourceStatus>>();
        var second = new TaskCompletionSource<IReadOnlyList<SourceStatus>>();
        var connector = new SigningListingConnector(call => call == 0 ? first.Task : second.Task);
        var (context, _) = ComposeSigningListing(connector, new ConnectedTarget(SigningListingConnector.Id, "backlog"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        connector.RaiseAccountChanged();
        page.WaitForAssertion(() => Assert.Equal(2, connector.Calls));

        second.SetResult(ProductStatuses);
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='task-connector-target-card-statuses']")));
        first.SetResult([new SourceStatus("Oud", NormalisedSourceState.Open)]);

        page.WaitForAssertion(() => Assert.Equal(
            ["Idee", "Verfijnd", "Gereed"],
            page.FindAll("[data-testid='task-connector-target-card-statuses'] label.field__label").Select(label => label.TextContent)));
    }

    private static (BunitContext Context, InMemoryTargets Targets) ComposeSigningListing(SigningListingConnector connector, params ConnectedTarget[] targets)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var store = new InMemoryTargets(targets);
        context.Services.AddSingleton(new LinkedTaskSources([connector], store));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        return (context, store);
    }

    /// <summary>A connector a person signs in to whose targets list their statuses,
    /// answering each call by its number, from zero.</summary>
    private sealed class SigningListingConnector(Func<int, Task<IReadOnlyList<SourceStatus>>> byCall) : ITaskConnector, ITaskConnectorSignIn
    {
        public const string Id = "signing-listing";

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Signing listing", "signing-listing", "color-primary");

        public TaskConnectorCapabilities Capabilities { get; } = new();

        public TaskConnectorAccount? Account { get; } = new("Someone", DateTimeOffset.UnixEpoch);

        public event Action? AccountChanged;

        /// <summary>How many times the statuses were asked for.</summary>
        public int Calls { get; private set; }

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);

        public Task<IReadOnlyList<SourceStatus>> ListSourceStatusesAsync(string target, CancellationToken cancellationToken) =>
            byCall(Calls++);

        public Task<string?> SignInAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void RaiseAccountChanged() => AccountChanged?.Invoke();
    }

    /// <summary>A connector whose targets list their statuses, through the delegate
    /// the test hands it.</summary>
    private sealed class ListingConnector(Func<string, Task<IReadOnlyList<SourceStatus>>> list, bool targetIsRepository = false) : ITaskConnector
    {
        public const string Id = "listing";

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Listing", "listing", "color-primary");

        public TaskConnectorCapabilities Capabilities { get; } = new(TargetIsRepository: targetIsRepository);

        /// <summary>Every target asked for its statuses, in order.</summary>
        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);

        public Task<IReadOnlyList<SourceStatus>> ListSourceStatusesAsync(string target, CancellationToken cancellationToken)
        {
            Asked.Add(target);
            return list(target);
        }
    }

    /// <summary>A target filed under an archived repository says on its card that it
    /// is paused, and keeps its switch on; one filed under an active repository says
    /// nothing (local ADR 0029 §5).</summary>
    [Fact]
    public void A_target_filed_under_an_archived_repository_says_it_is_paused()
    {
        var context = new BunitContext();
        using var _context = context;
        var store = new InMemoryTargets([
            new ConnectedTarget(ListingConnector.Id, "budget") { RepositoryId = "innovadis-dev/Budgetbeheer" },
            new ConnectedTarget(ListingConnector.Id, "backlog") { RepositoryId = "JSdotNet/Backlog" }]);
        context.Services.AddSingleton(new LinkedTaskSources([new ListingConnector(_ => Task.FromResult<IReadOnlyList<SourceStatus>>([]))], store));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        context.Services.AddSingleton<IRepositoryDirectory>(new OneArchived());

        var page = context.Render<TaskConnectorSettings>();

        var cards = page.FindAll("[data-testid='task-connector-target-card']");
        Assert.Equal(2, cards.Count);
        var paused = page.FindAll("[data-testid='task-connector-target-card-paused']");
        Assert.Equal("Paused while innovadis-dev/Budgetbeheer is archived.", Assert.Single(paused).TextContent);
        Assert.True(store.Get(ListingConnector.Id, "budget")!.Enabled);
    }

    private static IReadOnlyList<string> RepositoryOptionLabels(IRenderedComponent<TaskConnectorSettings> page) =>
        [.. page.FindAll("[data-testid='task-connector-target-card-repository'] select option").Select(option => option.TextContent)];

    /// <summary>
    /// Scenario "Show archived is on" for the Connectors page's repository picker (local
    /// ADR 0029, §3): an archived repository is offered only while show archived is on,
    /// and then says so.
    /// </summary>
    [Fact]
    public void The_repository_picker_offers_an_archived_repository_only_while_show_archived_is_on()
    {
        var context = new BunitContext();
        using var _context = context;
        var store = new InMemoryTargets([new ConnectedTarget(ListingConnector.Id, "backlog")]);
        var showArchived = new ShowArchivedRepositories(store: null);
        context.Services.AddSingleton(new LinkedTaskSources([new ListingConnector(_ => Task.FromResult<IReadOnlyList<SourceStatus>>([]))], store));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        context.Services.AddSingleton<IRepositoryDirectory>(new OneArchived());
        context.Services.AddSingleton<IShowArchivedRepositories>(showArchived);

        var page = context.Render<TaskConnectorSettings>();
        Assert.Equal(["None", "backlog"], RepositoryOptionLabels(page));

        showArchived.SetShown(true);
        var reopened = context.Render<TaskConnectorSettings>();

        Assert.Equal(["None", "budgetbeheer (archived)", "backlog"], RepositoryOptionLabels(reopened));
    }

    /// <summary>A target already filed under an archived repository keeps showing it,
    /// marked archived, while show archived is off: hiding it would show the target as
    /// filed nowhere.</summary>
    [Fact]
    public void A_target_filed_under_an_archived_repository_still_shows_it_marked_archived()
    {
        var context = new BunitContext();
        using var _context = context;
        var store = new InMemoryTargets([
            new ConnectedTarget(ListingConnector.Id, "budget") { RepositoryId = "innovadis-dev/Budgetbeheer" }]);
        context.Services.AddSingleton(new LinkedTaskSources([new ListingConnector(_ => Task.FromResult<IReadOnlyList<SourceStatus>>([]))], store));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        context.Services.AddSingleton<IRepositoryDirectory>(new OneArchived());

        var page = context.Render<TaskConnectorSettings>();

        Assert.Equal(["None", "budgetbeheer (archived)", "backlog"], RepositoryOptionLabels(page));
        Assert.Equal(
            "innovadis-dev/Budgetbeheer",
            page.Find("[data-testid='task-connector-target-card-repository'] select").GetAttribute("value"));
    }

    /// <summary>
    /// Scenario "Show archived is on" for "Connect a task source": a connector whose
    /// target is a repository offers an archived one only while show archived is on,
    /// and then says so (local ADR 0029, §3).
    /// </summary>
    [Fact]
    public void Connect_a_task_source_offers_an_archived_repository_only_while_show_archived_is_on()
    {
        var connector = new ChoosingConnector(new ConnectorTargetChoices(
            [new("innovadis-dev/Budgetbeheer", "innovadis-dev/Budgetbeheer"), new("JSdotNet/Backlog", "JSdotNet/Backlog")]))
        {
            Capabilities = new(TargetIsRepository: true),
        };
        var (context, _) = ComposeChoosing(connector);
        using var _context = context;
        var showArchived = new ShowArchivedRepositories(store: null);
        context.Services.AddSingleton<IRepositoryDirectory>(new OneArchived());
        context.Services.AddSingleton<IShowArchivedRepositories>(showArchived);

        var page = context.Render<TaskConnectorSettings>();
        Assert.Equal(["JSdotNet/Backlog"], ConnectOptionLabels(page));

        showArchived.SetShown(true);
        var reopened = context.Render<TaskConnectorSettings>();

        Assert.Equal(
            ["innovadis-dev/Budgetbeheer" + ArchivedRepositoryLabel.Suffix, "JSdotNet/Backlog"],
            ConnectOptionLabels(reopened));
    }

    private static IReadOnlyList<string> ConnectOptionLabels(IRenderedComponent<TaskConnectorSettings> page) =>
        [.. page.FindAll("select#task-connector-target option")
            .Where(option => option.GetAttribute("value") != string.Empty)
            .Select(option => option.TextContent)];

    /// <summary>A switched-off target already says why it is not syncing; its
    /// archived repository adds no second reason (the option's contract is "not
    /// syncing although it is switched on").</summary>
    [Fact]
    public void A_switched_off_target_under_an_archived_repository_is_not_said_to_be_paused()
    {
        var context = new BunitContext();
        using var _context = context;
        var store = new InMemoryTargets([
            new ConnectedTarget(ListingConnector.Id, "budget") { RepositoryId = "innovadis-dev/Budgetbeheer", Enabled = false }]);
        context.Services.AddSingleton(new LinkedTaskSources([new ListingConnector(_ => Task.FromResult<IReadOnlyList<SourceStatus>>([]))], store));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        context.Services.AddSingleton<IRepositoryDirectory>(new OneArchived());

        var page = context.Render<TaskConnectorSettings>();

        Assert.Single(page.FindAll("[data-testid='task-connector-target-card']"));
        Assert.Empty(page.FindAll("[data-testid='task-connector-target-card-paused']"));
    }

    /// <summary>A registry holding one archived and one active repository, resolving
    /// by <c>owner/name</c> as the real one does.</summary>
    private sealed class OneArchived : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories { get; } =
        [
            new("budgetbeheer", "innovadis-dev", "Budgetbeheer") { IsArchived = true },
            new("backlog", "JSdotNet", "Backlog"),
        ];

        public TasksRepositoryRef? Resolve(string name) =>
            Repositories.FirstOrDefault(repository => string.Equals(repository.Id, name.Trim(), StringComparison.OrdinalIgnoreCase));

        public Result<TasksRepositoryRef> Register(string name) => new TasksRepositoryRef(name, name, name);
    }

    /// <summary>A registry of two repositories, whose aliases are not their names, so
    /// the label and the stored value can be told apart.</summary>
    private sealed class TwoRepositories : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories { get; } =
        [
            new("backlog", "JSdotNet", "Backlog"),
            new("marketplace", "JSdotNet", "Marketplace"),
        ];

        public TasksRepositoryRef? Resolve(string name) => null;

        public Result<TasksRepositoryRef> Register(string name) => new TasksRepositoryRef(name, name, name);
    }

    /// <summary>A connector whose targets are products, naming its field the way
    /// spec-manager's descriptor does.</summary>
    private sealed class ProductConnector : ITaskConnector
    {
        public const string Id = "products";

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Products", "products", "color-primary")
        {
            TargetLabel = "Product",
            TargetPlaceholder = "product-slug",
            TargetHelp = "The product's slug, as it appears in its spec-manager URL.",
        };

        public TaskConnectorCapabilities Capabilities { get; } = new();

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);
    }

    /// <summary>The disconnected sources the test set, listing only those whose
    /// pair the targets do not hold, and refusing a delete for one they do — as the
    /// module's handler does.</summary>
    private sealed class FakeDisconnected(InMemoryTargets targets, IEnumerable<DisconnectedSource> sources) : IDisconnectedLinkedTasks
    {
        private readonly List<DisconnectedSource> _sources = [.. sources];

        public List<(string ConnectorId, string Target)> Deleted { get; } = [];

        /// <summary>How many times the page asked for the list.</summary>
        public int Lists { get; private set; }

        private bool IsConnected(string connectorId, string target) => targets.Get(connectorId, target) is not null;

        public Task<IReadOnlyList<DisconnectedSource>> ListAsync(CancellationToken cancellationToken = default)
        {
            Lists++;
            return Task.FromResult<IReadOnlyList<DisconnectedSource>>(
                [.. _sources.Where(source => !IsConnected(source.ConnectorId, source.Target))]);
        }

        public Task<Result<int>> DeleteAsync(string connectorId, string target, CancellationToken cancellationToken = default)
        {
            if (IsConnected(connectorId, target))
            {
                return Task.FromResult<Result<int>>(Error.Conflict(
                    "linked_tasks.target_connected",
                    $"{target} is connected again, so its tasks were not deleted."));
            }

            var source = _sources.Single(candidate => candidate.ConnectorId == connectorId && candidate.Target == target);
            _sources.Remove(source);
            Deleted.Add((connectorId, target));
            return Task.FromResult<Result<int>>(source.TaskCount);
        }
    }

    /// <summary>The deleted sources the test set, restoring one only while its pair
    /// is connected — as the module's handler does — and listing it no more once
    /// restored.</summary>
    private sealed class FakeDeleted(InMemoryTargets targets, IEnumerable<DeletedLinkedSource> sources) : IDeletedLinkedTasks
    {
        private readonly List<DeletedLinkedSource> _sources = [.. sources];

        public List<(string ConnectorId, string Target)> Restored { get; } = [];

        public Task<IReadOnlyList<DeletedLinkedSource>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeletedLinkedSource>>([.. _sources]);

        public Task<Result<int>> RestoreAsync(string connectorId, string target, CancellationToken cancellationToken = default)
        {
            if (targets.Get(connectorId, target) is null)
            {
                return Task.FromResult<Result<int>>(Error.Conflict(
                    "linked_tasks.target_not_connected",
                    $"{target} is no longer connected, so its tasks were not restored."));
            }

            var source = _sources.Single(candidate =>
                candidate.ConnectorId == connectorId && string.Equals(candidate.Target, target, StringComparison.OrdinalIgnoreCase));
            _sources.Remove(source);
            Restored.Add((connectorId, target));
            return Task.FromResult<Result<int>>(source.TaskCount);
        }
    }

    internal sealed class RecordingSync : ILinkedTaskSync
    {
        public int Runs { get; private set; }

        /// <summary>Every one-target request, in order.</summary>
        public List<(string ConnectorId, string Target)> Requests { get; } = [];

        /// <summary>What every one-target request answers.</summary>
        public LinkedTaskSyncOutcome Outcome { get; set; } = new(true, "Synced.");

        /// <summary>When set, answers each one-target request instead of
        /// <see cref="Outcome"/>, for a test that finishes the syncs itself.</summary>
        public Func<string, string, Task<LinkedTaskSyncOutcome>>? Answer { get; set; }

        public Task RequestSync()
        {
            Runs++;
            return Task.CompletedTask;
        }

        public Task<LinkedTaskSyncOutcome> RequestSync(string connectorId, string target)
        {
            Requests.Add((connectorId, target));
            return Answer?.Invoke(connectorId, target) ?? Task.FromResult(Outcome);
        }
    }
}
