using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// A window still sized by its effort is read the way the keep-up projection lays it out
/// (<see cref="RoadmapProjection"/>; ADR 0013, ruling 5 as amended on 2026-09-27 and
/// 2026-10-07): from the later of today and the day after what it waits on, part by part,
/// for the effort its tasks register now at the pace in use now. A reader that has not
/// opened the roadmap today therefore reports the dates the roadmap would store, and a pace
/// change redraws every such window without writing the plan.
/// </summary>
public class EffortWindowTests
{
    private static readonly DateOnly Start = new(2026, 3, 2);

    private static readonly PacesInUseDto SevenAWeek = new(7m, new Dictionary<string, decimal>());

    private static RoadmapItemDto Item(
        int days = 5,
        ImportPlacement? placement = ImportPlacement.Effort,
        params string[] repositories) =>
        new(
            Guid.NewGuid(),
            "Plan",
            Start,
            Start.AddDays(days - 1),
            PlanningPriority.Medium,
            repositories,
            null,
            null,
            [],
            PlacedByImport: placement);

    private static RoadmapItemRollupDto Gathers(int effort, RoadmapProgress progress = RoadmapProgress.Ready, params string[] repositories) =>
        new([new RoadmapGatheredLink("task-1", "Task", effort, RollupOrigin.Tag, progress, RepositoryIds: repositories)], []);

