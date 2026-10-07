using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The keep-up projection where the band reads the plan: opening the roadmap and a task
/// changing store the window an effort-placed item's unfinished work makes from today; a
/// pace change redraws it and stores nothing (ADR 0013, ruling 5 as amended on 2026-09-27;
/// local ADR 0018). Over the real stored plan and the real backlog the module gathers
/// from, so what the band stored is read back from the store.
/// <para>
/// The import runs on the harness clock, Friday 25 September 2026, and lays the item out
/// from there; the band is opened on Monday 12 October, when that window is behind it.
/// Nothing is measured, so <c>backlog</c> goes at Mine's 7 points a week. Each test waits
/// for the bar first: the band stores before it reads the plan it draws, so a drawn window
/// is one already stored.
/// </para>
/// </summary>
public sealed class RoadmapBandKeepUpTests : RoadmapBandHarness
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Friday = new(2026, 10, 16);

    private const string OneTask = "# One\n`task` `+plan-a` `effort:7`\n";

    /// <summary>Writes the plan's tasks, then lays the plan out on the roadmap the way an
    /// import does — effort-placed, from the harness's 25 September.</summary>
    private async Task<RoadmapItemDto> ImportedAsync(string tasks)
    {
        Configure("JSdotNet/Backlog");

        var written = await TasksTestHost.EntriesFor(Settings).ImportPlanAsync(
            tasks,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(written.IsSuccess);

        var imported = await Planning.ImportPlanItemsAsync(
            [new PlanImportEntryDto("Plan A", "plan-a", RepositoryAliases: ["backlog"])],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(imported.IsSuccess);

        var item = await StoredAsync();
        Assert.True(item.End < Monday, "The import placed the item before the band is opened.");
        return item;
    }

    private async Task<RoadmapItemDto> StoredAsync() =>
        Assert.Single((await Planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items);

    private static IRenderedComponent<RoadmapBand> OpenedOn(BunitContext context, DateOnly today)
    {
        var band = context.Render<RoadmapBand>(parameters => parameters.Add(component => component.Today, today));
        band.WaitForElement("[data-testid='roadmap-timeline']");
        return band;
    }

    private static (DateOnly Start, DateOnly End) DrawnWindow(IRenderedComponent<RoadmapBand> band, Guid itemId)
    {
        var bars = band.FindComponent<RoadmapTimeline>().Instance.Bars
            .Where(bar => RoadmapPlanView.NodeIdOf(bar.Id) == itemId)
            .ToList();
        Assert.NotEmpty(bars);
        return (bars.Min(bar => bar.Start), bars.Max(bar => bar.End));
    }

    [Fact]
    public async Task Opening_the_roadmap_stores_the_window_the_work_makes_from_today_and_draws_it()
    {
        var item = await ImportedAsync(OneTask);

        using var context = Context();
        var band = OpenedOn(context, Monday);

        // Seven points at seven a week, from today: Monday to Friday — drawn, and stored.
        band.WaitForAssertion(() => Assert.Equal((Monday, Friday), DrawnWindow(band, item.Id)));
        var stored = await StoredAsync();
        Assert.Equal((Monday, Friday), (stored.Start, stored.End));
        Assert.Equal(ImportPlacement.Effort, stored.PlacedByImport);
    }

    [Fact]
    public async Task Opening_it_again_the_same_day_stores_nothing()
    {
        var item = await ImportedAsync(OneTask);
        using (var first = Context())
        {
            var opened = OpenedOn(first, Monday);
            opened.WaitForAssertion(() => Assert.Equal((Monday, Friday), DrawnWindow(opened, item.Id)));
        }

        var changes = 0;
        Planning.Changed += () => changes++;

        using var context = Context();
        var band = OpenedOn(context, Monday);
        band.WaitForAssertion(() => Assert.Equal((Monday, Friday), DrawnWindow(band, item.Id)));

        Assert.Equal(0, changes);
        var stored = await StoredAsync();
        Assert.Equal((Monday, Friday), (stored.Start, stored.End));
    }

    [Fact]
    public async Task A_task_marked_done_stores_the_shorter_window()
    {
        var item = await ImportedAsync(OneTask + "\n# Two\n`task` `+plan-a` `effort:7`\n");

        using var context = Context();
        var band = OpenedOn(context, Monday);

        // Fourteen points: two working weeks from today.
        band.WaitForAssertion(() => Assert.Equal(new DateOnly(2026, 10, 23), DrawnWindow(band, item.Id).End));
        Assert.Equal(new DateOnly(2026, 10, 23), (await StoredAsync()).End);

        var entries = TasksTestHost.EntriesFor(Settings);
        var one = Assert.Single(
            await entries.ListAsync(TestContext.Current.CancellationToken),
            task => task.Title == "One");
        var done = await entries.SaveFromTextAsync(
            one.Id,
            "# One\n`task` `+plan-a` `effort:7` `!done` `completed:2026-10-12`\n",
            one.Order,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(done.IsSuccess);

        WorkChanges.Raise();

        // Seven points left at seven a week: this week.
        band.WaitForAssertion(() => Assert.Equal(Friday, DrawnWindow(band, item.Id).End));
        var stored = await StoredAsync();
        Assert.Equal(Friday, stored.End);
        Assert.Equal(ImportPlacement.Effort, stored.PlacedByImport);
    }

    [Fact]
    public async Task The_editor_offers_no_update_from_tasks_and_a_save_that_leaves_the_dates_keeps_the_placement()
    {
        var item = await ImportedAsync(OneTask + "\n# Two\n`task` `+plan-a` `effort:7`\n");

        using var context = Context();
        var band = OpenedOn(context, Monday);
        band.WaitForAssertion(() => Assert.Equal(new DateOnly(2026, 10, 23), DrawnWindow(band, item.Id).End));

        var timeline = band.FindComponent<RoadmapTimeline>();
        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(item.Id.ToString()));
        band.WaitForElement("[data-testid='roadmap-item-effort-total']");

        // The window is laid out from the tasks already; there is nothing to offer.
        Assert.Empty(band.FindAll("[data-testid='roadmap-item-effort-proposal']"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-editor-update-from-tasks']"));

        band.Find("[data-testid='roadmap-editor-save']").Click();

        band.WaitForAssertion(() => Assert.Empty(band.FindAll("[data-testid='roadmap-editor']")));
        var stored = await StoredAsync();
        Assert.Equal((Monday, new DateOnly(2026, 10, 23)), (stored.Start, stored.End));
        Assert.Equal(ImportPlacement.Effort, stored.PlacedByImport);
    }

    /// <summary>Marks task "One" done, the way the Tasks pane or an agent writes it, and
    /// tells the band a task changed.</summary>
    private async Task MarkOneDoneAsync()
    {
        var entries = TasksTestHost.EntriesFor(Settings);
        var one = Assert.Single(
            await entries.ListAsync(TestContext.Current.CancellationToken),
            task => task.Title == "One");
        var done = await entries.SaveFromTextAsync(
            one.Id,
            "# One\n`task` `+plan-a` `effort:7` `!done` `completed:2026-10-12`\n",
            one.Order,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(done.IsSuccess);

        WorkChanges.Raise();
    }

    private static async Task OpenTheEditorOnAsync(IRenderedComponent<RoadmapBand> band, Guid itemId)
    {
        var timeline = band.FindComponent<RoadmapTimeline>();
        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(itemId.ToString()));
        band.WaitForElement("[data-testid='roadmap-item-effort-total']");
    }

    /// <summary>
    /// A task written while the item is open in the editor would move the stored window
    /// under fields still holding the old one, and a Save of only the title would then
    /// send the old window back — a hand move, ending the effort placement. So the
    /// window is kept up once the dialog closes, over what the Save stored.
    /// </summary>
    [Fact]
    public async Task A_task_done_while_the_item_is_open_is_kept_up_after_a_save_of_the_title_alone()
    {
        var item = await ImportedAsync(OneTask + "\n# Two\n`task` `+plan-a` `effort:7`\n");

        using var context = Context();
        var band = OpenedOn(context, Monday);
        band.WaitForAssertion(() => Assert.Equal(new DateOnly(2026, 10, 23), DrawnWindow(band, item.Id).End));
        await OpenTheEditorOnAsync(band, item.Id);

        await MarkOneDoneAsync();
        band.Find("[data-testid=\"roadmap-editor-title\"] input").Input("Plan A, renamed");
        band.Find("[data-testid='roadmap-editor-save']").Click();

        band.WaitForAssertion(() => Assert.Empty(band.FindAll("[data-testid='roadmap-editor']")));
        band.WaitForAssertion(() => Assert.Equal(Friday, DrawnWindow(band, item.Id).End));
        var stored = await StoredAsync();
        Assert.Equal("Plan A, renamed", stored.Title);
        Assert.Equal(ImportPlacement.Effort, stored.PlacedByImport);
        Assert.Equal(DrawnWindow(band, item.Id), (stored.Start, stored.End));
    }

    [Fact]
    public async Task A_task_done_while_the_item_is_open_is_kept_up_when_the_dialog_is_cancelled()
    {
        var item = await ImportedAsync(OneTask + "\n# Two\n`task` `+plan-a` `effort:7`\n");

        using var context = Context();
        var band = OpenedOn(context, Monday);
        band.WaitForAssertion(() => Assert.Equal(new DateOnly(2026, 10, 23), DrawnWindow(band, item.Id).End));
        await OpenTheEditorOnAsync(band, item.Id);

        await MarkOneDoneAsync();
        band.Find("[data-testid='roadmap-editor-cancel']").Click();

        band.WaitForAssertion(() => Assert.Equal(Friday, DrawnWindow(band, item.Id).End));
        var stored = await StoredAsync();
        Assert.Equal(ImportPlacement.Effort, stored.PlacedByImport);
        Assert.Equal(DrawnWindow(band, item.Id), (stored.Start, stored.End));
    }

    [Fact]
    public async Task A_pace_change_redraws_the_bar_and_stores_nothing()
    {
        var item = await ImportedAsync(OneTask);

        using var context = Context();
        var band = OpenedOn(context, Monday);
        band.WaitForElement(".roadmap-pace input");
        band.WaitForAssertion(() => Assert.Equal((Monday, Friday), DrawnWindow(band, item.Id)));

        band.Find("[data-testid='roadmap-pace-manual'] input").Change("14");

        // Seven points at fourteen a week: half a working week, Monday to Wednesday.
        band.WaitForAssertion(() => Assert.Equal((Monday, new DateOnly(2026, 10, 14)), DrawnWindow(band, item.Id)));
        var stored = await StoredAsync();
        Assert.Equal((Monday, Friday), (stored.Start, stored.End));
    }
}
