using Backlog.UI.Components.Integrations;

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
}
