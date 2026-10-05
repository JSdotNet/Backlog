using Backlog.Modules.Tasks.Abstractions.Connectors;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static Backlog.Desktop.UI.UnitTests.LinkedTaskPaneTests;

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
        await page.Find("[data-testid='task-connector-target-card-assigned'] button[role='switch']").ClickAsync(new());
        await page.Find("select#task-connector-target-card-interval").ChangeAsync(new() { Value = "60" });

        var stored = Assert.Single(targets.List());
        Assert.True(stored.AssignedToMeByDefault);
        Assert.Equal(TimeSpan.FromHours(1), stored.SyncInterval);
    }

    [Fact]
    public async Task Sync_now_runs_the_sync_and_disconnect_removes_the_target()
    {
        var (context, targets, sync) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-sync']").ClickAsync(new());
        Assert.Equal(1, sync.Runs);

        await page.Find("[data-testid='task-connector-target-card-remove']").ClickAsync(new());
        Assert.Empty(targets.List());
        Assert.Contains("Disconnected owner/repo", page.Find("[data-testid='task-connectors-message']").TextContent);
    }

    [Fact]
    public void A_target_whose_connector_is_not_installed_stays_listed_under_its_id()
    {
        var (context, _, _) = Compose(true, new ConnectedTarget("jira", "PROJ"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Contains("jira", page.Find(".connected-target__connector").TextContent);
    }

    internal sealed class RecordingSync : ILinkedTaskSync
    {
        public int Runs { get; private set; }

        public Task RequestSync()
        {
            Runs++;
            return Task.CompletedTask;
        }
    }
}
