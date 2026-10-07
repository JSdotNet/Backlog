using Backlog.Mobile.UI.Outbox;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Note tab as a person meets it: what outlives a look at another tab, what
/// a refused file says, and what saving does.
/// </summary>
public sealed class TalkNoteScreenTests
{
    /// <summary>
    /// The Router remounts the Note page on every navigation. A talk does not
    /// pause for a glance at the Inbox, so everything typed and attached has to
    /// be there on the way back.
    /// </summary>
    [Fact]
    public void A_draft_with_an_attachment_survives_a_trip_to_the_inbox_and_back()
    {
        using var host = ShellHost.Paired();
        host.Picker.Next = AttachmentPick.Of([host.Picker.Text("handout.pdf", "application/pdf", "%PDF-1.4 handout")]);

        var app = host.Open("note");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-send']")));

        app.Find("[data-testid='note-title'] input").Input("Offline-first keynote");
        app.Find("[data-testid='note-body'] textarea").Input("Outbox before network.");
        app.Find("[data-testid='note-speaker'] input").Input("@ada");
        app.Find("[data-testid='note-tags'] input").Input("#conference");
        app.Find("[data-testid='note-choose-files']").Click();
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='attachment-tile']")));

        host.Navigation.NavigateTo("inbox");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='capture-field'] input")));
        host.Navigation.NavigateTo("note");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-send']")));

        Assert.Equal("Offline-first keynote", app.Find("[data-testid='note-title'] input").GetAttribute("value"));
        Assert.Equal("Outbox before network.", app.Find("[data-testid='note-body'] textarea").GetAttribute("value"));
        Assert.Equal("@ada", app.Find("[data-testid='note-speaker'] input").GetAttribute("value"));
        Assert.Equal("#conference", app.Find("[data-testid='note-tags'] input").GetAttribute("value"));

        var tile = app.Find("[data-testid='attachment-tile']");
        Assert.Equal("handout.pdf", tile.QuerySelector("[data-testid='attachment-tile-name']")!.TextContent);
        Assert.Equal("PDF · 16 B", tile.QuerySelector("[data-testid='attachment-tile-facts']")!.TextContent);
    }

    [Fact]
    public void A_file_outside_the_allowlist_is_refused_on_its_tile_naming_why_and_is_never_read()
    {
        using var host = ShellHost.Paired();
        host.Picker.Next = AttachmentPick.Of([host.Picker.Text("setup.exe", "application/x-msdownload", "MZ")]);

        var app = OpenNote(host);
        app.Find("[data-testid='note-title'] input").Input("Tools from the talk");
        app.Find("[data-testid='note-choose-files']").Click();

        app.WaitForAssertion(() =>
        {
            var refusal = app.Find("[data-testid='attachment-tile-refusal']").TextContent;
            Assert.Contains(".exe", refusal, StringComparison.Ordinal);
            Assert.Contains("Pictures, PDF, text and Office files only", refusal, StringComparison.Ordinal);
        });

        Assert.Equal(0, host.Picker.Reads);
        Assert.True(app.Find("[data-testid='note-send']").HasAttribute("disabled"));
        Assert.NotNull(app.Find("[data-testid='note-refused-notice']"));

        // Taking it off the strip is what lets the note go.
        app.Find("[data-testid='attachment-tile-remove']").Click();
        app.WaitForAssertion(() => Assert.False(app.Find("[data-testid='note-send']").HasAttribute("disabled")));
    }

    [Fact]
    public void A_file_over_the_cap_is_refused_with_its_size_and_the_limit()
    {
        using var host = ShellHost.Paired();
        host.Picker.Next = AttachmentPick.Of([host.Picker.File("recording.pdf", "application/pdf", [], claimedSize: 30L * 1024 * 1024)]);

        var app = OpenNote(host);
        app.Find("[data-testid='note-choose-files']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            "Too large: 30 MB, and the limit is 25 MB.",
            app.Find("[data-testid='attachment-tile-refusal']").TextContent));
        Assert.Equal(0, host.Picker.Reads);
    }

    [Fact]
    public void A_speaker_with_a_space_is_asked_for_as_one_name()
    {
        using var host = ShellHost.Paired();

        var app = OpenNote(host);
        app.Find("[data-testid='note-title'] input").Input("Keynote");
        app.Find("[data-testid='note-speaker'] input").Input("@Ada Lovelace");

        app.WaitForAssertion(() => Assert.Contains("one name with no spaces", app.Markup, StringComparison.Ordinal));
        Assert.True(app.Find("[data-testid='note-send']").HasAttribute("disabled"));
    }

    [Fact]
    public void Saving_uploads_the_file_posts_the_capture_clears_the_fields_and_says_synced()
    {
        using var host = ShellHost.Paired();
        host.Picker.Next = AttachmentPick.Of([host.Picker.Text("handout.pdf", "application/pdf", "%PDF-1.4 handout")]);

        var app = OpenNote(host);
        app.Find("[data-testid='note-choose-files']").Click();
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='attachment-tile']")));

        app.Find("[data-testid='note-send']").Click();

        app.WaitForAssertion(() => Assert.Equal("synced", app.Find("[data-testid='note-status']").GetAttribute("data-status")));

        var upload = Assert.Single(host.Inbox.Uploads);
        var capture = Assert.Single(host.Inbox.Received);
        Assert.Equal("handout.pdf", capture.Title);
        Assert.Equal(upload.Id, Assert.Single(capture.Attachments!).Id);

        Assert.Empty(app.FindAll("[data-testid='attachment-tile']"));
        Assert.Equal(string.Empty, app.Find("[data-testid='note-title'] input").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void A_note_saved_offline_is_in_the_inbox_list_marked_waiting()
    {
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox);

        var app = OpenNote(host);
        app.Find("[data-testid='note-body'] textarea").Input("# Keynote\nOutbox before network.");
        app.Find("[data-testid='note-send']").Click();

        app.WaitForAssertion(() => Assert.Equal("waiting", app.Find("[data-testid='note-status']").GetAttribute("data-status")));
        Assert.Equal(TalkNoteOutboxKind.Token, Assert.Single(host.Outbox.Entries).Kind);

        host.Navigation.NavigateTo("inbox");

        app.WaitForAssertion(() =>
        {
            var row = app.Find("[data-testid='inbox-row']");
            Assert.Equal("waiting", row.GetAttribute("data-state"));
            Assert.Contains("Keynote", row.TextContent, StringComparison.Ordinal);
        });
    }

    private static IRenderedComponent<Backlog.Mobile.UI.Components.Routes> OpenNote(ShellHost host)
    {
        var app = host.Open("note");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-send']")));
        return app;
    }
}
