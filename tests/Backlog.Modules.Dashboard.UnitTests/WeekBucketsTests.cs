using Backlog.Modules.Dashboard.Services;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The weekly axis every productivity series is drawn on.
/// </summary>
public class WeekBucketsTests
{
    /// <summary>
    /// ISO weeks rather than seven-day counts back from today. A rolling window
    /// would move every reading into a different bucket overnight, so no two
    /// screenshots taken a day apart would agree about which week a merge was in.
    /// </summary>
    [Fact]
    public void Two_instants_in_the_same_iso_week_land_in_the_same_bucket()
    {
        var monday = new DateTimeOffset(2026, 8, 17, 0, 30, 0, TimeSpan.Zero);
        var sunday = new DateTimeOffset(2026, 8, 23, 23, 30, 0, TimeSpan.Zero);

        // Monday to Sunday is one bucket; the Monday after it is the next one.
        Assert.Equal(Label(monday), Label(sunday));
        Assert.NotEqual(Label(sunday), Label(sunday.AddDays(1)));
        Assert.Equal(Label(sunday.AddDays(1)), Label(monday.AddDays(7)));
    }

    [Fact]
    public void The_axis_covers_the_whole_window_including_the_week_it_ends_in()
    {
        var to = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

        var labels = WeekBuckets.Buckets(to.AddDays(-7 * 12), to);

        // Twelve weeks back plus the partial week the window ends in.
        Assert.Equal(13, labels.Count);
        Assert.Equal(Label(to), labels[^1].Label);
        Assert.Equal(labels.Count, labels.Select(bucket => bucket.Key).Distinct().Count());
    }

    [Fact]
    public void A_reversed_window_gives_an_empty_axis_rather_than_hanging()
    {
        var now = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

        Assert.Empty(WeekBuckets.Buckets(now, now.AddDays(-30)));
    }

    [Fact]
    public void A_week_with_nothing_in_it_is_a_zero_rather_than_a_missing_bucket()
    {
        var to = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
        var labels = WeekBuckets.Buckets(to.AddDays(-21), to);

        var counted = WeekBuckets.Count(labels, new[] { to }, instant => instant);

        Assert.Equal(labels.Count, counted.Count);
        Assert.Equal(1m, counted[^1].Value);
        Assert.All(counted.SkipLast(1), point => Assert.Equal(0m, point.Value));
    }

    /// <summary>
    /// A window's edge is the window's edge. Folding an out-of-range item into the
    /// nearest bucket would quietly move a merge from June into August.
    /// </summary>
    [Fact]
    public void An_item_outside_the_axis_is_dropped_rather_than_folded_into_the_nearest_bucket()
    {
        var to = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
        var labels = WeekBuckets.Buckets(to.AddDays(-14), to);

        var counted = WeekBuckets.Count(labels, new[] { to.AddYears(-1) }, instant => instant);

        Assert.All(counted, point => Assert.Equal(0m, point.Value));
    }

    [Fact]
    public void Reduce_gives_an_empty_bucket_to_the_aggregate_rather_than_skipping_it()
    {
        var to = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
        var labels = WeekBuckets.Buckets(to.AddDays(-14), to);

        var seen = 0;
        var reduced = WeekBuckets.Reduce(labels, new[] { to }, instant => instant, bucket =>
        {
            seen++;
            return bucket.Count;
        });

        Assert.Equal(labels.Count, seen);
        Assert.Equal(labels.Count, reduced.Count);
        Assert.Equal(1m, reduced[^1].Value);
    }

    /// <summary>The short axis form, which is what a chart shows and what most of
    /// these tests compare. The ISO year lives on the key, not here — which is the
    /// distinction the out-of-window test exists to prove.</summary>
    private static string Label(DateTimeOffset instant) => WeekBuckets.Of(instant).Label;
}
