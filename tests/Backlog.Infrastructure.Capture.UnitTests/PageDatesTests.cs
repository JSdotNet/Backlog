using Backlog.Infrastructure.Capture.EventPages;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// The third step's date patterns: one day, a run in one month, a run across
/// two, with English or Dutch month names in full or abbreviated, in any case,
/// and always a year.
/// </summary>
public sealed class PageDatesTests
{
    [Theory]
    [InlineData("Join us on 27 November 2026.", 2026, 11, 27, 2026, 11, 27)]
    [InlineData("27 nov 2026", 2026, 11, 27, 2026, 11, 27)]
    [InlineData("27 Nov. 2026", 2026, 11, 27, 2026, 11, 27)]
    [InlineData("26 - 28 OCT 2026", 2026, 10, 26, 2026, 10, 28)]
    [InlineData("26-28 oktober 2026", 2026, 10, 26, 2026, 10, 28)]
    [InlineData("11 – 13 mei 2027", 2027, 5, 11, 2027, 5, 13)]
    [InlineData("11—13 May 2027", 2027, 5, 11, 2027, 5, 13)]
    [InlineData("30 sep - 2 okt 2026", 2026, 9, 30, 2026, 10, 2)]
    [InlineData("30 September - 2 October 2026", 2026, 9, 30, 2026, 10, 2)]
    [InlineData("3 maart 2027", 2027, 3, 3, 2027, 3, 3)]
    [InlineData("3 mrt. is no month, but 3 March 2027 is", 2027, 3, 3, 2027, 3, 3)]
    [InlineData("1 januari 2027", 2027, 1, 1, 2027, 1, 1)]
    [InlineData("12 juni 2027", 2027, 6, 12, 2027, 6, 12)]
    [InlineData("4 juli 2027", 2027, 7, 4, 2027, 7, 4)]
    [InlineData("20 augustus 2027", 2027, 8, 20, 2027, 8, 20)]
    [InlineData("8 Sept 2027", 2027, 9, 8, 2027, 9, 8)]
    [InlineData("1 december 2026", 2026, 12, 1, 2026, 12, 1)]
    [InlineData("14 februari 2027", 2027, 2, 14, 2027, 2, 14)]
    [InlineData("30 dec - 2 jan 2027", 2026, 12, 30, 2027, 1, 2)]
    [InlineData("November 27, 2026", 2026, 11, 27, 2026, 11, 27)]
    [InlineData("Nov 27 2026", 2026, 11, 27, 2026, 11, 27)]
    [InlineData("Nov. 27, 2026", 2026, 11, 27, 2026, 11, 27)]
    [InlineData("Feb 28, 2024", 2024, 2, 28, 2024, 2, 28)]
    [InlineData("Oct 26-28, 2026", 2026, 10, 26, 2026, 10, 28)]
    [InlineData("October 26 – 28 2026", 2026, 10, 26, 2026, 10, 28)]
    [InlineData("Oct 26 - Nov 2, 2026", 2026, 10, 26, 2026, 11, 2)]
    [InlineData("Dec 30 - Jan 2, 2027", 2026, 12, 30, 2027, 1, 2)]
    [InlineData("27th November 2026", 2026, 11, 27, 2026, 11, 27)]
    [InlineData("the 27th of November 2026", 2026, 11, 27, 2026, 11, 27)]
    [InlineData("November 27th, 2026", 2026, 11, 27, 2026, 11, 27)]
    [InlineData("1st - 3rd May 2027", 2027, 5, 1, 2027, 5, 3)]
    [InlineData("May 1st-3rd, 2027", 2027, 5, 1, 2027, 5, 3)]
    [InlineData("22nd Oct 2026", 2026, 10, 22, 2026, 10, 22)]
    public void A_date_in_the_text_is_read(string text, int startYear, int startMonth, int startDay, int endYear, int endMonth, int endDay)
    {
        var span = Assert.Single(PageDates.In(text));

        Assert.Equal(new DateOnly(startYear, startMonth, startDay), span.Start);
        Assert.Equal(new DateOnly(endYear, endMonth, endDay), span.End);
    }

    /// <summary>No year, no month, not a day, or a month name inside a word:
    /// none of these is a date the reader can be told about.</summary>
    [Theory]
    [InlineData("Join us the 27th of November")]
    [InlineData("Combi May 11-13")]
    [InlineData("November 27th")]
    [InlineData("DotnetFriday Februari 2024")]
    [InlineData("31 June 2027")]
    [InlineData("12 marketing 2026")]
    [InlineData("30 sep okt 2026")]
    [InlineData("26 OCT 26")]
    [InlineData("Nov 272026")]
    [InlineData("okt 2026")]
    public void Anything_else_is_no_date(string text)
    {
        Assert.Empty(PageDates.In(text));
    }

    /// <summary>A day is not read out of the tail of another number — the
    /// minutes of a time, a price, a decimal, a slash date — so the date the
    /// page wrote after it is read as written rather than as a run from it.</summary>
    [Theory]
    [InlineData("Inloop 18:30 - 27 november 2026")]
    [InlineData("Entree €10 - 12 november 2026")]
    [InlineData("Versie 1.10 - 12 november 2026")]
    [InlineData("Ref 3/10 - 12 november 2026")]
    public void A_day_is_not_read_out_of_the_tail_of_another_number(string text)
    {
        var span = Assert.Single(PageDates.In(text));

        Assert.Equal(span.Start, span.End);
        Assert.Equal(11, span.End.Month);
        Assert.Equal(2026, span.End.Year);
        Assert.NotEqual(10, span.Start.Day);
        Assert.NotEqual(30, span.Start.Day);
    }

    [Fact]
    public void The_time_before_a_date_does_not_hide_it()
    {
        var span = Assert.Single(PageDates.In("Inloop 18:30 - 27 november 2026"));

        Assert.Equal(new PageDateSpan(new DateOnly(2026, 11, 27), new DateOnly(2026, 11, 27)), span);
    }

    /// <summary>A run that ends before it starts was a misreading; the day
    /// after the dash is still the date the page wrote.</summary>
    [Fact]
    public void A_run_that_ends_before_it_starts_is_read_as_its_end_day()
    {
        var span = Assert.Single(PageDates.In("Zaal 28 - 26 OCT 2026"));

        Assert.Equal(new PageDateSpan(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 26)), span);
    }

    [Fact]
    public void Dates_come_back_in_the_order_the_text_holds_them()
    {
        var spans = PageDates.In("Last year: 11 - 13 MAY 2026. This year: 10 - 12 May 2027.");

        Assert.Equal([new DateOnly(2026, 5, 11), new DateOnly(2027, 5, 10)], spans.Select(span => span.Start).ToArray());
    }

    [Theory]
    [InlineData("mei", 5)]
    [InlineData("MAY", 5)]
    [InlineData("maart", 3)]
    [InlineData("Okt", 10)]
    [InlineData("oct", 10)]
    [InlineData("xyz", null)]
    [InlineData("ma", null)]
    public void A_month_is_known_by_its_first_three_letters_in_either_language(string name, int? month)
    {
        Assert.Equal(month, PageDates.MonthOf(name));
    }
}
