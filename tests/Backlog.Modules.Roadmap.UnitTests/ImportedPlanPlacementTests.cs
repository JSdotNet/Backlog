using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Services;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// Where an import places a window (ADR 0013, ruling 4): the start from what the item
/// waits on, the end from <c>due:</c> or from effort over velocity.
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

    [Theory]
    [InlineData(0, 7, ImportedPlanPlacement.DefaultSpanDays)] // nothing estimated gathered
    [InlineData(10, 7, 10)]
    [InlineData(3, 14, 2)]                                     // 1.5 rounds up
    [InlineData(1, 28, ImportedPlanPlacement.MinimumSpanDays)] // a quarter day is still a day
    [InlineData(5, 3.5, 10)]
    [InlineData(4, 4, 7)]                                      // a week's pace is a week, not a hair over
    [InlineData(20, 10, 14)]
    [InlineData(1, 3, 3)]                                      // 2.33 days rounds up
    public void LengthIsEffortOverAWeeksVelocity_InCalendarDays_RoundedUp_NeverUnderADay(int effort, double velocity, int days)
    {
        Assert.Equal(days, ImportedPlanPlacement.Days(effort, (decimal)velocity));
    }

    [Fact]
    public void WithoutADueDate_TheWindowSpansTheLength_BothEndsInclusive()
    {
        var (window, placement) = ImportedPlanPlacement.Place(Today, due: null, gatheredEffort: 6, velocity: 14);

        Assert.Equal(Today, window.Start);
        Assert.Equal(new DateOnly(2026, 3, 4), window.End);
        Assert.Equal(3, window.Days);
        Assert.Equal(ImportPlacement.Effort, placement);
    }

    [Fact]
    public void NothingGathered_PlacesTheDefaultSpan()
    {
        var (window, placement) = ImportedPlanPlacement.Place(Today, due: null, gatheredEffort: 0, velocity: 7);

        Assert.Equal(ImportedPlanPlacement.DefaultSpanDays, window.Days);
        Assert.Equal(ImportPlacement.Effort, placement);
    }

    [Fact]
    public void ADueDateEndsTheWindow_WhateverTheEffort()
    {
        var due = new DateOnly(2026, 3, 31);

        var (window, placement) = ImportedPlanPlacement.Place(Today, due, gatheredEffort: 2, velocity: 7);

        Assert.Equal(Today, window.Start);
        Assert.Equal(due, window.End);
        Assert.Equal(ImportPlacement.DueDate, placement);
    }

    [Fact]
    public void ADueDateBeforeTheStart_IsKept_AsAOneDayWindowOnIt()
    {
        var due = new DateOnly(2026, 2, 20);

        var (window, placement) = ImportedPlanPlacement.Place(Today, due, gatheredEffort: 0, velocity: 7);

        // The person's date wins; the contradiction with the predecessor is the
        // plan's to report, not the importer's to correct.
        Assert.Equal(due, window.Start);
        Assert.Equal(due, window.End);
        Assert.Equal(ImportPlacement.DueDate, placement);
    }
}
