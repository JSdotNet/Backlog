namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Four causes, one component, one voice. The assertions that matter here are
/// that the sentence is always there and that it never escalates.
/// </summary>
public sealed class IntegrationUnavailableTests
{
    [Theory]
    [InlineData(IntegrationAvailability.NotAuthorized, "GitHub", "GitHub is not connected.")]
    [InlineData(IntegrationAvailability.NotInstalled, "VS Code", "VS Code is not installed on this machine.")]
    [InlineData(IntegrationAvailability.FeatureOff, "GitHub", "GitHub is turned off in settings.")]
    public void Each_cause_has_its_own_sentence(
        IntegrationAvailability availability, string subject, string sentence)
    {
        // Four unrelated causes with four different remedies, and the temptation
        // is four components. From the reader's side they are the same sentence
        // in the same place, and splitting them would put it in four positions
        // and four voices.
        using var context = new BunitContext();

        var unavailable = context.Render<IntegrationUnavailable>(parameters => parameters
            .Add(u => u.Readiness, new IntegrationReadiness(availability, subject))
            .Add(u => u.TextTestId, "text"));

        Assert.Equal(sentence, unavailable.Find("[data-testid='text']").TextContent);
    }

    [Fact]
    public void Offline_says_that_everything_else_keeps_working()
    {
        // The second clause is the whole difference between a standing condition
        // and a failure.
        using var context = new BunitContext();

        var unavailable = context.Render<IntegrationUnavailable>(parameters => parameters
            .Add(u => u.Readiness, IntegrationReadiness.Offline())
            .Add(u => u.TextTestId, "text"));

        Assert.Equal(
            "Offline. This needs a connection; everything else keeps working.",
            unavailable.Find("[data-testid='text']").TextContent);
    }

    [Theory]
    [InlineData(IntegrationAvailability.NotAuthorized)]
    [InlineData(IntegrationAvailability.NotInstalled)]
    [InlineData(IntegrationAvailability.Offline)]
    [InlineData(IntegrationAvailability.FeatureOff)]
    public void No_cause_ever_escalates_to_an_alert(IntegrationAvailability availability)
    {
        // role="alert" interrupts. It is right for a save that failed and wrong
        // for a network that is not there — and all four of these are standing
        // conditions rather than events.
        using var context = new BunitContext();

        var unavailable = context.Render<IntegrationUnavailable>(parameters => parameters
            .Add(u => u.Readiness, new IntegrationReadiness(availability, "GitHub"))
            .Add(u => u.TestId, "row"));

        Assert.Equal("status", unavailable.Find("[data-testid='row']").GetAttribute("role"));
    }

    [Fact]
    public void The_icon_is_a_second_carrier_beside_the_words()
    {
        // The ink here is --color-text-disabled, which the colour scheme exempts
        // from contrast because it signals unavailability. That exemption is only
        // safe while the glyph and the sentence are doing the work.
        using var context = new BunitContext();

        var unavailable = context.Render<IntegrationUnavailable>(parameters => parameters
            .Add(u => u.Readiness, IntegrationReadiness.NotAuthorized("GitHub")));

        var glyph = unavailable.Find(".integration-unavailable__glyph svg");

        Assert.Equal("true", glyph.GetAttribute("aria-hidden"));
        Assert.NotEmpty(glyph.InnerHtml);
    }

    [Fact]
    public void Each_cause_draws_a_different_glyph()
    {
        using var context = new BunitContext();

        var glyphs = new[]
        {
            IntegrationAvailability.NotAuthorized,
            IntegrationAvailability.NotInstalled,
            IntegrationAvailability.Offline,
            IntegrationAvailability.FeatureOff
        }
        .Select(availability => context
            .Render<IntegrationUnavailable>(parameters => parameters
                .Add(u => u.Readiness, new IntegrationReadiness(availability, "GitHub")))
            .Find(".integration-unavailable__glyph svg")
            .InnerHtml)
        .ToList();

        Assert.Equal(4, glyphs.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Offline_offers_no_remedy_even_when_a_host_is_listening()
    {
        // There is no button that puts a network back, and offering one would be
        // the product pretending it can do something it cannot.
        using var context = new BunitContext();

        var unavailable = context.Render<IntegrationUnavailable>(parameters => parameters
            .Add(u => u.Readiness, IntegrationReadiness.Offline())
            .Add(u => u.OnRemedy, _ => { })
            .Add(u => u.RemedyTestId, "remedy"));

        Assert.Empty(unavailable.FindAll("[data-testid='remedy']"));
    }

    [Fact]
    public void A_remedy_reports_the_readiness_it_belongs_to()
    {
        using var context = new BunitContext();
        IntegrationReadiness? remedied = null;
        var readiness = IntegrationReadiness.FeatureOff("GitHub");

        var unavailable = context.Render<IntegrationUnavailable>(parameters => parameters
            .Add(u => u.Readiness, readiness)
            .Add(u => u.OnRemedy, r => remedied = r)
            .Add(u => u.RemedyTestId, "remedy"));

        var remedy = unavailable.Find("[data-testid='remedy']");

        Assert.Equal("Open settings", remedy.TextContent.Trim());

        remedy.Click();

        Assert.Equal(readiness, remedied);
    }

    [Fact]
    public void A_host_may_write_its_own_sentence_and_its_own_way_out()
    {
        using var context = new BunitContext();

        var unavailable = context.Render<IntegrationUnavailable>(parameters => parameters
            .Add(u => u.Readiness, new IntegrationReadiness(
                IntegrationAvailability.NotAuthorized,
                "GitHub",
                "This workspace has no GitHub token.",
                "Add a token"))
            .Add(u => u.OnRemedy, _ => { })
            .Add(u => u.TextTestId, "text")
            .Add(u => u.RemedyTestId, "remedy"));

        Assert.Equal("This workspace has no GitHub token.", unavailable.Find("[data-testid='text']").TextContent);
        Assert.Equal("Add a token", unavailable.Find("[data-testid='remedy']").TextContent.Trim());
    }
}