    private static PacesInUseDto Paces(decimal global, params (string Alias, decimal Pace)[] repositories) =>
        new(global, repositories.ToDictionary(pair => pair.Alias, pair => pair.Pace, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void AnEffortPlacedItemStartingToday_IsReadAtThePaceInUse_FromToday()
    {
        var item = Item(days: 14);

        var derived = EffortWindow.Derive(item, Gathers(14), Paces(14m), today: Start);

        Assert.Equal(Start, derived.Start);
        Assert.Equal(Start.AddDays(4), derived.End); // 14 points at 14 a week: Monday to Friday
        Assert.Equal(ImportPlacement.Effort, derived.PlacedByImport);
    }

    /// <summary>AC8: an item nobody has begun is not left in the past — it is read from
    /// today, the way the roadmap would store it on opening.</summary>
    [Fact]
    public void AnEffortPlacedItemNotBegun_WhoseStoredStartHasPassed_IsReadFromToday()
    {
        var today = Start.AddDays(14);

        var derived = EffortWindow.Derive(Item(days: 3), Gathers(7), SevenAWeek, today);

        Assert.Equal((today, today.AddDays(4)), (derived.Start, derived.End));
    }

    /// <summary>A floor — the day after what the item waits on — holds it back too.</summary>
    [Fact]
    public void AnEffortPlacedItem_StartsNoEarlierThanItsFloor()
    {
        var derived = EffortWindow.Derive(Item(days: 3), Gathers(7), SevenAWeek, Start, floor: Start.AddDays(7));

        Assert.Equal((Start.AddDays(7), Start.AddDays(11)), (derived.Start, derived.End));
    }

    [Fact]
    public void TheDerivedWindowIsTheOneTheImportsOwnPlacementWouldStore()
    {
        var item = Item(days: 3, repositories: "backlog");
        var paces = Paces(7m, ("backlog", 4m));

        var derived = EffortWindow.Derive(item, Gathers(9), paces, Start);
        var (placed, _) = ImportedPlanPlacement.Place(item.Start, due: null, 9, paces.PaceOf("backlog"), paces.Week);

        Assert.Equal(placed.Start, derived.Start);
        Assert.Equal(placed.End, derived.End);
    }

    /// <summary>Each repository's part at its own pace, and the window their envelope —
    /// never the whole item at the slowest of its repositories' paces (AC7).</summary>
    [Fact]
    public void EachPartIsReadAtItsOwnRepositorysPace_AndTheWindowIsTheirEnvelope()
    {
        var paces = Paces(7m, ("backlog", 14m), ("site", 2m));

        // One working week, Monday to Friday; seven, ending on the seventh Friday; two.
        Assert.Equal(5, EffortWindow.Derive(Item(days: 14, repositories: "backlog"), Gathers(14), paces, Start).Days);
        Assert.Equal(47, EffortWindow.Derive(Item(days: 14, repositories: "site"), Gathers(14), paces, Start).Days);
        Assert.Equal(12, EffortWindow.Derive(Item(days: 3), Gathers(14), paces, Start).Days); // the global pace

        // A task filed only in backlog is backlog's part: site holds none and spans it.
        Assert.Equal(5, EffortWindow.Derive(Item(days: 14, repositories: ["backlog", "site"]), Gathers(14, RoadmapProgress.Ready, "backlog"), paces, Start).Days);

        // Filed in both, it counts in full in both: the envelope ends with site's part.
        Assert.Equal(47, EffortWindow.Derive(Item(days: 14, repositories: ["backlog", "site"]), Gathers(14, RoadmapProgress.Ready, "backlog", "site"), paces, Start).Days);
    }

    [Theory]
    [InlineData(ImportPlacement.DueDate)]
    [InlineData(null)]
    public void ADueDateOrAHandPlacedWindow_IsReadAsStored(ImportPlacement? placement)
    {
        var item = Item(days: 10, placement: placement);

        Assert.Same(item, EffortWindow.Derive(item, Gathers(40), Paces(1m), Start.AddDays(30)));
    }

    [Fact]
    public void AnItemThatGathersNoTask_IsReadAsStored()
    {
        var item = Item(days: 10);

        Assert.Same(item, EffortWindow.Derive(item, null, SevenAWeek, Start.AddDays(30)));
        Assert.Same(item, EffortWindow.Derive(item, RoadmapItemRollupDto.Empty, SevenAWeek, Start.AddDays(30)));
    }

    [Fact]
    public void AFinishedItem_IsReadAsStored()
    {
        var item = Item(days: 10);

        Assert.Same(item, EffortWindow.Derive(item, Gathers(1, RoadmapProgress.Done), SevenAWeek, Start.AddDays(30)));
    }

    [Fact]
    public void AnItemWithNothingEstimated_SpansTheDefault()
    {
        var derived = EffortWindow.Derive(Item(days: 30), Gathers(0), SevenAWeek, Start);

        Assert.Equal(5, derived.Days); // one working week: Monday to Friday
    }

    [Fact]
    public void APaceNobodyCouldDivideBy_LeavesTheStoredWindow()
    {
        var item = Item(days: 10);

        Assert.Same(item, EffortWindow.Derive(item, Gathers(3), Paces(0m), Start.AddDays(30)));
    }

    /// <summary>The plan reads through the projection: a successor starts the worked day
    /// after its predecessor's projected end, not where either was stored.</summary>
    [Fact]
    public void APlanIsReadThroughTheProjection_ASuccessorAfterItsPredecessorsProjectedEnd()
    {
        var first = Item(days: 3);
        var second = Item(days: 3) with { DependsOn = [first.Id] };
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto> { [first.Id] = Gathers(7), [second.Id] = Gathers(7) };

        var read = EffortWindow.Derive([second, first], rollups, SevenAWeek, Start);

        Assert.Equal([second.Id, first.Id], read.Select(item => item.Id));
        Assert.Equal((Start, Start.AddDays(4)), (read[1].Start, read[1].End));
        Assert.Equal((Start.AddDays(7), Start.AddDays(11)), (read[0].Start, read[0].End));
    }

    [Fact]
    public async Task APlanIsReadWithEveryEffortPlacedWindowDerived_GatheringOnlyThoseAndReadingThePacesOnce()
    {
        var sized = Item(days: 3);
        var hand = Item(days: 3, placement: null);
        var plan = new RoadmapPlanDto([sized, hand], [], []);
        var rollups = new CountingRollup(Gathers(14));
        var velocity = new FixedVelocity(14);

        var read = await plan.WithDerivedWindowsAsync(rollups, velocity, Start, TestContext.Current.CancellationToken);

        Assert.Equal(5, Assert.Single(read.Items, item => item.Id == sized.Id).Days);
        Assert.Equal(3, Assert.Single(read.Items, item => item.Id == hand.Id).Days);
        Assert.Equal([sized.Id], rollups.Gathered);
        Assert.Equal(1, velocity.Reads);
    }

    [Fact]
    public async Task APlanWithNoEffortPlacedWindow_ReadsNothingMore()
    {
        var plan = new RoadmapPlanDto([Item(placement: null)], [], []);
        var rollups = new CountingRollup(Gathers(14));
        var velocity = new FixedVelocity(14);

        var read = await plan.WithDerivedWindowsAsync(rollups, velocity, Start, TestContext.Current.CancellationToken);

        Assert.Same(plan, read);
        Assert.Empty(rollups.Gathered);
        Assert.Equal(0, velocity.Reads);
    }

    /// <summary>A reader that reports part of the plan still reads each window after what it
    /// waits on: the predecessors it reaches are projected with it, gathering nothing more
    /// than the effort-placed items among them.</summary>
    [Fact]
    public async Task APartOfThePlan_IsReadAfterThePredecessorsItReaches()
    {
        var hand = Item(days: 10, placement: null);
        var first = Item(days: 3) with { DependsOn = [hand.Id] };
        var reported = Item(days: 3) with { DependsOn = [first.Id] };
        var elsewhere = Item(days: 3);
        var plan = new RoadmapPlanDto([elsewhere, hand, first, reported], [], []);
        var rollups = new CountingRollup(Gathers(7));
        var velocity = new FixedVelocity(7);

        var read = await plan.WithDerivedWindowsAsync(rollups, velocity, Start, item => item.Id == reported.Id, TestContext.Current.CancellationToken);

        // The hand-placed window ends on Wednesday the 11th; first runs Thursday the 12th
        // to Wednesday the 18th, and the reported item from Thursday the 19th.
        Assert.Equal(Start.AddDays(17), Assert.Single(read.Items, item => item.Id == reported.Id).Start);
        Assert.Equal(new[] { first.Id, reported.Id }.Order(), rollups.Gathered.Order());
        Assert.Same(elsewhere, Assert.Single(read.Items, item => item.Id == elsewhere.Id));
    }

    // --- Local ADR 0019: a window counts the working week in hours ----------------

    private static readonly DateOnly Saturday = Start.AddDays(-2);

    private static WorkingHours Week(params WorkingDay[] days) =>
        new() { Days = [.. WorkingHours.Week.Select(day => days.FirstOrDefault(own => own.Day == day) ?? WorkingHours.Default.On(day))] };

    private static WorkingHours ShortFriday =>
        Week(new WorkingDay(DayOfWeek.Friday, true, new TimeOnly(9, 0), new TimeOnly(13, 0)));

    private static WorkingHours NoDayWorked =>
        new() { Days = [.. WorkingHours.Week.Select(day => WorkingHours.Default.On(day) with { Working = false })] };

    /// <summary>ADR 0019 Verification 1: 10 points at 5 a week from a Monday end on
    /// the Friday of the next week — ten working days, 85 hours.</summary>
    [Fact]
    public void ATwoWeekBarOnTheDefaultWeek_EndsOnTheNextWeeksFriday()
    {
        var end = EffortWindow.EndFrom(Start, 10, 5m, WorkingHours.Default);

        Assert.Equal(Start.AddDays(11), end);
        Assert.Equal(DayOfWeek.Friday, end.DayOfWeek);

        var worked = Enumerable.Range(0, end.DayNumber - Start.DayNumber + 1)
            .Select(offset => Start.AddDays(offset))
            .Where(day => WorkingHours.Default.IsWorked(day.DayOfWeek))
            .ToList();
        Assert.Equal(10, worked.Count);
        Assert.Equal(85, worked.Sum(day => WorkingHours.Default.WorkedOn(day.DayOfWeek).TotalHours));
    }

    /// <summary>ADR 0019 Verification 2: a window whose start would fall on a Saturday
    /// starts on the following Monday.</summary>
    [Fact]
    public void AWeekendStart_MovesToMonday()
    {
        Assert.Equal(Start, EffortWindow.FirstWorkedDay(Saturday, WorkingHours.Default));
        Assert.Equal(Start, EffortWindow.FirstWorkedDay(Saturday.AddDays(1), WorkingHours.Default));
        Assert.Equal(Start, EffortWindow.FirstWorkedDay(Start, WorkingHours.Default));

        var (window, _) = ImportedPlanPlacement.Place(Saturday, due: null, 5, 5m, WorkingHours.Default);
        Assert.Equal(Start, window.Start);
        Assert.Equal(Start.AddDays(4), window.End);
    }

    /// <summary>ADR 0019 Verification 2, for the daily re-projection: an item the import
    /// placed by its effort, stored with a Saturday start and read that Saturday, is read
    /// from the Monday.</summary>
    [Fact]
    public void AnEffortPlacedItemStoredOnAWeekend_IsReadFromTheNextWorkedDay()
    {
        var item = Item(days: 3) with { Start = Saturday, End = Saturday.AddDays(2) };

        var derived = EffortWindow.Derive(item, Gathers(7), SevenAWeek, today: Saturday);

        Assert.Equal(Start, derived.Start);
        Assert.Equal(Start.AddDays(4), derived.End);
    }

    /// <summary>ADR 0019 Verification 3: nothing gathered spans one working week —
    /// Monday to that Friday.</summary>
    [Fact]
    public void TheDefaultSpan_IsOneWorkingWeek()
    {
        Assert.Equal(Start.AddDays(4), EffortWindow.EndFrom(Start, 0, 7m, WorkingHours.Default));

        // From a Wednesday, one working week runs to the next Tuesday.
        Assert.Equal(Start.AddDays(8), EffortWindow.EndFrom(Start.AddDays(2), 0, 7m, WorkingHours.Default));
    }

    /// <summary>ADR 0019 Verification 4: with Friday 09:00 to 13:00, a plan needing
    /// exactly Monday to Thursday's hours ends on Thursday, and an hour more ends it on
    /// Friday.</summary>
    [Fact]
    public void AShortFriday_Counts()
    {
        var week = ShortFriday; // 38 hours: 34 of them Monday to Thursday

        Assert.Equal(Start.AddDays(3), EffortWindow.EndAfter(Start, TimeSpan.FromHours(34), week));
        Assert.Equal(Start.AddDays(4), EffortWindow.EndAfter(Start, TimeSpan.FromHours(35), week));

        // The same through the pace: 34 points at 38 a week need 34 hours.
        Assert.Equal(Start.AddDays(3), EffortWindow.EndFrom(Start, 34, 38m, week));
        Assert.Equal(Start.AddDays(4), EffortWindow.EndFrom(Start, 35, 38m, week));
    }

    /// <summary>Effort × H ÷ pace, multiplied before dividing: 4 points at 4 a week
    /// need exactly one week of hours, and end on the Friday rather than spilling into
    /// the next Monday.</summary>
    [Fact]
    public void FourPointsAtFourAWeek_IsExactlyOneWeek()
    {
        Assert.Equal(Start.AddDays(4), EffortWindow.EndFrom(Start, 4, 4m, WorkingHours.Default));
        Assert.Equal(Start.AddDays(4), EffortWindow.EndFrom(Start, 3, 3m, Week(new WorkingDay(DayOfWeek.Monday, true, new TimeOnly(9, 0), new TimeOnly(17, 20)))));
    }

    /// <summary>A day counts whole, and the start day is never skipped: a window is
    /// never shorter than its start day.</summary>
    [Fact]
    public void AWindowIsNeverShorterThanItsStartDay()
    {
        Assert.Equal(Start, EffortWindow.EndFrom(Start, 1, 1000m, WorkingHours.Default));
    }

    /// <summary>An absurd total cannot run a window off the calendar.</summary>
    [Fact]
    public void AnAbsurdTotal_IsClampedToTheCalendar()
    {
        Assert.Equal(DateOnly.MaxValue, EffortWindow.EndFrom(Start, int.MaxValue, 0.0001m, WorkingHours.Default));
    }

    /// <summary>ADR 0019 Verification 6: a week with no worked day places as the
    /// default week.</summary>
    [Fact]
    public void AnEmptyWeek_PlacesAsTheDefaultWeek()
    {
        Assert.Equal(
            EffortWindow.EndFrom(Start, 10, 5m, WorkingHours.Default),
            EffortWindow.EndFrom(Start, 10, 5m, NoDayWorked));
        Assert.Equal(Start, EffortWindow.FirstWorkedDay(Saturday, NoDayWorked));

        var paces = SevenAWeek with { Week = NoDayWorked };
        Assert.Equal(Start.AddDays(4), EffortWindow.Derive(Item(days: 3), Gathers(7), paces, Start).End);
    }

    /// <summary>The week a reading uses is the one the paces were read with.</summary>
    [Fact]
    public void TheDerivedWindow_CountsThePacesWeek()
    {
        var paces = Paces(38m) with { Week = ShortFriday };

        Assert.Equal(Start.AddDays(3), EffortWindow.Derive(Item(days: 1), Gathers(34), paces, Start).End);
    }

    // --- Day overrides (local ADR 0019, §§1 and 5) ------------------------------

    private static readonly DateOnly Monday5Oct = new(2026, 10, 5);
    private static readonly DateOnly Wednesday7Oct = new(2026, 10, 7);
    private static readonly DateOnly Friday9Oct = new(2026, 10, 9);
    private static readonly DateOnly Saturday10Oct = new(2026, 10, 10);
    private static readonly DateOnly Monday12Oct = new(2026, 10, 12);

    /// <summary>Requirement "An effort window counts the overrides", ADR 0019
    /// Verification 10 and 11: 7 points at 7 a week from Monday 5 October end on Friday
    /// 9; blocking Wednesday 7 ends them on Monday 12; unblocking Saturday 10 as well
    /// ends them on Saturday 10.</summary>
    [Fact]
    public void ABlockedAndAnUnblockedDate_MoveTheEnd()
    {
        var blocked = WorkingHours.Default.Toggled(Wednesday7Oct);
        var unblocked = blocked.Toggled(Saturday10Oct);

        Assert.Equal(Friday9Oct, EffortWindow.EndFrom(Monday5Oct, 7, 7m, WorkingHours.Default));
        Assert.Equal(Monday12Oct, EffortWindow.EndFrom(Monday5Oct, 7, 7m, blocked));
        Assert.Equal(Saturday10Oct, EffortWindow.EndFrom(Monday5Oct, 7, 7m, unblocked));
    }

    /// <summary>The paces carry the week whole, overrides included, so a derived window
    /// counts them — the path the band, the dashboard and the tools all read.</summary>
    [Fact]
    public void TheDerivedWindow_CountsThePacesOverrides()
    {
        var item = Item(days: 5) with { Start = Monday5Oct, End = Friday9Oct };
        var paces = SevenAWeek with { Week = WorkingHours.Default.Toggled(Wednesday7Oct) };

        Assert.Single(paces.Week.Overrides);
        Assert.Equal(Monday12Oct, EffortWindow.Derive(item, Gathers(7), paces, Monday5Oct).End);
    }

    /// <summary>ADR 0019 Verification 12: a window never starts on a blocked date. One
    /// whose start would fall on a blocked Monday starts on the Tuesday — for the
    /// importer's placement and for a derived window alike.</summary>
    [Fact]
    public void AWindowNeverStartsOnABlockedDate()
    {
        var week = WorkingHours.Default.Toggled(Monday12Oct);
        var tuesday = Monday12Oct.AddDays(1);

        Assert.Equal(tuesday, EffortWindow.FirstWorkedDay(Monday12Oct, week));

        var (placed, _) = ImportedPlanPlacement.Place(Monday12Oct, due: null, 7, 7m, week);
        Assert.Equal(tuesday, placed.Start);
        Assert.Equal(Monday12Oct.AddDays(7), placed.End);

        var item = Item(days: 5) with { Start = Monday12Oct, End = Monday12Oct.AddDays(4) };
        var derived = EffortWindow.Derive(item, Gathers(7), SevenAWeek with { Week = week }, Monday12Oct);
        Assert.Equal(tuesday, derived.Start);
    }

    /// <summary>A window from a blocked weekend's unblocked Saturday starts on it.</summary>
    [Fact]
    public void AWindowStartsOnAnUnblockedSaturday()
    {
        var week = WorkingHours.Default.Toggled(Saturday10Oct);

        Assert.Equal(Saturday10Oct, EffortWindow.FirstWorkedDay(Saturday10Oct, week));
        Assert.Equal(new DateOnly(2026, 10, 15), EffortWindow.EndFrom(Saturday10Oct, 7, 7m, week)); // Saturday, then Monday to Thursday
    }

    /// <summary>ADR 0019 Verification 14: toggling back removes the override, and the
    /// window reads as the pattern alone again.</summary>
    [Fact]
    public void TogglingBack_RemovesTheOverride()
    {
        var back = WorkingHours.Default.Toggled(Wednesday7Oct).Toggled(Wednesday7Oct);

        Assert.Empty(back.Overrides);
        Assert.Equal(Friday9Oct, EffortWindow.EndFrom(Monday5Oct, 7, 7m, back));
    }

    /// <summary>Answers every item with one rollup, and remembers which items it was
    /// asked to gather.</summary>
    private sealed class CountingRollup(RoadmapItemRollupDto rollup) : IRoadmapItemRollup
    {
        public List<Guid> Gathered { get; } = [];

        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default) =>
            Task.FromResult(rollup);

        public Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default)
        {
            Gathered.AddRange(plan.Items.Select(item => item.Id));
            return Task.FromResult<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>>(
                plan.Items.ToDictionary(item => item.Id, _ => rollup));
        }
    }
}
