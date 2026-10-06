using Backlog.Modules.Tasks.Abstractions.Connectors;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Linked tasks in the Tasks pane (ADR 0020 §2, §3 and §9): the source badge on the
/// row and in the detail panel, and the Source filter. All of it is drawn from the installed connectors' descriptors, so these use a fake
/// connector and never name a real one.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class LinkedTaskPaneTests
{
    private const string SourceChip = "[data-testid='source-filter-option']";

    private static string RowTestId(EntryRow row) => $"entry-list-{row.TaskId}";

    /// <summary>A host with the fake connector installed, three linked tasks — two
    /// through it, the second blocked at the source, and one from a connector this
    /// build does not know — and one local task.</summary>
    private static async Task<(TasksPaneHost Host, EntryRow Linked, EntryRow Blocked, EntryRow Unknown, EntryRow Local)> FourAsync()
    {
        var sources = new LinkedTaskSources([new FakeConnector()], new InMemoryTargets());
        var host = await TasksPaneHost.CreateAsync(roadmapTags: null, [], linkedSources: sources);

        var linked = await host.WriteEntryAsync("# Ship the installer\n`task` `!ready`\n");
        var blocked = await host.WriteEntryAsync("# Review the tokens\n`task` `!ready`\n");
        var unknown = await host.WriteEntryAsync("# Triage the import bug\n`task` `!ready`\n");
        var local = await host.WriteEntryAsync("# Water the plants\n`task` `!ready`\n");
        await host.State.SelectAsync(null);

        await LinkAsync(host, linked, Reference(FakeConnector.Id, "#1", "someone"));
        await LinkAsync(host, blocked, Reference(FakeConnector.Id, "#2", "someone-else") with { Blocked = true, BlockedReason = "Waiting on review" });
        await LinkAsync(host, unknown, Reference("jira", "JIRA-301", "someone-else"));
        await host.State.ReloadFromStoreAsync();

        return (host, Find(host, linked), Find(host, blocked), Find(host, unknown), Find(host, local));
    }

    private static EntryRow Find(TasksPaneHost host, EntryRow before) => host.State.Rows.Single(row => row.Id == before.Id);

    private static SourceRef Reference(string connectorId, string key, string? assignee) =>
        new(connectorId, "owner/repo", $"{connectorId}-{key}", $"https://example.com/{connectorId}/{Uri.EscapeDataString(key)}", key, assignee, "open",
            new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Gives a written entry a source the way the sync does: on the stored
    /// task, through the repository, never through the text.</summary>
    private static async Task LinkAsync(TasksPaneHost host, EntryRow row, SourceRef source)
    {
        var repository = TasksTestHost.RepositoryFor(new WorkspaceSettingsStore(host.StorageRoot, Path.Combine(host.StorageRoot, "elsewhere.json")));
        var task = await repository.GetAsync(row.Id!.Value, TestContext.Current.CancellationToken);
        task!.SetSourceRef(source);
        await repository.SaveAsync(task, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_linked_row_wears_its_connectors_badge_and_a_local_row_none()
    {
        var (host, linked, blocked, _, local) = await FourAsync();
        using var _host = host;

        var pane = host.Render();

        var badge = pane.Find($"[data-testid='{RowTestId(linked)}'] .badge--linked");
        Assert.Equal("Fake #1", badge.TextContent.Trim());
        Assert.Equal("https://example.com/fake/%231", badge.GetAttribute("href"));

        Assert.Contains("Waiting on review", pane.Find($"[data-testid='{RowTestId(blocked)}'] .task-item__detail--sourceblocked").TextContent);
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(local)}'] .badge--linked"));
    }

    [Fact]
    public async Task A_connector_this_build_does_not_know_shows_its_key_under_the_neutral_badge()
    {
        var (host, _, _, unknown, _) = await FourAsync();
        using var _host = host;

        var badge = host.Render().Find($"[data-testid='{RowTestId(unknown)}'] .badge--linked");

        Assert.Contains("badge--linked-neutral", badge.ClassList);
        Assert.Equal("JIRA-301", badge.TextContent.Trim());
    }

    [Fact]
    public async Task The_detail_panel_shows_the_badge_on_its_heading_line()
    {
        var (host, _, blocked, _, _) = await FourAsync();
        using var _host = host;

        var pane = host.Render();
        await host.OpenAsync(blocked);
        pane.Render();

        Assert.Equal("Fake #2", pane.Find("[data-testid='entry-panel-source']").TextContent.Trim());
        Assert.Contains("Waiting on review", pane.Find("[data-testid='entry-panel-source-flags']").TextContent);
    }

    [Fact]
    public async Task The_source_filter_offers_local_work_and_one_chip_per_connector()
    {
        var (host, _, _, _, _) = await FourAsync();
        using var _host = host;

        var chips = host.Render().FindAll(SourceChip);

        Assert.Equal(["local", FakeConnector.Id, "jira"], chips.Select(chip => chip.GetAttribute("data-source")));
        Assert.Equal(["Local1", "Fake2", "jira1"], chips.Select(chip => chip.TextContent.Trim()));
    }

    [Fact]
    public async Task Pressing_a_source_keeps_only_its_tasks_and_pressing_it_again_widens()
    {
        var (host, linked, blocked, _, local) = await FourAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.Find($"{SourceChip}[data-source='{FakeConnector.Id}']").ClickAsync(new());

        Assert.Equal([linked, blocked], host.State.FilteredRows);

        await pane.Find($"{SourceChip}[data-source='local']").ClickAsync(new());
        Assert.Equal([local], host.State.FilteredRows);

        await pane.Find($"{SourceChip}[data-source='local']").ClickAsync(new());
        Assert.Equal(4, host.State.FilteredRows.Count);
    }

    [Fact]
    public async Task A_backlog_with_no_connector_and_no_linked_task_has_no_source_group()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Water the plants\n`task` `!ready`\n");

        var pane = host.Render();

        Assert.Empty(pane.FindAll(".filter-group--source"));
        Assert.Empty(host.State.SourceFilters);
    }

    /// <summary>A connector that answers nothing; only its descriptor matters to
    /// the screens.</summary>
    internal sealed class FakeConnector : ITaskConnector
    {
        public const string Id = "fake";

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Fake", "fake", "color-primary-light");

        public TaskConnectorCapabilities Capabilities { get; } = new();

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);
    }

    /// <summary>The connected targets, held in a list.</summary>
    internal sealed class InMemoryTargets(params IEnumerable<ConnectedTarget> targets) : IConnectedTargets
    {
        private readonly List<ConnectedTarget> _targets = [.. targets];

        public event Action? Changed;

        public IReadOnlyList<ConnectedTarget> List() => [.. _targets];

        public ConnectedTarget? Get(string connectorId, string target) =>
            _targets.FirstOrDefault(candidate => candidate.Is(connectorId, target));

        public string? Save(ConnectedTarget target)
        {
            _targets.RemoveAll(candidate => candidate.Is(target.ConnectorId, target.Target));
            _targets.Add(target);
            Changed?.Invoke();
            return null;
        }

        public string? Update(string connectorId, string target, Func<ConnectedTarget, ConnectedTarget> change)
        {
            var index = _targets.FindIndex(candidate => candidate.Is(connectorId, target));
            if (index < 0) return "Not connected.";
            _targets[index] = change(_targets[index]);
            Changed?.Invoke();
            return null;
        }

        public string? Remove(string connectorId, string target)
        {
            if (_targets.RemoveAll(candidate => candidate.Is(connectorId, target)) > 0) Changed?.Invoke();
            return null;
        }
    }
}
