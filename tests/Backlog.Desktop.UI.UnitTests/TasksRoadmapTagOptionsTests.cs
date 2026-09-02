using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Selects;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The whole point of the roadmap→backlog tag join: every tag the plan uses is
/// offered in the backlog's tag picker, unioned with the tags the backlog already
/// uses, de-duplicated and in a stable order — and offered even when no backlog entry
/// carries it yet, wearing a hint that says where it came from.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksRoadmapTagOptionsTests
{
    private sealed class StubRoadmapTags(params string[] tags) : IRoadmapTagSource
    {
        public Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(tags);
    }

    private static IReadOnlyList<SelectorOption> TagOptions(IRenderedComponent<TasksPane> pane) =>
        pane.FindComponents<TagMultiSelect>()
            .Single(component => component.Instance.TestId == "entry-tags-input")
            .Instance.Options;

    [Fact]
    public async Task Roadmap_tags_are_offered_even_when_no_backlog_entry_uses_them()
    {
        using var host = await TasksPaneHost.CreateAsync(
            new StubRoadmapTags("planned-only", "shared"),
            []);

        // The backlog uses "shared" and "backlog-only"; the roadmap uses "planned-only"
        // and "shared". "planned-only" is used nowhere in the backlog and must still be
        // offered — that is the feature.
        var row = await host.WriteEntryAsync("# Ship it\n`task` `#shared` `#backlog-only`\n");

        var pane = host.Render();
        await host.OpenAsync(row);

        var options = TagOptions(pane);

        // Union, de-duplicated case-insensitively, in a stable (alphabetical) order.
        Assert.Equal(
            ["backlog-only", "planned-only", "shared"],
            options.Select(option => option.Value));
    }

    [Fact]
    public async Task Only_a_roadmap_tag_no_backlog_entry_uses_carries_the_hint()
    {
        using var host = await TasksPaneHost.CreateAsync(
            new StubRoadmapTags("planned-only", "shared"),
            []);

        var row = await host.WriteEntryAsync("# Ship it\n`task` `#shared` `#backlog-only`\n");

        var pane = host.Render();
        await host.OpenAsync(row);

        var options = TagOptions(pane);

        Assert.Equal("from roadmap", options.Single(option => option.Value == "planned-only").Hint);
        Assert.Null(options.Single(option => option.Value == "shared").Hint);
        Assert.Null(options.Single(option => option.Value == "backlog-only").Hint);
    }

    [Fact]
    public async Task Without_a_roadmap_source_the_picker_offers_only_the_backlog_tags()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Ship it\n`task` `#alpha`\n");

        var pane = host.Render();
        await host.OpenAsync(row);

        var options = TagOptions(pane);

        var option = Assert.Single(options);
        Assert.Equal("alpha", option.Value);
        Assert.Null(option.Hint);
    }
}

/// <summary>
/// The adapter that answers the backlog's tag port from the roadmap plan, over a real
/// stored plan. First-appearance order, and each tag once.
/// </summary>
public sealed class RoadmapPlanTagSourceTests : IDisposable
{
    private readonly string _root;
    private readonly WorkspaceSettingsStore _settings;
    private readonly RoadmapPlanTagSource _source;

    public RoadmapPlanTagSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "roadmap-tag-source-" + Guid.NewGuid().ToString("N"));
        _settings = new WorkspaceSettingsStore(_root, Path.Combine(_root, "settings.json"));
        _source = new RoadmapPlanTagSource(TasksTestHost.PlanningFor(_settings));
    }

    [Fact]
    public async Task An_empty_plan_offers_no_tags()
    {
        Assert.Empty(await _source.TagsInUseAsync());
    }

    [Fact]
    public async Task Each_tag_is_offered_once_in_first_appearance_order()
    {
        var planning = TasksTestHost.PlanningFor(_settings);
        var source = new RoadmapPlanTagSource(planning);

        await planning.AddItemAsync("Sync", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5), tag: "sync");
        await planning.AddItemAsync("Desktop", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 9), tag: "desktop");
        await planning.AddItemAsync("Sync again", new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 12), tag: "sync");

        Assert.Equal(["sync", "desktop"], await source.TagsInUseAsync());
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

        GC.SuppressFinalize(this);
    }
}
