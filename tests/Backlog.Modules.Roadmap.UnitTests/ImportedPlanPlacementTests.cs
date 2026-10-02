using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// Where an import places a window (ADR 0013, ruling 4): the start from what the item
/// waits on — moved to the next worked day — and the end from <c>due:</c> or from the
/// hours the effort needs at the velocity, counted through the working week (local ADR
/// 0019).
/// </summary>
public class ImportedPlanPlacementTests
{
    private static readonly DateOnly Today = new(2026, 3, 2);

    [Fact]
    public void AnItemWaitingOnNothingStartsToday()
    {
        Assert.Equal(Today, ImportedPlanPlacement.StartAfter([], Today));
    }

    [Fact]
    public void AnItemStartsTheDayAfterItsLatestPredecessorEnds()
    {
        var start = ImportedPlanPlacement.StartAfter(
            [new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 15)],
            Today);

        Assert.Equal(new DateOnly(2026, 3, 21), start);
    }

    [Fact]
    public void APredecessorInThePastStillSetsTheStart_TodayIsOnlyTheFallback()
    {
        Assert.Equal(new DateOnly(2026, 2, 2), ImportedPlanPlacement.StartAfter([new DateOnly(2026, 2, 1)], Today));
    }

    /// <summary>On the default week from a Monday: the hours the effort needs at the
    /// pace, counted through the working days, the weekend inside the window and adding
    /// nothing (local ADR 0019).</summary>
    [Theory]
    [InlineData(0, 7, 5)]     // nothing estimated gathered: one working week, Monday to Friday
    [InlineData(10, 7, 10)]   // 60.7 hours: into the second Wednesday
    [InlineData(3, 14, 2)]    // 9.1 hours: a day and a bit
    [InlineData(1, 28, 1)]    // 1.5 hours: a window is never shorter than its start day
    [InlineData(5, 3.5, 10)]
    [InlineData(4, 4, 5)]     // a week's pace is a working week, not a hair over
    [InlineData(20, 10, 12)]  // two working weeks: the next Friday
    [InlineData(1, 3, 2)]     // 14.2 hours
    public void LengthIsTheHoursTheEffortNeeds_CountedThroughTheWorkingWeek(int effort, double velocity, int days)
    {
        var (window, placement) = ImportedPlanPlacement.Place(Today, due: null, effort, (decimal)velocity, WorkingHours.Default);

        Assert.Equal(Today, window.Start);
        Assert.Equal(days, window.Days);
        Assert.Equal(ImportPlacement.Effort, placement);
    }

    [Fact]
    public void WithoutADueDate_TheWindowSpansTheLength_BothEndsInclusive()
    {
        var (window, placement) = ImportedPlanPlacement.Place(Today, due: null, gatheredEffort: 6, velocity: 14, WorkingHours.Default);

        Assert.Equal(Today, window.Start);
        Assert.Equal(new DateOnly(2026, 3, 4), window.End);
        Assert.Equal(3, window.Days);
        Assert.Equal(ImportPlacement.Effort, placement);
    }

    /// <summary>ADR 0019 Verification 3: nothing gathered spans one working week.</summary>
    [Fact]
    public void NothingGathered_PlacesOneWorkingWeek()
    {
        var (window, placement) = ImportedPlanPlacement.Place(Today, due: null, gatheredEffort: 0, velocity: 7, WorkingHours.Default);

        Assert.Equal(Today, window.Start);
        Assert.Equal(new DateOnly(2026, 3, 6), window.End);
        Assert.Equal(ImportPlacement.Effort, placement);
    }

    /// <summary>ADR 0019 Verification 2, for a successor: a predecessor ending on a
    /// Friday puts the next start on the Saturday, and the window opens on the
    /// Monday.</summary>
    [Fact]
    public void ASuccessorOfAFridayEnd_StartsOnTheMonday()
    {
        var start = ImportedPlanPlacement.StartAfter([new DateOnly(2026, 3, 6)], Today);
        Assert.Equal(DayOfWeek.Saturday, start.DayOfWeek);

        var (window, _) = ImportedPlanPlacement.Place(start, due: null, gatheredEffort: 7, velocity: 7, WorkingHours.Default);

        Assert.Equal(new DateOnly(2026, 3, 9), window.Start);
        Assert.Equal(new DateOnly(2026, 3, 13), window.End);
    }

    [Fact]
    public void ADueDateEndsTheWindow_WhateverTheEffort()
    {
        var due = new DateOnly(2026, 3, 31);

        var (window, placement) = ImportedPlanPlacement.Place(Today, due, gatheredEffort: 2, velocity: 7, WorkingHours.Default);

        Assert.Equal(Today, window.Start);
        Assert.Equal(due, window.End);
        Assert.Equal(ImportPlacement.DueDate, placement);
    }

    [Fact]
    public void ADueDateBeforeTheStart_IsKept_AsAOneDayWindowOnIt()
    {
        var due = new DateOnly(2026, 2, 20);

        var (window, placement) = ImportedPlanPlacement.Place(Today, due, gatheredEffort: 0, velocity: 7, WorkingHours.Default);

        // The person's date wins; the contradiction with the predecessor is the
        // plan's to report, not the importer's to correct.
        Assert.Equal(due, window.Start);
        Assert.Equal(due, window.End);
        Assert.Equal(ImportPlacement.DueDate, placement);
    }
}
