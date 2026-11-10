using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// One test or more per rule of <c>.devbook/domain/events/domain.invariants.md#event-series</c>,
/// named after the invariant it proves, and the restore from storage.
/// </summary>
public sealed class EventSeriesInvariantTests
{
    // --- A series has a name ------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_series_without_a_name_is_refused(string name)
    {
        var result = EventSeries.Create(Guid.NewGuid(), name, organiser: null, link: null);

        Assert.Equal("event_series.name_missing", result.Error.Code);
    }

    [Fact]
    public void A_series_keeps_its_name_trimmed_its_organiser_and_its_link()
    {
        var id = Guid.NewGuid();

        var series = EventSeries.Create(id, "  Techorama ", " Techorama vzw ", " https://techorama.be ").Value;

        Assert.Equal(id, series.Id);
        Assert.Equal("Techorama", series.Name);
        Assert.Equal("Techorama vzw", series.Organiser);
        Assert.Equal("https://techorama.be", series.Link);
    }

    [Fact]
    public void A_name_alone_makes_a_series()
    {
        var series = EventSeries.Create(Guid.NewGuid(), "dotNET Friday", organiser: " ", link: null).Value;

        Assert.Null(series.Organiser);
        Assert.Null(series.Link);
    }

    [Fact]
    public void Renaming_to_a_blank_name_is_refused_and_keeps_the_name()
    {
        var series = EventSeries.Create(Guid.NewGuid(), "Techorama", organiser: null, link: null).Value;

        Assert.Equal("event_series.name_missing", series.Rename("  ").Error.Code);
        Assert.Equal("Techorama", series.Name);
    }

    [Fact]
    public void Renaming_trims_the_new_name()
    {
        var series = EventSeries.Create(Guid.NewGuid(), "Techorama", organiser: null, link: null).Value;

        Assert.True(series.Rename(" Techorama Belgium ").IsSuccess);
        Assert.Equal("Techorama Belgium", series.Name);
    }

    [Fact]
    public void Restore_rebuilds_every_field_as_stored()
    {
        var id = Guid.NewGuid();

        var series = EventSeries.Restore(id, "dotNET Friday", "Info Support", "https://dotnetfriday.nl");

        Assert.Equal(id, series.Id);
        Assert.Equal("dotNET Friday", series.Name);
        Assert.Equal("Info Support", series.Organiser);
        Assert.Equal("https://dotnetfriday.nl", series.Link);
    }
}
