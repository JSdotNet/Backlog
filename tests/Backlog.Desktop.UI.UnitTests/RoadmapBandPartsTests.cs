using System.Globalization;

using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.UI;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.AspNetCore.Components.Web;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A plan filed under two repositories, in the band: each repository draws its own part,
/// a drag on one part moves the whole item, and a pin dragged on a part that does not end
/// last moves only the part that does (ADR 0013, rulings 4 and 5 as amended on 2026-10-07).
/// Over the real stored plan and the real backlog, so the window the band stores is the one
/// its parts make.
/// <para>
/// The plan holds a 7-point task in <c>backlog</c> and a 7-point task in <c>fincent</c> that
/// waits on it. Nothing is measured, so both go at Mine's 7 points a week. The band is opened
/// on Monday 12 October 2026.
/// </para>
/// </summary>
public sealed class RoadmapBandPartsTests : RoadmapBandHarness
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static DateOnly October(int day) => new(2026, 10, day);

    private const string HandOver =
        "# One\n`task` `+plan-a` `id:one` `effort:7` `repo:backlog`\n\n"
        + "# Two\n`task` `+plan-a` `id:two` `after:one` `effort:7` `repo:fincent`\n";

    /// <summary>Writes the plan's tasks — filed, as the app files them, under the
    /// <c>owner/name</c> ids of the configured repositories — then lays the plan out on the
    /// roadmap the way an import does: effort-placed, from the harness's 25 September.</summary>
    private async Task<RoadmapItemDto> ImportedAsync(string tasks)
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Fincent");

        var entries = TasksTestHost.EntriesFor(TasksTestHost.RepositoryFor(Settings), new ConfiguredRepositories(RepositorySettings));
        var written = await entries.ImportPlanAsync(
            tasks,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(written.IsSuccess, written.IsFailure ? written.Error.Message : null);

        var imported = await Planning.ImportPlanItemsAsync(
            [new PlanImportEntryDto("Plan A", "plan-a", RepositoryAliases: ["backlog", "fincent"])],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(imported.IsSuccess);

        return await StoredAsync();
    }

    private async Task<RoadmapItemDto> StoredAsync() =>
        Assert.Single((await Planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items);

    private static IRenderedComponent<RoadmapBand> OpenedOnMonday(BunitContext context)
    {
        var band = context.Render<RoadmapBand>(parameters => parameters.Add(component => component.Today, Monday));
        band.WaitForElement("[data-testid='roadmap-timeline']");
        return band;
    }

    private static RoadmapBar BarIn(IRenderedComponent<RoadmapBand> band, string alias) =>
        Assert.Single(
            band.FindComponent<RoadmapTimeline>().Instance.Bars,
            bar => bar.RowId.StartsWith($"{alias}::", StringComparison.Ordinal));

    /// <summary>Each band draws only its part, and the stored window is the parts' envelope —
    /// the keep-up writer and the band reading a task filed by <c>owner/name</c> into the same
    /// part.</summary>
    [Fact]
    public async Task Each_band_draws_its_own_part_and_the_item_stores_their_envelope()
    {
        await ImportedAsync(HandOver);

        using var context = Context();
        var band = OpenedOnMonday(context);

        band.WaitForAssertion(() =>
        {
            Assert.Equal((Monday, October(16)), (BarIn(band, "backlog").Start, BarIn(band, "backlog").End));
            Assert.Equal((October(19), October(23)), (BarIn(band, "fincent").Start, BarIn(band, "fincent").End));
        });

        var stored = await StoredAsync();
        Assert.Equal((Monday, October(23)), (stored.Start, stored.End));
        Assert.Equal(ImportPlacement.Effort, stored.PlacedByImport);
    }

    /// <summary>Parts that wait on nothing run side by side, and the window stored on opening
    /// is the one they are drawn over: the keep-up writer files a task by its repository's
    /// <c>owner/name</c> into the same part the band draws it in, not into the first.</summary>
    [Fact]
    public async Task Parts_that_wait_on_nothing_are_stored_over_the_window_the_bands_draw()
    {
        await ImportedAsync(HandOver.Replace(" `after:one`", string.Empty, StringComparison.Ordinal));

        using var context = Context();
        var band = OpenedOnMonday(context);

        band.WaitForAssertion(() =>
        {
            Assert.Equal((Monday, October(16)), (BarIn(band, "backlog").Start, BarIn(band, "backlog").End));
            Assert.Equal((Monday, October(16)), (BarIn(band, "fincent").Start, BarIn(band, "fincent").End));
        });

        var stored = await StoredAsync();
        Assert.Equal((Monday, October(16)), (stored.Start, stored.End));
    }

    /// <summary>AC5: dragging the fincent part a week moves the whole item a week from the
    /// window it is drawn over. The item is then placed by hand, and both bands draw the one
    /// moved window.</summary>
    [Fact]
    public async Task Dragging_one_part_moves_the_whole_item_and_both_bands_then_draw_the_moved_window()
    {
        var item = await ImportedAsync(HandOver);

        using var context = Context();
        var band = OpenedOnMonday(context);
        band.WaitForAssertion(() => Assert.Equal(October(23), BarIn(band, "fincent").End));

        var timeline = band.FindComponent<RoadmapTimeline>();
        var part = BarIn(band, "fincent");
        await band.InvokeAsync(() => timeline.Instance.OnBarChanged.InvokeAsync(
            new RoadmapChange(part.Id, part.RowId, part.Start.AddDays(7), part.End.AddDays(7), RoadmapDrag.Move)));

        var stored = await StoredAsync();
        Assert.Equal((October(19), October(30)), (stored.Start, stored.End));
        Assert.Null(stored.PlacedByImport);

        band.WaitForAssertion(() =>
        {
            Assert.Equal((October(19), October(30)), (BarIn(band, "backlog").Start, BarIn(band, "backlog").End));
            Assert.Equal((October(19), October(30)), (BarIn(band, "fincent").Start, BarIn(band, "fincent").End));
        });
        Assert.Equal(item.Id, RoadmapPlanView.NodeIdOf(BarIn(band, "backlog").Id));
    }

    /// <summary>AC10: work in flight — backlog's part begun today, forecast to Friday the 16th,
    /// and fincent's after it to Friday the 23rd. Dragging the end of backlog's part three
    /// days out pins the item's end three days after the drawn end, on the 26th; fincent's
    /// part is drawn to the pin and backlog's keeps its forecast.</summary>
    [Fact]
    public async Task Pulling_the_end_of_the_part_that_does_not_end_last_pins_the_item_and_moves_only_the_latest_part()
    {
        await ImportedAsync(HandOver.Replace("`effort:7` `repo:backlog`", "`effort:7` `repo:backlog` `!in-progress` `started:2026-10-12`", StringComparison.Ordinal));

        using var context = Context();
        var band = OpenedOnMonday(context);
        band.WaitForAssertion(() => Assert.Equal(October(23), BarIn(band, "fincent").End));

        var timeline = band.FindComponent<RoadmapTimeline>();
        var earlier = BarIn(band, "backlog");
        Assert.True(earlier.Locked);
        Assert.True(earlier.EndResizable);
        Assert.Equal(October(16), earlier.End);

        await band.InvokeAsync(() => timeline.Instance.OnBarChanged.InvokeAsync(
            new RoadmapChange(earlier.Id, earlier.RowId, earlier.Start, earlier.End.AddDays(3), RoadmapDrag.ResizeEnd)));

        var stored = await StoredAsync();
        Assert.True(stored.EndPinned);
        Assert.Equal(October(26), stored.End);

        band.WaitForAssertion(() =>
        {
            Assert.Equal(October(16), BarIn(band, "backlog").End);
            Assert.Equal(October(26), BarIn(band, "fincent").End);
        });
    }

    /// <summary>QA fix round 1: a part dropped a week later by keyboard is announced at the
    /// window the whole item lands on — 12 to 23 October moved to 19 to 30 — and not at the
    /// part's own 26 to 30.</summary>
    [Fact]
    public async Task A_dropped_part_is_announced_at_the_window_the_item_lands_on()
    {
        await ImportedAsync(HandOver);

        using var context = Context();
        var band = OpenedOnMonday(context);
        band.WaitForAssertion(() => Assert.Equal(October(23), BarIn(band, "fincent").End));

        var part = BarIn(band, "fincent");
        BodyOf(band, part.Id).KeyDown(new KeyboardEventArgs { Key = " " });
        BodyOf(band, part.Id).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        BodyOf(band, part.Id).KeyDown(new KeyboardEventArgs { Key = " " });

        band.WaitForAssertion(() => Assert.Equal(
            $"Dropped Plan A at {Label(October(19))} to {Label(October(30))}.",
            band.Find("[data-testid='roadmap-timeline-announcement']").TextContent));

        var stored = await StoredAsync();
        Assert.Equal((October(19), October(30)), (stored.Start, stored.End));
    }

    /// <summary>QA fix round 1: the end of in-flight work's earlier part pulled a day out pins
    /// the item's end a day after its drawn end of the 23rd, and the drop is announced at the
    /// item's window — its drawn start to that pin — not at the part's own 12 to 17.</summary>
    [Fact]
    public async Task A_dropped_end_of_an_earlier_part_is_announced_at_the_items_pinned_window()
    {
        await ImportedAsync(HandOver.Replace("`effort:7` `repo:backlog`", "`effort:7` `repo:backlog` `!in-progress` `started:2026-10-12`", StringComparison.Ordinal));

        using var context = Context();
        var band = OpenedOnMonday(context);
        band.WaitForAssertion(() => Assert.Equal(October(23), BarIn(band, "fincent").End));

        var part = BarIn(band, "backlog");
        BodyOf(band, part.Id).KeyDown(new KeyboardEventArgs { Key = " " });
        BodyOf(band, part.Id).KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        BodyOf(band, part.Id).KeyDown(new KeyboardEventArgs { Key = " " });

        band.WaitForAssertion(() => Assert.Equal(
            $"Dropped Plan A at {Label(Monday)} to {Label(October(24))}.",
            band.Find("[data-testid='roadmap-timeline-announcement']").TextContent));

        var stored = await StoredAsync();
        Assert.True(stored.EndPinned);
        Assert.Equal(October(24), stored.End);
    }

    private static AngleSharp.Dom.IElement BodyOf(IRenderedComponent<RoadmapBand> band, string barId) =>
        band.FindComponents<RoadmapTimelineBar>().Single(bar => bar.Instance.Bar.Id == barId).Find(".roadmap-bar__body");

    private static string Label(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>The configured repositories as the Tasks side resolves a <c>repo:</c> value
    /// against them: by alias or full name, to the <c>owner/name</c> id an entry stores.</summary>
    private sealed class ConfiguredRepositories(GitHubSettingsStore configured) : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories =>
            [.. configured.Current.Repositories.Select(repository => new TasksRepositoryRef(repository.Alias, repository.Owner, repository.Name))];

        public TasksRepositoryRef? Resolve(string name) =>
            Repositories.FirstOrDefault(repository =>
                string.Equals(repository.Alias, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(repository.Id, name, StringComparison.OrdinalIgnoreCase));

        public Result<TasksRepositoryRef> Register(string name) => new TasksRepositoryRef(name, name, name);
    }
}
