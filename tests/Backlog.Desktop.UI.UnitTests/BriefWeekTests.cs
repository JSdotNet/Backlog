using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.UI.Parts;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Reading the reported week off a series that leaves out the weeks it has no reading
/// for — the per-week medians (entry 19). A positional read of such a series lands on
/// whichever week happens to sit at that index.
/// </summary>
public sealed class BriefWeekTests
{
    /// <summary>Monday 12 October 2026 is in W42, so the reported week is W41.</summary>
    private static readonly BriefWeek Reported = BriefWeek.Of(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), 0);

    [Fact]
    public void The_reported_week_carries_the_label_the_series_use()
    {
        Assert.Equal("W41", Reported.Label);
    }

    /// <summary>The review that found it: reviewed merges only in W33 and W38, so the
    /// series is two points long and nothing was reviewed in the reported week.</summary>
    [Fact]
    public void A_week_the_series_has_no_point_for_has_no_value()
    {
        IReadOnlyList<InsightPoint> gapped = [new("W33", 5m), new("W38", 9m)];

        Assert.Null(Reported.ValueAt(gapped));
    }

    [Fact]
    public void The_reported_weeks_point_is_found_by_its_label_whatever_its_position()
    {
        IReadOnlyList<InsightPoint> gapped = [new("W33", 5m), new("W41", 7m), new("W42", 2m)];

        Assert.Equal(7m, Reported.ValueAt(gapped));
        Assert.Equal(5m, BriefWeek.Of(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero), 0).ValueAt(gapped));
    }
}
