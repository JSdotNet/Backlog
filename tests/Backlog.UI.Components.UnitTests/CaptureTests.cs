using Backlog.UI.Components.Capture;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The phone's capture parts on their own: the sheet whose words follow the
/// choice, its status line after a save, its three ways out, and a capture row
/// that says where it stands and offers nothing that would triage it.
/// </summary>
public sealed class CaptureTests
{
    [Theory]
    [InlineData(CaptureTarget.Inbox, "Add to inbox", "Sort it out later — it waits in the inbox", "What is on your mind?")]
    [InlineData(CaptureTarget.Note, "Save note", "A note keeps thoughts, not to-dos", "Start writing…")]
    [InlineData(CaptureTarget.Today, "Add to today", "Lands in Anytime today", "What needs doing today?")]
    public void The_hint_the_prompt_and_the_button_follow_the_choice(CaptureTarget target, string save, string hint, string placeholder)
    {
        using var context = Context();

        var sheet = context.Render<CaptureSheet>(parameters => parameters
            .Add(p => p.Target, target)
            .Add(p => p.TestId, "sheet"));

        Assert.Equal(save, sheet.Find("[data-testid='sheet-save']").TextContent.Trim());
        Assert.Equal(hint, sheet.Find("[data-testid='sheet-text'] label").TextContent.Trim());
        Assert.Equal(placeholder, sheet.Find("[data-testid='sheet-text'] textarea").GetAttribute("placeholder"));

        var pressed = Assert.Single(sheet.FindAll("[data-testid='sheet-targets'] [aria-pressed='true']"));
        Assert.Equal(CaptureTargets.Label(target), pressed.TextContent.Trim());
    }

