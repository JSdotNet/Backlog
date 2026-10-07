using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Tasks;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The capture sheet at <c>/capture</c>, as the whole app renders it: a bottom
/// sheet over the screen it was opened from, whose one save goes to the Inbox
/// through the quick capture, to a note through the phone's notes, or to today
/// through My Day's add — and which stays open, cleared, saying where the last
/// one went.
/// </summary>
public sealed class CaptureSheetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void The_sheet_opens_over_today_on_the_inbox_choice()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        var app = Open(host);

        Assert.Equal("Capture", app.Find("h1").TextContent);
        Assert.Equal("true", app.Find("[data-testid='capture-sheet-target-inbox']").GetAttribute("aria-pressed"));
        Assert.Equal("Add to inbox", app.Find("[data-testid='capture-sheet-save']").TextContent.Trim());

        var behind = app.Find("[data-testid='capture-behind']");
        Assert.True(behind.HasAttribute("inert"));
        app.WaitForAssertion(() => Assert.NotNull(behind.QuerySelector("[data-testid='today-date']")));
    }

    [Theory]
    [InlineData("capture", "")]
    [InlineData("capture?from=inbox", "inbox")]
    [InlineData("capture?from=https%3A%2F%2Felsewhere.test", "")]
    public void Cancel_returns_to_where_the_sheet_came_from(string route, string back)
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        var app = Open(host, route);

        app.Find("[data-testid='capture-sheet-cancel']").Click();

        Assert.Equal(host.Navigation.BaseUri + back, host.Navigation.Uri);
    }

    [Fact]
    public void A_tap_outside_returns_to_the_inbox_it_was_opened_over()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        var app = Open(host, "capture?from=inbox");

        Assert.NotNull(app.Find("[data-testid='capture-behind'] [data-testid='capture-field']"));

        app.Find("[data-testid='capture-sheet-backdrop']").Click();

        Assert.Equal(host.Navigation.BaseUri + "inbox", host.Navigation.Uri);
    }

    [Fact]
    public void Inbox_sends_a_text_capture_and_the_sheet_stays_open_cleared()
    {
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = Open(host);

        Type(app, "Ask Anna about the offsite budget");
        app.Find("[data-testid='capture-sheet-save']").Click();

        app.WaitForAssertion(() =>
            Assert.Equal("Added to inbox — capture another", app.Find("[data-testid='capture-sheet-status']").TextContent.Trim()));

        var entry = Assert.Single(host.Outbox.Entries);
        Assert.Equal(CaptureOutboxKind.Token, entry.Kind);
        Assert.Equal("Ask Anna about the offsite budget", CaptureOutboxKind.Read(entry).Title);
        Assert.Equal(string.Empty, TextValue(app));
        Assert.Equal(host.Navigation.BaseUri + "capture", host.Navigation.Uri);
    }

    [Fact]
    public void Note_creates_a_note_through_the_phones_notes()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        var app = Open(host);

        app.Find("[data-testid='capture-sheet-target-note']").Click();
        Assert.Equal("A note keeps thoughts, not to-dos", app.Find("[data-testid='capture-sheet-text'] label").TextContent);
        Assert.Equal("Save note", app.Find("[data-testid='capture-sheet-save']").TextContent.Trim());

        Type(app, "Three ideas from the async stand-ups keynote");
        app.Find("[data-testid='capture-sheet-save']").Click();

        app.WaitForAssertion(() =>
            Assert.Equal("Note saved — capture another", app.Find("[data-testid='capture-sheet-status']").TextContent.Trim()));

        var note = Assert.Single(host.Service<NoteViewProjection>().Notes());
        Assert.Equal("Three ideas from the async stand-ups keynote", note.Note.ContentMd);
        app.WaitForAssertion(() => Assert.Contains(host.Tasks.Pushed, change => change.Id == note.Id && change.Task.Type == NoteFold.NoteType));
        Assert.Empty(host.Inbox.Received);
    }

    [Fact]
    public void Today_adds_a_task_picked_for_today()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        var app = Open(host);

        app.Find("[data-testid='capture-sheet-target-today']").Click();
        Assert.Equal("Add to today", app.Find("[data-testid='capture-sheet-save']").TextContent.Trim());
        Assert.Empty(app.FindAll("[data-testid='capture-sheet-photo']"));

        Type(app, "Renew the domain");
        app.Find("[data-testid='capture-sheet-save']").Click();

        app.WaitForAssertion(() =>
            Assert.Equal("Added to today — capture another", app.Find("[data-testid='capture-sheet-status']").TextContent.Trim()));

        var task = Assert.Single(host.Service<TaskViewProjection>().Day(Today));
        Assert.Equal("Renew the domain", task.Task.Title);
        Assert.Equal(Today, task.Task.InMyDayOn);
        Assert.Empty(host.Inbox.Received);
    }

    [Fact]
    public void Picking_another_choice_clears_the_line_about_the_last_save()
    {
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = Open(host);

        Type(app, "Buy a present for Sam");
        app.Find("[data-testid='capture-sheet-save']").Click();
        app.WaitForAssertion(() => Assert.NotEmpty(app.Find("[data-testid='capture-sheet-status']").TextContent.Trim()));

        app.Find("[data-testid='capture-sheet-target-note']").Click();

        Assert.Equal(string.Empty, app.Find("[data-testid='capture-sheet-status']").TextContent.Trim());
    }

    [Fact]
    public void A_photo_goes_to_the_inbox_with_the_capture()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.Picker.Next = AttachmentPick.Of([host.Picker.File("slide.gif", "image/gif", "GIF89a slide"u8.ToArray())]);
        var app = Open(host);

        Type(app, "The slide with the roadmap");
        app.Find("[data-testid='capture-sheet-photo']").Click();
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='attachment-tile']")));

        app.Find("[data-testid='capture-sheet-save']").Click();

        app.WaitForAssertion(() =>
        {
            var capture = Assert.Single(host.Inbox.Received);
            Assert.Equal("The slide with the roadmap", capture.Title);
            Assert.Single(capture.Attachments!);
            Assert.Single(host.Inbox.Uploads);
        });
        Assert.Empty(app.FindAll("[data-testid='attachment-tile']"));
    }

    [Fact]
    public void A_photo_cannot_go_with_a_task_for_today()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.Picker.Next = AttachmentPick.Of([host.Picker.File("slide.gif", "image/gif", "GIF89a slide"u8.ToArray())]);
        var app = Open(host);

        Type(app, "Print the slide");
        app.Find("[data-testid='capture-sheet-photo']").Click();
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='attachment-tile']")));

        app.Find("[data-testid='capture-sheet-target-today']").Click();

        Assert.NotNull(app.Find("[data-testid='capture-sheet-photo-notice']"));
        Assert.True(app.Find("[data-testid='capture-sheet-save']").HasAttribute("disabled"));
    }

    [Fact]
    public void Dictation_is_offered_when_the_device_can_listen_and_extends_the_text()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.Speech.Supported = true;
        var app = Open(host);

        Type(app, "buy milk");
        StartListening(app, host);
        app.WaitForAssertion(() =>
        {
            Assert.Equal("true", app.Find("[data-testid='capture-sheet-dictate']").GetAttribute("aria-pressed"));
            Assert.Contains("Listening", app.Find("[data-testid='capture-sheet-speech-status']").TextContent);
        });

        host.Speech.Complete(SpeechTranscript.Heard("and eggs"));

        app.WaitForAssertion(() =>
        {
            Assert.Equal("buy milk and eggs", TextValue(app));
            Assert.Contains("and eggs", app.Find("[data-testid='capture-sheet-speech-status']").TextContent);
            Assert.Equal("false", app.Find("[data-testid='capture-sheet-dictate']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void Pressing_the_mic_again_asks_the_recogniser_to_finish()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.Speech.Supported = true;
        var app = Open(host);

        StartListening(app, host);
        app.Find("[data-testid='capture-sheet-dictate']").Click();

        app.WaitForAssertion(() => Assert.Equal(1, host.Speech.StopCount));
    }

    [Fact]
    public void A_recogniser_that_fails_says_so_and_leaves_the_text_alone()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.Speech.Supported = true;
        var app = Open(host);

        Type(app, "buy milk");
        StartListening(app, host);
        host.Speech.Complete(SpeechTranscript.Failed("Microphone access was denied."));

        app.WaitForAssertion(() =>
        {
            Assert.Contains("denied", app.Find("[data-testid='capture-sheet-speech-error']").TextContent);
            Assert.Equal("buy milk", TextValue(app));
        });
    }

    [Fact]
    public void A_device_without_a_recogniser_gets_a_disabled_mic_and_is_told_why()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        var app = Open(host);

        app.WaitForAssertion(() =>
        {
            Assert.True(app.Find("[data-testid='capture-sheet-dictate']").HasAttribute("disabled"));
            Assert.Contains("not available", app.Find("[data-testid='capture-sheet-speech-unavailable']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Leaving_while_dictating_turns_the_microphone_off()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.Speech.Supported = true;
        var app = Open(host);

        StartListening(app, host);
        app.Find("[data-testid='capture-sheet-cancel']").Click();

        app.WaitForAssertion(() => Assert.Equal(1, host.Speech.StopCount));
        Assert.Empty(app.FindAll("[data-testid='capture-sheet']"));
    }

    [Fact]
    public void Typing_the_next_thought_clears_the_line_about_the_last_one()
    {
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = Open(host);

        Type(app, "First thought");
        app.Find("[data-testid='capture-sheet-save']").Click();
        app.WaitForAssertion(() => Assert.NotEmpty(app.Find("[data-testid='capture-sheet-status']").TextContent.Trim()));

        Type(app, "Second thought");

        Assert.Equal(string.Empty, app.Find("[data-testid='capture-sheet-status']").TextContent.Trim());
    }

    private static IRenderedComponent<Routes> Open(ShellHost host, string route = "capture")
    {
        var app = host.Open(route);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='capture-sheet']")));
        return app;
    }

    private static void Type(IRenderedComponent<Routes> app, string text) =>
        app.Find("[data-testid='capture-sheet-text'] textarea").Input(text);

    private static string TextValue(IRenderedComponent<Routes> app) =>
        app.Find("[data-testid='capture-sheet-text'] textarea").GetAttribute("value") ?? string.Empty;

    private static void StartListening(IRenderedComponent<Routes> app, ShellHost host)
    {
        app.WaitForAssertion(() => Assert.False(app.Find("[data-testid='capture-sheet-dictate']").HasAttribute("disabled")));
        app.Find("[data-testid='capture-sheet-dictate']").Click();
        app.WaitForAssertion(() => Assert.True(host.Speech.IsListening));
    }
}
