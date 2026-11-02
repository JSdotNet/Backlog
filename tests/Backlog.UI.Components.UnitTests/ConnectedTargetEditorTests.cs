using Backlog.UI.Components.Integrations;
using Backlog.UI.Components.Selects;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// One connected repository or product, and the options its sync runs by (ADR 0020
/// §9). The card holds no draft: every change is reported at once as the whole
/// record, and the host hands the stored record back.
/// </summary>
public sealed class ConnectedTargetEditorTests
{
    private static readonly ConnectedTargetOptions Target =
        new("GitHub", "JSdotNet/Backlog") { Icon = "github", LastSynced = "5 Oct 2026 09:15" };

    [Fact]
    public void It_names_the_connector_the_target_and_when_it_last_synced()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));

        Assert.Equal("JSdotNet/Backlog", card.Find("[data-testid='t-name']").TextContent);
        Assert.Equal("Synced 5 Oct 2026 09:15", card.Find("[data-testid='t-synced']").TextContent.Trim());
        Assert.Contains("GitHub", card.Find(".connected-target__connector").TextContent);
    }

    [Fact]
    public void A_target_that_never_synced_says_so()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target with { LastSynced = null }).Add(c => c.TestId, "t"));

        Assert.Equal("Not synced yet", card.Find("[data-testid='t-synced']").TextContent.Trim());
    }

    /// <summary>A target that stopped syncing says why on its own card, with when,
    /// until a sync succeeds — and a card with nothing wrong says nothing.</summary>
    [Fact]
    public void A_last_sync_that_failed_is_shown_on_the_card_with_when()
    {
        using var context = new BunitContext();

        var failed = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target with { LastSyncError = "GitHub cannot find JSdotNet/Backlog.", LastSyncFailed = "7 Oct 2026 21:53" })
            .Add(c => c.TestId, "t"));
        var fine = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));

        Assert.Equal(
            "Last sync failed 7 Oct 2026 21:53: GitHub cannot find JSdotNet/Backlog.",
            failed.Find("[data-testid='t-error']").TextContent);
        Assert.Empty(fine.FindAll("[data-testid='t-error']"));
    }

    /// <summary>A target the host says is paused — its repository archived — says
    /// so on its own card; a card that is not paused says nothing.</summary>
    [Fact]
    public void A_paused_target_says_why_on_the_card()
    {
        using var context = new BunitContext();

        var paused = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target with { PausedReason = "Paused while JSdotNet/Backlog is archived." })
            .Add(c => c.TestId, "t"));
        var running = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));

        Assert.Equal("Paused while JSdotNet/Backlog is archived.", paused.Find("[data-testid='t-paused']").TextContent);
        Assert.Equal("status", paused.Find("[data-testid='t-paused']").GetAttribute("role"));
        Assert.Empty(running.FindAll("[data-testid='t-paused']"));
    }

    [Fact]
    public void The_hosts_answer_to_sync_now_is_shown_on_the_card()
    {
        using var context = new BunitContext();

        var answered = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target)
            .Add(c => c.SyncMessage, "Synced JSdotNet/Backlog: 2 new.")
            .Add(c => c.TestId, "t"));
        var quiet = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));

        Assert.Equal("Synced JSdotNet/Backlog: 2 new.", answered.Find("[data-testid='t-sync-message']").TextContent);
        Assert.Empty(quiet.FindAll("[data-testid='t-sync-message']"));
    }

    [Fact]
    public async Task A_switch_reports_the_whole_record_with_that_one_option_changed()
    {
        using var context = new BunitContext();
        ConnectedTargetOptions? changed = null;

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target)
            .Add(c => c.OnChange, options => changed = options)
            .Add(c => c.TestId, "t"));

        await card.Find("[data-testid='t-title-follows'] button[role='switch']").ClickAsync(new());

        Assert.Equal(Target with { TitleFollowsSource = false }, changed);
    }

    /// <summary>Promote to plan was dropped unbuilt, so no switch says what it would
    /// do: a setting for something the app cannot do would do nothing.</summary>
    [Fact]
    public void No_switch_offers_a_setting_for_promoting_to_a_plan()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));

        Assert.Empty(card.FindAll("[data-testid='t-promote-archives']"));
        Assert.DoesNotContain("Promot", card.Markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The switch is offered only where the connector can complete an item
    /// at its source; elsewhere it would do nothing.</summary>
    [Fact]
    public void Complete_at_the_source_is_offered_only_where_the_connector_can()
    {
        using var context = new BunitContext();

        var cannot = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));
        var can = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target with { CanCompleteAtSource = true }).Add(c => c.TestId, "t"));

        Assert.Empty(cannot.FindAll("[data-testid='t-complete-at-source']"));
        var toggle = can.Find("[data-testid='t-complete-at-source']");
        Assert.Contains("Complete at the source", toggle.TextContent);
        Assert.Equal("false", toggle.QuerySelector("button[role='switch']")!.GetAttribute("aria-checked"));
    }

    /// <summary>"Only questions assigned to me" is offered only where the source asks
    /// questions, and starts off.</summary>
    [Fact]
    public void Only_questions_assigned_to_me_is_offered_only_where_the_source_asks_questions()
    {
        using var context = new BunitContext();

        var none = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));
        var asks = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target with { HasQuestions = true }).Add(c => c.TestId, "t"));

        Assert.Empty(none.FindAll("[data-testid='t-only-assigned-questions']"));
        var toggle = asks.Find("[data-testid='t-only-assigned-questions']");
        Assert.Contains("Only questions assigned to me", toggle.TextContent);
        Assert.Equal("false", toggle.QuerySelector("button[role='switch']")!.GetAttribute("aria-checked"));
    }

    /// <summary>A connector that asks questions also pushes them to a phone subscribed
    /// to it, so its card says which of its preferences leaves only Backlog's push;
    /// a connector that asks none says nothing about it.</summary>
    [Fact]
    public void A_connector_that_asks_questions_says_how_to_switch_off_its_own_push()
    {
        using var context = new BunitContext();
        var specManager = new ConnectedTargetOptions("spec-manager", "billing") { HasQuestions = true };

        var asks = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, specManager).Add(c => c.TestId, "t"));
        var none = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));

        var hint = asks.Find("[data-testid='t-only-assigned-questions'] [data-testid='t-question-push-hint']");
        Assert.Equal(
            "Questions also arrive as spec-manager's own push on a phone subscribed to it. "
            + "Switch off \"Backlog question\" in spec-manager's notification preferences on that phone to get only Backlog's.",
            hint.TextContent);
        Assert.Empty(none.FindAll("[data-testid='t-question-push-hint']"));
    }

    [Fact]
    public async Task Switching_only_questions_assigned_to_me_on_reports_the_whole_record()
    {
        using var context = new BunitContext();
        ConnectedTargetOptions? changed = null;
        var options = Target with { HasQuestions = true };

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.OnChange, value => changed = value)
            .Add(c => c.TestId, "t"));

        await card.Find("[data-testid='t-only-assigned-questions'] button[role='switch']").ClickAsync(new());

        Assert.Equal(options with { OnlyQuestionsAssignedToMe = true }, changed);
    }

    [Fact]
    public async Task Switching_complete_at_the_source_on_reports_the_whole_record()
    {
        using var context = new BunitContext();
        ConnectedTargetOptions? changed = null;
        var options = Target with { CanCompleteAtSource = true };

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.OnChange, value => changed = value)
            .Add(c => c.TestId, "t"));

        await card.Find("[data-testid='t-complete-at-source'] button[role='switch']").ClickAsync(new());

        Assert.Equal(options with { CompleteAtSource = true }, changed);
    }

    [Fact]
    public async Task The_interval_and_the_skip_age_are_reported_in_minutes_and_days()
    {
        using var context = new BunitContext();
        var changes = new List<ConnectedTargetOptions>();

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target)
            .Add(c => c.OnChange, options => changes.Add(options))
            .Add(c => c.TestId, "t"));

        await card.Find("select#t-interval").ChangeAsync(new() { Value = "60" });
        await card.Find("select#t-skip").ChangeAsync(new() { Value = "90" });
        await card.Find("select#t-skip").ChangeAsync(new() { Value = string.Empty });

        Assert.Equal(60, changes[0].SyncIntervalMinutes);
        Assert.Equal(90, changes[1].SkipUntouchedOlderThanDays);
        Assert.Null(changes[2].SkipUntouchedOlderThanDays);
    }

    [Fact]
    public void A_stored_interval_that_is_no_offered_choice_is_still_offered()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target with { SyncIntervalMinutes = 45 }).Add(c => c.TestId, "t"));

        Assert.Contains(card.FindAll("select#t-interval option"), option => option.GetAttribute("value") == "45");
    }

    [Fact]
    public void With_no_listener_there_are_no_actions()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));

        Assert.Empty(card.FindAll(".connected-target__actions"));
    }

    [Fact]
    public async Task Sync_now_and_disconnect_report_the_target()
    {
        using var context = new BunitContext();
        ConnectedTargetOptions? synced = null;
        ConnectedTargetOptions? removed = null;

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target)
            .Add(c => c.OnSyncNow, options => synced = options)
            .Add(c => c.OnRemove, options => removed = options)
            .Add(c => c.TestId, "t"));

        await card.Find("[data-testid='t-sync']").ClickAsync(new());
        await card.Find("[data-testid='t-remove']").ClickAsync(new());

        Assert.Equal(Target, synced);
        Assert.Equal(Target, removed);
    }

    /// <summary>Restore deleted tasks is drawn only when the host listens for it and
    /// says the target has deleted tasks: a button with nothing to restore would do
    /// nothing.</summary>
    [Theory]
    [InlineData(true, 3, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 3, false)]
    public void Restore_deleted_tasks_is_offered_only_with_a_listener_and_something_to_restore(bool listening, int deleted, bool offered)
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p =>
        {
            p.Add(c => c.Options, Target with { DeletedTaskCount = deleted }).Add(c => c.TestId, "t");
            if (listening) p.Add(c => c.OnRestoreDeleted, _ => { });
        });

        Assert.Equal(offered, card.FindAll("[data-testid='t-restore']").Count == 1);
    }

    /// <summary>A restore and a sync would write the same tasks at once, so the
    /// restore waits: it is disabled while the host says a sync runs, and a press
    /// asks nothing.</summary>
    [Fact]
    public async Task Restore_deleted_tasks_is_disabled_while_a_sync_runs()
    {
        using var context = new BunitContext();
        var asked = 0;

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target with { DeletedTaskCount = 3 })
            .Add(c => c.Syncing, true)
            .Add(c => c.OnSyncNow, _ => { })
            .Add(c => c.OnRestoreDeleted, _ => asked++)
            .Add(c => c.TestId, "t"));

        var restore = card.Find("[data-testid='t-restore']");
        Assert.True(restore.HasAttribute("disabled"));
        await restore.ClickAsync(new());

        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task Restore_deleted_tasks_reports_the_target()
    {
        using var context = new BunitContext();
        ConnectedTargetOptions? restored = null;
        var options = Target with { DeletedTaskCount = 34 };

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.OnRestoreDeleted, chosen => restored = chosen)
            .Add(c => c.TestId, "t"));

        var restore = card.Find("[data-testid='t-restore']");
        Assert.Equal("Restore deleted tasks", restore.TextContent.Trim());
        await restore.ClickAsync(new());

        Assert.Equal(options, restored);
    }

    [Fact]
    public void A_target_switched_off_refuses_sync_now()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target with { Enabled = false })
            .Add(c => c.OnSyncNow, _ => { })
            .Add(c => c.TestId, "t"));

        Assert.True(card.Find("[data-testid='t-sync']").HasAttribute("disabled"));
    }

    private static readonly IReadOnlyList<SelectorOption> Repositories =
    [
        new("JSdotNet/Backlog", "backlog"),
        new("JSdotNet/Marketplace", "marketplace"),
    ];

    private static readonly IReadOnlyList<SelectorOption> Statuses =
    [
        new("Draft", "Draft"),
        new("Ready", "Ready"),
        new("InProgress", "In progress"),
        new("Done", "Done"),
        new("Archived", "Archived"),
    ];

    private static readonly ConnectedTargetOptions Product =
        new("spec-manager", "backlog")
        {
            RepositoryChoices = Repositories,
            StatusChoices = Statuses,
            StatusRows =
            [
                new("Idee", "Default (Ready)"),
                new("Verfijnd", "Default (In progress)", "Ready"),
                new("Gereed", "Default (Done)"),
            ],
        };

    /// <summary>The empty choice says what no repository means: a target that is a
    /// repository files under itself, any other under none.</summary>
    [Fact]
    public void The_repository_pickers_empty_choice_says_what_no_repository_means()
    {
        using var context = new BunitContext();

        var product = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Product).Add(c => c.TestId, "t"));
        var repository = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target with { RepositoryChoices = Repositories, TargetIsRepository = true })
            .Add(c => c.TestId, "t"));

        var options = product.FindAll("select#t-repository option");
        Assert.Equal(["None", "backlog", "marketplace"], options.Select(option => option.TextContent));
        Assert.Equal("Its own repository (JSdotNet/Backlog)", repository.FindAll("select#t-repository option")[0].TextContent);
        Assert.Contains("Tasks you already filed keep theirs.", product.Find("[data-testid='t-repository']").TextContent);
    }

    [Fact]
    public async Task Picking_a_repository_reports_it_and_the_empty_choice_clears_it()
    {
        using var context = new BunitContext();
        var changes = new List<ConnectedTargetOptions>();

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Product with { RepositoryId = "JSdotNet/Marketplace" })
            .Add(c => c.OnChange, options => changes.Add(options))
            .Add(c => c.TestId, "t"));

        await card.Find("select#t-repository").ChangeAsync(new() { Value = "JSdotNet/Backlog" });
        await card.Find("select#t-repository").ChangeAsync(new() { Value = string.Empty });

        Assert.Equal("JSdotNet/Backlog", changes[0].RepositoryId);
        Assert.Null(changes[1].RepositoryId);
    }

    /// <summary>A stored repository the registry no longer holds is still offered and
    /// still selected, so editing another option never rewrites it.</summary>
    [Fact]
    public void A_stored_repository_the_choices_no_longer_hold_is_still_offered()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Product with { RepositoryId = "JSdotNet/Gone" })
            .Add(c => c.TestId, "t"));

        var option = Assert.Single(card.FindAll("select#t-repository option"), option => option.GetAttribute("value") == "JSdotNet/Gone");
        Assert.True(option.HasAttribute("selected"));
    }

    [Fact]
    public void With_no_repository_to_pick_and_none_stored_there_is_no_picker()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Target).Add(c => c.TestId, "t"));

        Assert.Empty(card.FindAll("[data-testid='t-repository']"));
    }

    [Fact]
    public void A_source_that_lists_no_status_gets_no_statuses_group()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Target with { StatusChoices = Statuses })
            .Add(c => c.TestId, "t"));

        Assert.Empty(card.FindAll("[data-testid='t-statuses']"));
    }

    /// <summary>One row per status, named as the source names it, its empty choice the
    /// default the host worded, and a mapped row showing its mapping.</summary>
    [Fact]
    public void Each_status_is_a_row_with_its_default_and_its_mapping()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p.Add(c => c.Options, Product).Add(c => c.TestId, "t"));

        var group = card.Find("[data-testid='t-statuses']");
        Assert.Equal("Statuses", group.QuerySelector("legend")!.TextContent);
        Assert.Equal(["Idee", "Verfijnd", "Gereed"], group.QuerySelectorAll("label.field__label").Select(label => label.TextContent));
        Assert.Equal("Default (In progress)", card.FindAll("select#t-status-1 option")[0].TextContent);
        Assert.Equal("Ready", card.Find("select#t-status-1").GetAttribute("value"));
        Assert.Equal(string.Empty, card.Find("select#t-status-0").GetAttribute("value"));
    }

    [Fact]
    public async Task Mapping_a_row_reports_it_and_the_default_clears_it()
    {
        using var context = new BunitContext();
        var changes = new List<ConnectedTargetOptions>();

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Product)
            .Add(c => c.OnChange, options => changes.Add(options))
            .Add(c => c.TestId, "t"));

        await card.Find("select#t-status-0").ChangeAsync(new() { Value = "Draft" });
        await card.Find("select#t-status-1").ChangeAsync(new() { Value = string.Empty });

        Assert.Equal(["Draft", "Ready", null], changes[0].StatusRows.Select(row => row.Mapped));
        Assert.Equal([null, null, null], changes[1].StatusRows.Select(row => row.Mapped));
        Assert.Equal(Product.StatusRows.Select(row => row.Name), changes[0].StatusRows.Select(row => row.Name));
    }

    /// <summary>An id prefix names the selects' ids, so two cards under one test id
    /// keep each label on its own select; the test ids stay the test id's.</summary>
    [Fact]
    public void An_id_prefix_names_the_selects_ids_and_leaves_the_test_ids_alone()
    {
        using var context = new BunitContext();

        var card = context.Render<ConnectedTargetEditor>(p => p
            .Add(c => c.Options, Product)
            .Add(c => c.TestId, "t")
            .Add(c => c.IdPrefix, "card-a"));

        Assert.NotNull(card.Find("[data-testid='t-repository'] select#card-a-repository"));
        Assert.NotNull(card.Find("[data-testid='t-status-2'] select#card-a-status-2"));
        Assert.NotNull(card.Find("[data-testid='t-interval'] select#card-a-interval"));
        Assert.Equal("card-a-repository", card.Find("[data-testid='t-repository'] label").GetAttribute("for"));
    }
}
