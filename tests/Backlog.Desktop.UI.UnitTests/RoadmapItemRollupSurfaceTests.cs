using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.UI;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the roadmap item editor draws for the work an item has already gathered: the
/// two lists with their origins, the honest effort total beside the count it does not
/// hide, and the empty state when nothing is gathered. The rollup is handed in as a
/// parameter, so these assert the surface directly without a plan behind it.
/// </summary>
public sealed class RoadmapItemRollupSurfaceTests : IDisposable
{
    private readonly BunitContext _context = new();

    public RoadmapItemRollupSurfaceTests()
    {
        // The modal reaches for JS to trap focus; nothing asserted here depends on it.
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static RoadmapItemDto Item() =>
        new(
            Guid.NewGuid(),
            "Ship the thing",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            PlanningPriority.Medium,
            [],
            Lane: null,
            BacklogEntryId: null,
            DependsOn: [],
            Tag: "sync");

    private IRenderedComponent<RoadmapItemEditor> Render(RoadmapItemDto? item, RoadmapItemRollupDto rollup) =>
        _context.Render<RoadmapItemEditor>(parameters => parameters
            .Add(editor => editor.Open, true)
            .Add(editor => editor.Item, item)
            .Add(editor => editor.Rollup, rollup));

    [Fact]
    public void The_two_lists_show_every_gathered_thing_with_its_origin_and_effort()
    {
        var rollup = new RoadmapItemRollupDto(
            [
                new RoadmapGatheredLink("id1", "Wire the store", 5, RollupOrigin.Direct),
                new RoadmapGatheredLink("id2", "Tagged work", null, RollupOrigin.Tag)
            ],
            [
                new RoadmapGatheredLink("k1", "Sync design", 13, RollupOrigin.Both)
            ]);

        var editor = Render(Item(), rollup);

        var backlog = editor.Find("[data-testid='roadmap-item-linked-backlog']").TextContent;
        Assert.Contains("Wire the store", backlog, StringComparison.Ordinal);
        Assert.Contains("linked", backlog, StringComparison.Ordinal);
        Assert.Contains("5 points", backlog, StringComparison.Ordinal);
        Assert.Contains("Tagged work", backlog, StringComparison.Ordinal);
        Assert.Contains("by tag", backlog, StringComparison.Ordinal);
        Assert.Contains("not estimated", backlog, StringComparison.Ordinal);

        var knowledge = editor.Find("[data-testid='roadmap-item-linked-knowledge']").TextContent;
        Assert.Contains("Sync design", knowledge, StringComparison.Ordinal);
        Assert.Contains("linked & by tag", knowledge, StringComparison.Ordinal);
        Assert.Contains("13 points", knowledge, StringComparison.Ordinal);
    }

    [Fact]
    public void The_effort_total_states_both_the_sum_and_the_work_that_registered_none()
    {
        var rollup = new RoadmapItemRollupDto(
            [
                new RoadmapGatheredLink("id1", "Wire the store", 5, RollupOrigin.Direct),
                new RoadmapGatheredLink("id2", "Tagged work", null, RollupOrigin.Tag)
            ],
            [
                new RoadmapGatheredLink("k1", "Sync design", 13, RollupOrigin.Both)
            ]);

        var total = Render(Item(), rollup).Find("[data-testid='roadmap-item-effort-total']").TextContent;

        // 5 + 13, with the null left out of the sum but named beside it — the total is
        // never allowed to read as smaller than the work is.
        Assert.Contains("18 story points", total, StringComparison.Ordinal);
        Assert.Contains("1 of 3 gathered things registered no estimate", total, StringComparison.Ordinal);
        Assert.Contains("the real effort is larger", total, StringComparison.Ordinal);
    }

    [Fact]
    public void When_everything_is_estimated_the_total_says_so_rather_than_a_count()
    {
        var rollup = new RoadmapItemRollupDto(
            [new RoadmapGatheredLink("id1", "Wire the store", 5, RollupOrigin.Direct)],
            []);

        var total = Render(Item(), rollup).Find("[data-testid='roadmap-item-effort-total']").TextContent;

        Assert.Contains("5 story points", total, StringComparison.Ordinal);
        Assert.Contains("registered an estimate", total, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_that_has_gathered_nothing_shows_the_empty_state()
    {
        var editor = Render(Item(), RoadmapItemRollupDto.Empty);

        Assert.NotNull(editor.Find("[data-testid='roadmap-item-rollup-empty']"));
        Assert.Empty(editor.FindAll("[data-testid='roadmap-item-linked-backlog']"));
        Assert.Empty(editor.FindAll("[data-testid='roadmap-item-linked-knowledge']"));
        Assert.Empty(editor.FindAll("[data-testid='roadmap-item-effort-total']"));
    }

    [Fact]
    public void A_new_item_has_nothing_to_roll_up_so_the_section_is_absent()
    {
        var editor = Render(item: null, RoadmapItemRollupDto.Empty);

        Assert.Empty(editor.FindAll("[data-testid='roadmap-item-rollup']"));
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
