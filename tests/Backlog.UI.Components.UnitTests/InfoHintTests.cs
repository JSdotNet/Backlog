using Microsoft.AspNetCore.Components.Web;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The info mark: an explanation a screen keeps behind a ⓘ instead of wearing.
///
/// <para>What is pinned is the wiring a sighted reader never sees — that the
/// trigger is a real button with a name, that it is described by the bubble
/// whether or not the bubble shows, and that Escape and a press change the
/// classes the stylesheet shows and hides on. Hover and focus are the
/// stylesheet's, and are not something bUnit can see.</para>
/// </summary>
public sealed class InfoHintTests
{
    [Fact]
    public void The_trigger_is_a_named_button_described_by_the_bubble()
    {
        using var context = new BunitContext();

        var hint = context.Render<InfoHint>(parameters => parameters
            .Add(h => h.AriaLabel, "About Score")
            .Add(h => h.Text, "Two scores, each normalised to 100."));

        var trigger = hint.Find("button.info-hint__trigger");
        var bubble = hint.Find("[role='tooltip']");

        Assert.Equal("About Score", trigger.GetAttribute("aria-label"));
        Assert.Equal(bubble.Id, trigger.GetAttribute("aria-describedby"));
        Assert.Equal("Two scores, each normalised to 100.", bubble.TextContent.Trim());
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Empty(hint.FindAll("[title]"));
    }

    [Fact]
    public void Child_content_replaces_the_text()
    {
        using var context = new BunitContext();

        var hint = context.Render<InfoHint>(parameters => parameters
            .Add(h => h.AriaLabel, "About Cost")
            .Add(h => h.Text, "ignored")
            .AddChildContent("<em>rich</em>"));

        Assert.NotNull(hint.Find("[role='tooltip'] em"));
        Assert.DoesNotContain("ignored", hint.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_press_pins_the_bubble_open_and_a_second_press_releases_it()
    {
        using var context = new BunitContext();

        var hint = context.Render<InfoHint>(parameters => parameters
            .Add(h => h.AriaLabel, "About Score")
            .Add(h => h.Text, "text"));

        hint.Find("button").Click();

        hint.WaitForAssertion(() =>
        {
            Assert.NotNull(hint.Find(".info-hint--open"));
            Assert.Equal("true", hint.Find("button").GetAttribute("aria-expanded"));
        });

        hint.Find("button").Click();

        hint.WaitForAssertion(() => Assert.Empty(hint.FindAll(".info-hint--open")));
    }

    [Fact]
    public void Escape_dismisses_until_focus_or_the_pointer_leaves()
    {
        using var context = new BunitContext();

        var hint = context.Render<InfoHint>(parameters => parameters
            .Add(h => h.AriaLabel, "About Score")
            .Add(h => h.Text, "text"));

        hint.Find("button").Click();
        hint.Find(".info-hint").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        hint.WaitForAssertion(() =>
        {
            Assert.NotNull(hint.Find(".info-hint--dismissed"));
            Assert.Empty(hint.FindAll(".info-hint--open"));
        });

        hint.Find(".info-hint").FocusOut();

        hint.WaitForAssertion(() => Assert.Empty(hint.FindAll(".info-hint--dismissed")));
    }

    /// <summary>
    /// The live host is the dashboard takeover, which closes on any Escape that
    /// reaches the shell's surface. The Escape that closes the caption is the mark's
    /// and goes no further; the next one, with nothing left to close, is the host's.
    /// A host that counts is what tells the two apart — a key that went nowhere looks
    /// exactly like a key that was heard, unless something above is keeping score.
    /// </summary>
    [Fact]
    public void The_escape_that_closes_the_bubble_stops_there_and_the_next_one_reaches_the_host()
    {
        using var context = new BunitContext();

        var host = context.Render<InfoHintEscapeHarness>();
        var mark = host.Find("[data-testid='mark']");

        mark.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        host.WaitForAssertion(() => Assert.NotNull(host.Find(".info-hint--dismissed")));
        Assert.Empty(host.Instance.HostKeys);

        host.Find("[data-testid='mark']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        host.WaitForAssertion(() => Assert.Equal(["Escape"], host.Instance.HostKeys));
    }

    [Fact]
    public void The_test_ids_land_on_the_wrapper_the_trigger_and_the_bubble()
    {
        using var context = new BunitContext();

        var hint = context.Render<InfoHint>(parameters => parameters
            .Add(h => h.AriaLabel, "About Score")
            .Add(h => h.Text, "text")
            .Add(h => h.TestId, "hint")
            .Add(h => h.TriggerTestId, "hint-info")
            .Add(h => h.TooltipTestId, "hint-note"));

        Assert.Equal("SPAN", hint.Find("[data-testid='hint']").TagName);
        Assert.Equal("BUTTON", hint.Find("[data-testid='hint-info']").TagName);
        Assert.Equal("tooltip", hint.Find("[data-testid='hint-note']").GetAttribute("role"));
    }
}
