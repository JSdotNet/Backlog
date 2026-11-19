namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The Monday every Monday-first week starts on: a Monday is its own, and every other
/// day reads back to the Monday before it — Sunday included, the end of the week rather
/// than <see cref="DayOfWeek"/>'s start of one.
/// </summary>
public class CalendarWeekTests
{
    [Theory]
    [InlineData(12, 12)] // Monday
    [InlineData(13, 12)]
    [InlineData(17, 12)] // Saturday
    [InlineData(18, 12)] // Sunday
    [InlineData(19, 19)] // the next Monday
    public void MondayOf_ReadsTheMondayOnOrBefore(int day, int monday)
    {
        Assert.Equal(new DateOnly(2026, 10, monday), CalendarWeek.MondayOf(new DateOnly(2026, 10, day)));
    }

    [Fact]
    public void MondayOf_CrossesAMonthAndAYear()
    {
        Assert.Equal(new DateOnly(2025, 12, 29), CalendarWeek.MondayOf(new DateOnly(2026, 1, 1)));
    }
}
