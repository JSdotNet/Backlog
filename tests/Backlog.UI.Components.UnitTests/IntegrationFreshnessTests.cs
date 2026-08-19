namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Nothing in this product polls, so what is on screen is a reading with a time
/// on it rather than a value that keeps itself true. These assertions are about
/// the component saying so.
/// </summary>
public sealed class IntegrationFreshnessTests
{
    [Fact]
    public void Nobody_has_looked_yet_is_a_thing_the_line_says()
    {
        // "Not checked" and "it is closed" are different facts, and a surface
        // that showed a state with no reading behind it would be claiming a
        // currency nothing here provides.
        using var context = new BunitContext();

        var freshness = context.Render<IntegrationFreshness>(parameters => parameters
            .Add(f => f.TextTestId, "text"));

        Assert.Equal("Not checked yet", freshness.Find("[data-testid='text']").TextContent);
    }

    [Fact]
    public void A_reading_is_printed_as_the_host_formatted_it()
    {
        // What "4 minutes ago" is, and in what language, belongs to the host —
        // the same division MarkdownComment already makes for its timestamps.
        using var context = new BunitContext();

        var freshness = context.Render<IntegrationFreshness>(parameters => parameters
            .Add(f => f.Reading, new IntegrationReading("4 minutes ago"))
            .Add(f => f.TextTestId, "text"));

        Assert.Equal("as of 4 minutes ago", freshness.Find("[data-testid='text']").TextContent);
    }

    [Fact]
    public void A_failed_read_stays_a_status_and_never_becomes_an_alert()
    {
        // .design/design-principles.md#local-first requires offline to be "a
        // calm, persistent status — not an error modal", and a read that could
        // not reach GitHub is the same class of thing.
        using var context = new BunitContext();

        var freshness = context.Render<IntegrationFreshness>(parameters => parameters
            .Add(f => f.Reading, new IntegrationReading("an hour ago", FailureReason: "no connection"))
            .Add(f => f.TextTestId, "text"));

        var line = freshness.Find("[data-testid='text']");

        Assert.Equal("status", line.GetAttribute("role"));
        Assert.Equal("Could not check — no connection", line.TextContent);
    }

    [Fact]
    public void A_read_in_flight_beats_the_time_of_the_last_one()
    {
        // What is happening now beats what happened last, or the line reads as
        // current when it is being replaced.
        using var context = new BunitContext();

        var freshness = context.Render<IntegrationFreshness>(parameters => parameters
            .Add(f => f.Reading, new IntegrationReading("an hour ago", InFlight: true))
            .Add(f => f.TextTestId, "text"));

        Assert.Equal("Checking now…", freshness.Find("[data-testid='text']").TextContent);
    }

    [Fact]
    public void Without_a_delegate_there_is_a_line_and_no_button()
    {
        // A surface that cannot re-read should not offer a control that would do
        // nothing.
        using var context = new BunitContext();

        var quiet = context.Render<IntegrationFreshness>(parameters => parameters
            .Add(f => f.RefreshTestId, "refresh"));

        Assert.Empty(quiet.FindAll("[data-testid='refresh']"));

        var checkable = context.Render<IntegrationFreshness>(parameters => parameters
            .Add(f => f.OnRefresh, () => { })
            .Add(f => f.RefreshTestId, "refresh"));

        Assert.Equal("Check now", checkable.Find("[data-testid='refresh']").TextContent.Trim());
    }

    [Fact]
    public void Checking_reports_once_per_press()
    {
        using var context = new BunitContext();
        var checks = 0;

        var freshness = context.Render<IntegrationFreshness>(parameters => parameters
            .Add(f => f.OnRefresh, () => checks++)
            .Add(f => f.RefreshTestId, "refresh"));

        freshness.Find("[data-testid='refresh']").Click();

        Assert.Equal(1, checks);
    }

    [Fact]
    public void The_button_is_busy_while_a_read_is_in_flight()
    {
        // AppButton's own busy state, so the control stays in place and stays in
        // the tab order rather than being swapped for a spinner.
        using var context = new BunitContext();

        var freshness = context.Render<IntegrationFreshness>(parameters => parameters
            .Add(f => f.Reading, new IntegrationReading(InFlight: true))
            .Add(f => f.OnRefresh, () => { })
            .Add(f => f.RefreshTestId, "refresh"));

        Assert.Equal("true", freshness.Find("[data-testid='refresh']").GetAttribute("aria-busy"));
    }
}
