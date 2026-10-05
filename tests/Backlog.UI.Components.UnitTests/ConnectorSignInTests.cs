using Backlog.UI.Components.Integrations;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Signing in to one outside tool on the Connectors page. The line holds nothing
/// but what it shows: every value is the host's and every act a callback, so these
/// render it with values and assert on the markup and on what it hands back.
/// </summary>
public sealed class ConnectorSignInTests
{
    [Fact]
    public void It_names_the_connector_and_draws_a_mark_only_for_a_known_provider()
    {
        using var context = new BunitContext();

        var known = context.Render<ConnectorSignIn>(p => p.Add(c => c.ConnectorName, "GitHub").Add(c => c.Icon, "github"));
        var unknown = context.Render<ConnectorSignIn>(p => p.Add(c => c.ConnectorName, "spec-manager").Add(c => c.Icon, "spec-manager"));

        Assert.Equal("GitHub", known.Find(".connector-sign-in__name").TextContent);
        Assert.Single(known.FindAll(".connector-sign-in__head > svg"));
        Assert.Equal("spec-manager", unknown.Find(".connector-sign-in__name").TextContent);
        Assert.Empty(unknown.FindAll(".connector-sign-in__head > svg"));
    }

    [Fact]
    public void Signed_out_offers_sign_in_and_hands_the_press_back()
    {
        using var context = new BunitContext();
        var pressed = 0;

        var line = context.Render<ConnectorSignIn>(p => p
            .Add(c => c.ConnectorName, "Tracker")
            .Add(c => c.OnSignIn, () => pressed++)
            .Add(c => c.TestId, "t"));

        Assert.Equal("Not signed in", line.Find("[data-testid='t-account']").TextContent.Trim());
        Assert.Empty(line.FindAll("[data-testid='t-sign-out']"));
        line.Find("[data-testid='t-sign-in']").Click();

        Assert.Equal(1, pressed);
    }

    [Fact]
    public void A_sign_in_under_way_is_busy_and_cannot_be_pressed_again()
    {
        using var context = new BunitContext();

        var line = context.Render<ConnectorSignIn>(p => p
            .Add(c => c.ConnectorName, "Tracker")
            .Add(c => c.SigningIn, true)
            .Add(c => c.TestId, "t"));

        var button = line.Find("[data-testid='t-sign-in']");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
    }

    [Fact]
    public void Signed_in_names_the_account_and_offers_sign_out()
    {
        using var context = new BunitContext();
        var pressed = 0;

        var line = context.Render<ConnectorSignIn>(p => p
            .Add(c => c.ConnectorName, "Tracker")
            .Add(c => c.AccountName, "Sam")
            .Add(c => c.OnSignOut, () => pressed++)
            .Add(c => c.TestId, "t"));

        Assert.Equal("Signed in as Sam", line.Find("[data-testid='t-account']").TextContent.Trim());
        Assert.Empty(line.FindAll("[data-testid='t-sign-in']"));
        line.Find("[data-testid='t-sign-out']").Click();

        Assert.Equal(1, pressed);
    }

    [Fact]
    public void A_failed_sign_in_is_shown_as_an_alert()
    {
        using var context = new BunitContext();

        var line = context.Render<ConnectorSignIn>(p => p
            .Add(c => c.ConnectorName, "Tracker")
            .Add(c => c.Error, "The window was closed.")
            .Add(c => c.TestId, "t"));

        var error = line.Find("[data-testid='t-error']");
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Equal("The window was closed.", error.TextContent);
    }

    [Fact]
    public void Without_an_error_there_is_no_alert()
    {
        using var context = new BunitContext();

        var line = context.Render<ConnectorSignIn>(p => p.Add(c => c.ConnectorName, "Tracker").Add(c => c.TestId, "t"));

        Assert.Empty(line.FindAll("[data-testid='t-error']"));
    }
}
