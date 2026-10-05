namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The visible half of the confirmation. The status line is covered through the
/// hosts that draw it — CodeView, FileHeader, the integration bar — and two hosts
/// hide that line on purpose, which is what the check glyph is for: it is the only
/// confirmation a task row ever shows.
/// </summary>
public sealed class CopyButtonTests
{
    /// <summary>
    /// A disabled copy keeps its place and its shape and says why it refuses: the
    /// reason is the wrapper's title — a disabled button raises no pointer events
    /// of its own, so a tooltip on it is one nobody can open — and a described-by
    /// for a screen reader, the pairing <c>TaskAction</c> already makes. The
    /// button is <c>disabled</c> and <c>aria-disabled</c>, and a press that reaches
    /// it anyway copies nothing.
    /// </summary>
    [Fact]
    public void A_disabled_copy_says_why_and_copies_nothing()
    {
        using var context = new BunitContext();
        var clipboard = context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true);
        clipboard.SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.Disabled, true)
            .Add(c => c.DisabledReason, "Not yet")
            .Add(c => c.ButtonTestId, "copy")
            .Add(c => c.StatusTestId, "status"));

        var control = button.Find("[data-testid='copy']");
        Assert.True(control.HasAttribute("disabled"));
        Assert.Equal("true", control.GetAttribute("aria-disabled"));
        Assert.Equal("Not yet", button.Find(".copy-button").GetAttribute("title"));
        Assert.Equal("Not yet", button.Find($"#{control.GetAttribute("aria-describedby")}").TextContent);

        // The glyph is still drawn, so the row it sits on does not shift.
        Assert.NotNull(button.Find(".copy-button__glyphs"));

        control.Click();

        Assert.Empty(clipboard.Invocations);
        Assert.Equal(string.Empty, button.Find("[data-testid='status']").TextContent);
    }

    /// <summary>The pointer is told the reason, not the act. The button's own
    /// title used to stay "Copy …" over the wrapper's reason, and the innermost
    /// title is the one a browser shows — so hovering a refused copy offered to
    /// copy. The accessible name stays the act; the reason is the description.</summary>
    [Fact]
    public void A_disabled_copy_shows_the_reason_and_not_the_act_on_hover()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.Label, "Copy Wait for the vendor")
            .Add(c => c.Disabled, true)
            .Add(c => c.DisabledReason, "Blocked — unblock to copy")
            .Add(c => c.ButtonTestId, "copy"));

        var control = button.Find("[data-testid='copy']");
        Assert.Equal("Blocked — unblock to copy", control.GetAttribute("title"));
        Assert.Equal("Copy Wait for the vendor", control.GetAttribute("aria-label"));
    }

    /// <summary>
    /// A confirmation does not outlive the copy becoming refused. A row copied and
    /// then marked blocked inside the confirmation's minute kept the check and the
    /// "Copied" line on a button that now says it cannot copy — two answers at
    /// once. Becoming disabled clears both.
    /// </summary>
    [Fact]
    public void Becoming_disabled_clears_a_standing_confirmation()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.ConfirmationDuration, TimeSpan.FromMinutes(1))
            .Add(c => c.DisabledReason, "Blocked — unblock to copy")
            .Add(c => c.ButtonTestId, "copy")
            .Add(c => c.StatusTestId, "status"));

        button.Find("[data-testid='copy']").Click();
        Assert.Equal("Copied", button.Find("[data-testid='status']").TextContent);

        button.Render(parameters => parameters.Add(c => c.Disabled, true));

        Assert.Equal(string.Empty, button.Find("[data-testid='status']").TextContent);
        Assert.Equal("false", button.Find(".copy-button__glyphs").GetAttribute("data-copied"));
        Assert.Equal("false", button.Find(".copy-button").GetAttribute("data-copied"));
    }

    /// <summary>Enabled is the default, and an enabled copy carries no refusal at
    /// all — no title on the wrapper, no aria-disabled on the button.</summary>
    [Fact]
    public void An_enabled_copy_carries_no_refusal()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.DisabledReason, "Not yet")
            .Add(c => c.ButtonTestId, "copy"));

        var control = button.Find("[data-testid='copy']");
        Assert.False(control.HasAttribute("disabled"));
        Assert.Null(control.GetAttribute("aria-disabled"));
        Assert.Null(button.Find(".copy-button").GetAttribute("title"));
    }

    [Fact]
    public void Nothing_is_confirmed_before_anything_is_copied()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping."));

        Assert.Equal("false", button.Find(".copy-button__glyphs").GetAttribute("data-copied"));
        Assert.Equal("false", button.Find(".copy-button").GetAttribute("data-copied"));
    }

    [Fact]
    public void A_copy_that_worked_swaps_the_glyph_for_a_check()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.ButtonTestId, "copy"));

        button.Find("[data-testid='copy']").Click();

        Assert.Equal("true", button.Find(".copy-button__glyphs").GetAttribute("data-copied"));
        Assert.Equal("true", button.Find(".copy-button").GetAttribute("data-copied"));
    }

    [Fact]
    public void A_confirmation_stands_for_three_seconds_unless_the_host_says_otherwise()
    {
        using var context = new BunitContext();

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping."));

        Assert.Equal(TimeSpan.FromSeconds(3), button.Instance.ConfirmationDuration);
    }

    [Fact]
    public void The_check_clears_once_the_hosts_duration_has_passed()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.ConfirmationDuration, TimeSpan.FromMilliseconds(50))
            .Add(c => c.ButtonTestId, "copy"));

        button.Find("[data-testid='copy']").Click();

        button.WaitForAssertion(
            () => Assert.Equal("false", button.Find(".copy-button__glyphs").GetAttribute("data-copied")),
            TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Both glyphs are in the markup at once, because the swap is a cross-fade
    /// between them rather than a replacement: an element that only appears when
    /// the state changes has nothing to transition from.
    /// </summary>
    [Fact]
    public void Both_glyphs_are_drawn_so_one_can_fade_into_the_other()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping."));

        Assert.Single(button.FindAll(".copy-button__glyph--sheets"));
        Assert.Single(button.FindAll(".copy-button__glyph--check"));
    }

    /// <summary>
    /// Motion confirms; it does not report a failure. A refusal keeps the sentence
    /// that tells the reader what to do instead, and the glyph stays as it was.
    /// </summary>
    [Fact]
    public void A_copy_the_browser_refused_is_not_celebrated()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(false);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.ButtonTestId, "copy")
            .Add(c => c.StatusTestId, "status"));

        button.Find("[data-testid='copy']").Click();

        Assert.Equal("false", button.Find(".copy-button__glyphs").GetAttribute("data-copied"));
        Assert.StartsWith("Could not copy", button.Find("[data-testid='status']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one path where the check has to be taken back rather than merely
    /// withheld: the reader has a confirmation on screen, presses again, and this
    /// time the browser refuses. The state is derived from the status precisely so
    /// this cannot leave a check standing over a failure.
    /// </summary>
    [Fact]
    public void A_refusal_after_a_success_takes_the_check_back()
    {
        using var context = new BunitContext();
        var clipboard = context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true);
        clipboard.SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.ButtonTestId, "copy")
            .Add(c => c.StatusTestId, "status"));

        button.Find("[data-testid='copy']").Click();
        Assert.Equal("true", button.Find(".copy-button__glyphs").GetAttribute("data-copied"));

        clipboard.SetResult(false);
        button.Find("[data-testid='copy']").Click();

        Assert.Equal("false", button.Find(".copy-button__glyphs").GetAttribute("data-copied"));
        Assert.Equal("false", button.Find(".copy-button").GetAttribute("data-copied"));
        Assert.StartsWith("Could not copy", button.Find("[data-testid='status']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check is inside the button. The status line stays exactly the sentence
    /// a screen reader is meant to hear, which is also what five host tests assert
    /// character for character.
    /// </summary>
    [Fact]
    public void The_status_line_says_only_what_it_said_before()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.ButtonTestId, "copy")
            .Add(c => c.StatusTestId, "status"));

        button.Find("[data-testid='copy']").Click();

        Assert.Equal("Copied", button.Find("[data-testid='status']").TextContent);
        Assert.Empty(button.FindAll("[data-testid='status'] svg"));
    }

    /// <summary>
    /// A host that puts words on the button keeps them — there is no glyph to swap.
    /// The state still lands on the wrapper, so such a host can style its own
    /// confirmation without the library guessing what that should look like.
    /// </summary>
    [Fact]
    public void A_host_with_its_own_content_keeps_it_and_still_gets_the_state()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.ButtonTestId, "copy")
            .AddChildContent("Copy"));

        button.Find("[data-testid='copy']").Click();

        Assert.Empty(button.FindAll(".copy-button__glyphs"));
        Assert.Equal("true", button.Find(".copy-button").GetAttribute("data-copied"));
        Assert.Contains("Copy", button.Find("[data-testid='copy']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The clipboard call outlives the button that made it. Blazor does not cancel
    /// a JS interop call in flight when a component is disposed, so a task row
    /// filtered out of a list — or a pane navigated away from — mid-copy leaves the
    /// browser to answer a button that is already gone.
    /// <para>
    /// There is nobody left to tell. Announcing it anyway is not merely pointless:
    /// the announcement comes with a three-second timer to take it back, and a
    /// timer started after disposal is one nothing owns and nothing will cancel —
    /// exactly the shape <c>TimedFireAndForgetTests</c> exists to keep out of the
    /// user interface. Disposal before the answer arrives stands in for that
    /// timing.
    /// </para>
    /// </summary>
    [Fact]
    public void A_copy_that_comes_back_after_the_component_is_gone_says_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.ButtonTestId, "copy")
            .Add(c => c.StatusTestId, "status"));

        button.Instance.Dispose();

        button.Find("[data-testid='copy']").Click();

        Assert.Empty(button.Find("[data-testid='status']").TextContent);
        Assert.Equal("false", button.Find(".copy-button").GetAttribute("data-copied"));
    }

    /// <summary>
    /// The same lateness, one copy further on, where a timer from the earlier copy
    /// is already standing. Nulling the field on disposal would silence the first
    /// case and not this one: it would leave the late answer free to replace a
    /// confirmation the reader can no longer see and start another timer to clear
    /// it. What was said last stays said.
    /// </summary>
    [Fact]
    public void A_later_copy_that_comes_back_after_the_component_is_gone_leaves_the_last_word_alone()
    {
        using var context = new BunitContext();
        var clipboard = context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true);
        clipboard.SetResult(true);

        var button = context.Render<CopyButton>(parameters => parameters
            .Add(c => c.Text, "Something worth keeping.")
            .Add(c => c.ButtonTestId, "copy")
            .Add(c => c.StatusTestId, "status"));

        button.Find("[data-testid='copy']").Click();
        Assert.Equal("Copied", button.Find("[data-testid='status']").TextContent);

        button.Instance.Dispose();

        clipboard.SetResult(false);
        button.Find("[data-testid='copy']").Click();

        Assert.Equal("Copied", button.Find("[data-testid='status']").TextContent);
        Assert.Equal("true", button.Find(".copy-button").GetAttribute("data-copied"));
    }
}