    [Fact]
    public void The_sheet_opens_on_the_inbox_and_offers_no_when_or_project()
    {
        using var context = Context();

        var sheet = context.Render<CaptureSheet>(parameters => parameters.Add(p => p.TestId, "sheet"));

        Assert.Equal("true", sheet.Find("[data-testid='sheet-target-inbox']").GetAttribute("aria-pressed"));
        Assert.Equal(["Inbox", "Note", "Today"], sheet.FindAll("[data-testid='sheet-targets'] button").Select(b => b.TextContent.Trim()));
        Assert.DoesNotContain("When", sheet.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Project", sheet.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Picking_a_choice_raises_it()
    {
        using var context = Context();
        CaptureTarget? picked = null;

        var sheet = context.Render<CaptureSheet>(parameters => parameters
            .Add(p => p.TargetChanged, target => picked = target)
            .Add(p => p.TestId, "sheet"));

        sheet.Find("[data-testid='sheet-target-note']").Click();

        Assert.Equal(CaptureTarget.Note, picked);
    }

    [Theory]
    [InlineData(CaptureTarget.Inbox, "Added to inbox — capture another")]
    [InlineData(CaptureTarget.Note, "Note saved — capture another")]
    [InlineData(CaptureTarget.Today, "Added to today — capture another")]
    public void The_status_line_says_where_the_last_one_went(CaptureTarget saved, string status)
    {
        using var context = Context();

        var sheet = context.Render<CaptureSheet>(parameters => parameters
            .Add(p => p.SavedAs, saved)
            .Add(p => p.TestId, "sheet"));

        var line = sheet.Find("[data-testid='sheet-status']");
        Assert.Equal(status, line.TextContent.Trim());
        Assert.Equal("status", line.GetAttribute("role"));
    }

    [Fact]
    public void Before_a_save_the_status_line_is_there_and_empty()
    {
        using var context = Context();

        var sheet = context.Render<CaptureSheet>(parameters => parameters.Add(p => p.TestId, "sheet"));

        Assert.Equal(string.Empty, sheet.Find("[data-testid='sheet-status']").TextContent.Trim());
    }

    [Fact]
    public void Cancel_a_tap_outside_and_escape_all_leave()
    {
        using var context = Context();
        var left = 0;

        var sheet = context.Render<CaptureSheet>(parameters => parameters
            .Add(p => p.OnCancel, () => left++)
            .Add(p => p.TestId, "sheet"));

        sheet.Find("[data-testid='sheet-cancel']").Click();
        sheet.Find("[data-testid='sheet-backdrop']").Click();
        sheet.Find("[role='dialog']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(3, left);
    }

    [Fact]
    public void Save_is_withheld_until_the_host_says_there_is_something_to_send()
    {
        using var context = Context();
        var saves = 0;

        var sheet = context.Render<CaptureSheet>(parameters => parameters
            .Add(p => p.CanSave, false)
            .Add(p => p.OnSave, () => saves++)
            .Add(p => p.TestId, "sheet"));

        Assert.True(sheet.Find("[data-testid='sheet-save']").HasAttribute("disabled"));

        sheet.Render(parameters => parameters.Add(p => p.CanSave, true));
        sheet.Find("[data-testid='sheet-save']").Click();

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Photo_is_left_out_when_the_host_says_none_can_go_and_the_mic_is_disabled_without_a_recogniser()
    {
        using var context = Context();

        var sheet = context.Render<CaptureSheet>(parameters => parameters
            .Add(p => p.PhotoAvailable, false)
            .Add(p => p.DictationAvailable, false)
            .Add(p => p.TestId, "sheet"));

        Assert.Empty(sheet.FindAll("[data-testid='sheet-photo']"));
        Assert.True(sheet.Find("[data-testid='sheet-dictate']").HasAttribute("disabled"));
    }

    [Fact]
    public void A_waiting_row_says_so_in_words_and_offers_nothing_to_triage()
    {
        using var context = Context();

        var row = context.Render<CaptureRow>(parameters => parameters
            .Add(p => p.Title, "Ask Anna about the offsite budget")
            .Add(p => p.Kind, "text")
            .Add(p => p.Time, "just now")
            .Add(p => p.State, CaptureSyncState.Waiting)
            .Add(p => p.TestId, "row"));

        Assert.Equal("waiting", row.Find("[data-testid='row']").GetAttribute("data-state"));
        Assert.Equal("Waiting", row.Find("[data-testid='row-state']").TextContent.Trim());
        Assert.Equal("just now", row.Find("[data-testid='row-time']").TextContent.Trim());
        Assert.Single(row.FindAll("button"));
        foreach (var triage in new[] { "Today", "Later", "To note", "Delete", "Dismiss" })
        {
            Assert.DoesNotContain(triage, row.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_sent_row_says_sent_and_a_stuck_one_retries_on_a_tap()
    {
        using var context = Context();
        var retries = 0;

        var sent = context.Render<CaptureRow>(parameters => parameters
            .Add(p => p.Title, "Renew the domain")
            .Add(p => p.State, CaptureSyncState.Sent)
            .Add(p => p.TestId, "sent"));
        var stuck = context.Render<CaptureRow>(parameters => parameters
            .Add(p => p.Title, "Keynote notes")
            .Add(p => p.State, CaptureSyncState.Stuck)
            .Add(p => p.StuckReason, "The service answered 503.")
            .Add(p => p.OnRetry, () => retries++)
            .Add(p => p.TestId, "stuck"));

        Assert.Equal("Sent", sent.Find("[data-testid='sent-state']").TextContent.Trim());

        var retry = stuck.Find("[data-testid='stuck-retry']");
        Assert.Equal("Waiting — tap to retry", retry.TextContent.Trim());
        Assert.Equal("The service answered 503.", retry.GetAttribute("title"));
        retry.Click();
        Assert.Equal(1, retries);
    }

    [Fact]
    public void A_row_opens_on_a_tap_and_shows_its_first_line()
    {
        using var context = Context();
        var opened = 0;

        var row = context.Render<CaptureRow>(parameters => parameters
            .Add(p => p.Title, "Ask about the offsite")
            .Add(p => p.Preview, "Dates, budget, who drives.")
            .Add(p => p.OnOpen, () => opened++)
            .Add(p => p.TestId, "row"));

        Assert.Equal("Dates, budget, who drives.", row.Find("[data-testid='row-preview']").TextContent);
        row.Find("[data-testid='row-open']").Click();
        Assert.Equal(1, opened);
    }

    [Fact]
    public void The_sheet_traps_focus_unless_the_host_turns_it_off()
    {
        using var context = Context();

        context.Render<CaptureSheet>();
        Assert.Single(context.JSInterop.Invocations, invocation => invocation.Identifier == "backlogFocusTrap");

        using var sampler = Context();
        sampler.Render<CaptureSheet>(parameters => parameters.Add(p => p.TrapFocus, false));
        Assert.DoesNotContain(sampler.JSInterop.Invocations, invocation => invocation.Identifier == "backlogFocusTrap");
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
