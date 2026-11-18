using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.UI;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// How the Events list words when an event runs: one day reads once, whatever the end carries
/// on that same date, and only a different day draws a range.
/// </summary>
public sealed class EventPresentationTests
{
    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);
    private static readonly DateOnly Friday = new(2026, 11, 27);

    [Fact]
    public void A_same_day_end_without_a_time_reads_as_the_start_day()
    {
        var start = new EventDateDto(Friday, new TimeOnly(16, 30), Cet);

        Assert.Equal("27 Nov 2026 16:30", EventPresentation.When(start, new EventDateDto(Friday)));
    }

    [Fact]
    public void A_same_day_end_at_the_start_time_reads_as_the_start_day()
    {
        var start = new EventDateDto(Friday, new TimeOnly(16, 30), Cet);

        Assert.Equal("27 Nov 2026 16:30", EventPresentation.When(start, new EventDateDto(Friday, new TimeOnly(16, 30), Cet)));
        Assert.Equal("27 Nov 2026 16:30", EventPresentation.When(start, new EventDateDto(Friday, new TimeOnly(16, 30))));
        Assert.Equal("27 Nov 2026 16:30", EventPresentation.When(start, new EventDateDto(Friday, new TimeOnly(16, 30), TimeSpan.Zero)));
    }

    [Fact]
    public void A_same_day_end_at_a_later_time_reads_the_day_once_with_both_times()
    {
        var start = new EventDateDto(Friday, new TimeOnly(16, 30), Cet);
        var end = new EventDateDto(Friday, new TimeOnly(21, 0), Cet);

        Assert.Equal("27 Nov 2026 16:30 – 21:00", EventPresentation.When(start, end));
    }

    [Fact]
    public void A_same_day_end_with_a_time_after_a_start_without_one_reads_the_day_once()
    {
        var end = new EventDateDto(Friday, new TimeOnly(21, 0), Cet);

        Assert.Equal("27 Nov 2026 – 21:00", EventPresentation.When(new EventDateDto(Friday), end));
    }

    [Fact]
    public void A_same_day_end_without_times_reads_the_day_once()
    {
        Assert.Equal("27 Nov 2026", EventPresentation.When(new EventDateDto(Friday), new EventDateDto(Friday)));
    }

    [Fact]
    public void An_end_on_another_day_reads_as_a_range()
    {
        var start = new EventDateDto(Friday, new TimeOnly(16, 30), Cet);
        var end = new EventDateDto(new DateOnly(2026, 11, 28));

        Assert.Equal("27 Nov 2026 16:30 – 28 Nov 2026", EventPresentation.When(start, end));
    }

    [Fact]
    public void No_end_reads_as_the_start_day_and_no_start_as_not_known()
    {
        Assert.Equal("27 Nov 2026", EventPresentation.When(new EventDateDto(Friday), null));
        Assert.Equal("Date not known", EventPresentation.When(null, new EventDateDto(Friday)));
    }

    [Fact]
    public void The_row_line_reads_a_one_day_event_once()
    {
        var summary = new KnowledgeEventSummaryDto(
            Guid.NewGuid(), "dotNET Friday", KnowledgeEventState.Spotted,
            new EventDateDto(Friday, new TimeOnly(16, 30), Cet), new EventDateDto(Friday),
            "DotnetFriday", new PlaceDto("Nieuwegein", IsOnline: false));

        Assert.Equal("27 Nov 2026 16:30 · DotnetFriday · Nieuwegein", EventPresentation.RowLine(summary));
    }
}
