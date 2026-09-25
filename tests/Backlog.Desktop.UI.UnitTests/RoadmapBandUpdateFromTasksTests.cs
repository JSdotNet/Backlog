using System.Globalization;

using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// "Update from tasks" in the item editor: offered while an imported item's window is
/// still sized by effort and its tasks now make a different one, and applied against
/// the real stored plan. The rollup is a fixed stand-in — what the backlog adapter
/// gathers is pinned beside the adapter — so a test can say what the tasks add up to
/// without writing a backlog; the plan behind the band is the real one, so what the
/// action stored is read back from the store.
/// </summary>
public sealed class RoadmapBandUpdateFromTasksTests : RoadmapBandHarness
{
    private sealed class FixedEffort(int totalEffort) : IRoadmapItemRollup
    {
        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RoadmapItemRollupDto(
                [new RoadmapGatheredLink("task-1", "Gathered task", totalEffort, RollupOrigin.Tag)],
                []));

        public Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>>(new Dictionary<Guid, RoadmapItemRollupDto>());
    }

    private const string Button = "[data-testid=\"roadmap-editor-update-from-tasks\"]";
    private const string ProposalLine = "[data-testid=\"roadmap-item-effort-proposal\"]";

    private BunitContext ContextGathering(int totalEffort)
    {
        var context = Context();
        context.Services.AddSingleton<IRoadmapItemRollup>(new FixedEffort(totalEffort));
        return context;
    }

    /// <summary>Two imported plans, the second waiting on the first — both placed by
    /// effort from today, the first over the default span since nothing was gathered
    /// when it was imported.</summary>
    private async Task<(RoadmapItemDto First, RoadmapItemDto Second)> ImportedAsync()
    {
        var imported = await Planning.ImportPlanItemsAsync(
            [
                new PlanImportEntryDto("Plan A", "plan-a", RepositoryAliases: ["backlog"]),
                new PlanImportEntryDto("Plan B", "plan-b", RepositoryAliases: ["backlog"], After: ["plan-a"])
            ],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(imported.IsSuccess);

        return (await StoredAsync("plan-a"), await StoredAsync("plan-b"));
    }

    private async Task<RoadmapItemDto> StoredAsync(string tag) =>
        Assert.Single(
            (await Planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items,
            item => item.Tag == tag);

    private static async Task OpenAsync(IRenderedComponent<RoadmapBand> band, Guid itemId)
    {
        var timeline = band.FindComponent<RoadmapTimeline>();
        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(itemId.ToString()));
        band.WaitForElement("[data-testid=\"roadmap-editor\"]");
    }

    [Fact]
    public async Task An_effort_placed_item_whose_tasks_changed_is_offered_the_update()
    {
        var (first, _) = await ImportedAsync();

        using var context = ContextGathering(8);
        var band = Drawn(context);
        await OpenAsync(band, first.Id);

        band.WaitForElement(Button);
        var line = band.Find(ProposalLine).TextContent;
        Assert.Contains("8 points", line, StringComparison.Ordinal);
        Assert.Contains(
            $"ends {first.Start.AddDays(7).ToString("d MMM yyyy", CultureInfo.InvariantCulture)} "
            + $"instead of {first.End.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}",
            line,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hand_moved_item_is_not_offered_the_update()
    {
        var (first, _) = await ImportedAsync();
        var moved = await Planning.RescheduleItemAsync(
            first.Id, first.Start, first.End.AddDays(2), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(moved.IsSuccess);

        using var context = ContextGathering(8);
        var band = Drawn(context);
        await OpenAsync(band, first.Id);

        band.WaitForElement("[data-testid=\"roadmap-item-effort-total\"]");
        Assert.Empty(band.FindAll(Button));
        Assert.Empty(band.FindAll(ProposalLine));
    }

    [Fact]
    public async Task An_item_whose_tasks_already_make_its_window_is_not_offered_the_update()
    {
        var (first, _) = await ImportedAsync();

        // Five points at a point a day is the five days the import already gave it.
        using var context = ContextGathering(5);
        var band = Drawn(context);
        await OpenAsync(band, first.Id);

        band.WaitForElement("[data-testid=\"roadmap-item-effort-total\"]");
        Assert.Empty(band.FindAll(Button));
    }

    [Fact]
    public async Task Taking_the_update_stores_the_new_end_and_names_what_now_overlaps()
    {
        var (first, second) = await ImportedAsync();

        using var context = ContextGathering(8);
        var band = Drawn(context);
        await OpenAsync(band, first.Id);

        band.WaitForElement(Button).Click();

        band.WaitForAssertion(() =>
            Assert.Contains("Now overlaps: Plan B", band.Find("[data-testid=\"roadmap-editor-overlaps\"]").TextContent, StringComparison.Ordinal));

        var relengthened = await StoredAsync("plan-a");
        Assert.Equal(first.Start, relengthened.Start);
        Assert.Equal(first.Start.AddDays(7), relengthened.End);
        Assert.Equal(ImportPlacement.Effort, relengthened.PlacedByImport);

        // Named, not moved.
        var dependent = await StoredAsync("plan-b");
        Assert.Equal(second.Start, dependent.Start);
        Assert.Equal(second.End, dependent.End);

        // The dialog shows the stored window, and the offer has gone with the mismatch.
        band.WaitForAssertion(() => Assert.Empty(band.FindAll(Button)));
        Assert.Equal(
            first.Start.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            band.Find("[data-testid=\"roadmap-editor-end\"] input").GetAttribute("value"));
    }
}
