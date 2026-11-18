using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Notes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone Inbox as one list (.devbook/domain/inbox/requirements.md#notes-on-the-phone,
/// #meetings-on-the-phone; .devbook/design/interaction-guidelines.md#phone-inbox): the
/// captures this phone made beside the notes and meetings, each row marked with its kind,
/// four kind chips of which exactly one is pressed, and a + menu of Note and Meeting —
/// Meeting opening a new meeting already recording.
/// </summary>
public sealed class PhoneInboxScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Captures_notes_and_meetings_share_one_list_each_marked_with_its_kind()
    {
        var (host, app) = Open();
        using var disposing = host;

        app.WaitForAssertion(() => Assert.Equal(3, app.FindAll("[data-testid='inbox-list'] > li").Count));

        Assert.Equal(["Weekly sync", "Release plan"], app.FindAll("[data-testid='note-row-title']").Select(title => title.TextContent));
        Assert.NotNull(app.Find("[data-testid='inbox-row']"));
        Assert.Equal(["Meeting", "Note"], app.FindAll("[data-testid='note-row-kind'] .sr-only").Select(word => word.TextContent));
        Assert.Equal("Meeting", app.Find("[data-testid='note-row-kind']").GetAttribute("title"));
        Assert.NotNull(app.Find("[data-testid='inbox-row-kind'] .capture-kind-marker"));
        Assert.Equal(["Today", "Inbox"], app.FindAll("[data-testid='tab-bar'] a").Select(tab => tab.TextContent.Trim()));
    }

    [Fact]
    public void The_kind_chips_are_one_group_with_exactly_one_pressed_and_one_tab_stop()
    {
        var (host, app) = Open();
        using var disposing = host;
        app.WaitForAssertion(() => Assert.Equal(3, app.FindAll("[data-testid='inbox-list'] > li").Count));

        var group = app.Find("[data-testid='inbox-kinds']");
        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal("Kind", group.GetAttribute("aria-label"));
        Assert.True(group.HasAttribute("data-roving-buttons"));
        Assert.Equal(["All", "Captures", "Notes", "Meetings"], group.QuerySelectorAll("button").Select(chip => chip.TextContent.Trim()));
        Assert.Equal(["true", "false", "false", "false"], group.QuerySelectorAll("button").Select(chip => chip.GetAttribute("aria-pressed")));
        Assert.Equal(["0", "-1", "-1", "-1"], group.QuerySelectorAll("button").Select(chip => chip.GetAttribute("tabindex")));

        app.Find("[data-testid='inbox-kind-meetings']").Click();
        app.WaitForAssertion(() => Assert.Equal(["Weekly sync"], app.FindAll("[data-testid='note-row-title']").Select(title => title.TextContent)));
        Assert.Empty(app.FindAll("[data-testid='inbox-row']"));

        // A press on the pressed chip leaves it on.
        app.Find("[data-testid='inbox-kind-meetings']").Click();
        Assert.Equal("true", app.Find("[data-testid='inbox-kind-meetings']").GetAttribute("aria-pressed"));

        // The stop the page drew stays put: components.js moves it on the arrows, and a
        // render that moved it too could leave the group with two.
        Assert.Equal(
            ["0", "-1", "-1", "-1"],
            app.Find("[data-testid='inbox-kinds']").QuerySelectorAll("button").Select(chip => chip.GetAttribute("tabindex")));

        app.Find("[data-testid='inbox-kind-captures']").Click();
        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='note-row']")));
        Assert.Single(app.FindAll("[data-testid='inbox-row']"));

        app.Find("[data-testid='inbox-kind-all']").Click();
        app.WaitForAssertion(() => Assert.Equal(3, app.FindAll("[data-testid='inbox-list'] > li").Count));
    }

    [Fact]
    public void The_new_menu_offers_note_and_meeting_and_note_opens_an_empty_editor()
    {
        var (host, app) = Open();
        using var disposing = host;

        var button = app.Find("[data-testid='inbox-new']");
        Assert.Equal("New", button.GetAttribute("aria-label"));
        Assert.Equal("menu", button.GetAttribute("aria-haspopup"));

        button.Click();

        app.WaitForAssertion(() => Assert.Equal(
            ["Note", "Meeting"],
            app.FindAll("[data-testid='inbox-new-menu'] [role='menuitem']").Select(item => item.TextContent.Trim())));

        app.Find("[data-testid='inbox-new-menu-item-note']").Click();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-editor']")));
        Assert.EndsWith("/notes/new", host.Navigation.Uri, StringComparison.Ordinal);
        Assert.Empty(host.MeetingRecorder.Started);
    }

    [Fact]
    public void Meeting_opens_a_new_meeting_with_its_recording_running_and_the_inbox_lists_it()
    {
        var (host, app) = Open();
        using var disposing = host;

        app.Find("[data-testid='inbox-new']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-new-menu-item-meeting']")));
        app.Find("[data-testid='inbox-new-menu-item-meeting']").Click();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));
        var meeting = Assert.Single(host.Service<NoteViewProjection>().Notes(), note => note.IsMeeting && note.Title.StartsWith("Meeting · ", StringComparison.Ordinal));
        Assert.Equal(meeting.Id, Assert.Single(host.MeetingRecorder.Started));

        host.Navigation.NavigateTo("inbox");

        app.WaitForAssertion(() => Assert.Contains(
            "Meeting",
            app.FindAll("[data-testid='note-row-kind'] .sr-only").Select(word => word.TextContent)));
    }

    /// <summary>The meeting address is replaced by the meeting's own once it exists, so
    /// Back, or coming back, opens the same meeting rather than starting a second.</summary>
    [Fact]
    public void Returning_to_a_started_meeting_opens_it_again_and_makes_no_second()
    {
        var (host, app) = Open();
        using var disposing = host;
        host.Navigation.NavigateTo("notes/new?kind=meeting");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));
        var meeting = Assert.Single(Meetings(host));

        Assert.EndsWith($"/notes/{meeting.Id}", host.Navigation.Uri, StringComparison.Ordinal);

        host.Navigation.NavigateTo("inbox");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-list']")));
        host.Navigation.NavigateTo($"notes/{meeting.Id}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));
        Assert.Equal(meeting.Id, Assert.Single(Meetings(host)).Id);
        Assert.Equal(meeting.Id, Assert.Single(host.MeetingRecorder.Started));
    }

    [Fact]
    public void A_new_note_takes_its_own_address_once_it_is_saved()
    {
        var clock = new FakeTimeProvider(Now);
        using var host = ShellHost.Paired(clock: clock);
        var app = host.Open("notes/new");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-editor']")));

        app.Find("[data-testid='note-editor-body'] textarea").Input("Offsite ideas");
        clock.Advance(Components.Pages.NotePage.SaveDelay);

        var note = default(NoteViewRow);
        app.WaitForAssertion(() => note = Assert.Single(host.Service<NoteViewProjection>().Notes()));
        app.WaitForAssertion(() => Assert.EndsWith($"/notes/{note!.Id}", host.Navigation.Uri, StringComparison.Ordinal));
    }

    /// <summary>The editor's back link goes to the Inbox on All, where the meeting just
    /// made is listed — the Notes chip would hide it.</summary>
    [Fact]
    public void The_editors_back_link_leads_to_the_inbox_where_the_meeting_is_listed()
    {
        var (host, app) = Open();
        using var disposing = host;
        host.Navigation.NavigateTo("notes/new?kind=meeting");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-recording']")));

        var back = app.Find("[data-testid='note-editor-back']");
        Assert.Equal("inbox", back.GetAttribute("href"));
        Assert.Equal("Back to Inbox", back.GetAttribute("aria-label"));

        host.Navigation.NavigateTo(back.GetAttribute("href")!);

        app.WaitForAssertion(() => Assert.Equal("true", app.Find("[data-testid='inbox-kind-all']").GetAttribute("aria-pressed")));
        var meeting = Assert.Single(Meetings(host));
        Assert.Contains(meeting.Title, app.FindAll("[data-testid='note-row-title']").Select(title => title.TextContent));
    }

    [Fact]
    public void Where_the_phone_cannot_record_the_meeting_opens_without_a_recording_and_says_so()
    {
        var (host, app) = Open();
        using var disposing = host;
        host.MeetingRecorder.Supported = false;

        host.Navigation.NavigateTo("notes/new?kind=meeting");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-record-error']")));
        Assert.Single(Meetings(host));
        Assert.Empty(host.MeetingRecorder.Started);
        Assert.Empty(app.FindAll("[data-testid='note-recording']"));
    }

    [Fact]
    public async Task While_another_recording_runs_the_meeting_opens_without_one_and_says_why()
    {
        var (host, app) = Open();
        using var disposing = host;
        var running = host.Service<NoteViewProjection>().Notes().Single(note => note.Title == "Release plan");
        await app.InvokeAsync(() => host.Service<Backlog.Mobile.UI.Recordings.NoteRecordingSession>().StartAsync(running.Id));

        host.Navigation.NavigateTo("notes/new?kind=meeting");

        app.WaitForAssertion(() => Assert.Contains(
            "Another recording is running",
            app.Find("[data-testid='note-record-error']").TextContent,
            StringComparison.Ordinal));
        Assert.Equal(running.Id, Assert.Single(host.MeetingRecorder.Started));
        Assert.Single(Meetings(host));
    }

    [Fact]
    public void The_one_line_field_still_makes_a_capture()
    {
        var (host, app) = Open();
        using var disposing = host;
        app.WaitForAssertion(() => Assert.Equal(3, app.FindAll("[data-testid='inbox-list'] > li").Count));

        app.Find("[data-testid='capture-field'] input").Input("Book the venue");
        app.Find("[data-testid='capture-submit']").Click();

        app.WaitForAssertion(() => Assert.Equal(2, app.FindAll("[data-testid='inbox-row']").Count));
        Assert.Contains("Book the venue", app.Find("[data-testid='inbox-list']").TextContent, StringComparison.Ordinal);
    }

    private static (ShellHost Host, IRenderedComponent<Routes> App) Open()
    {
        var clock = new FakeTimeProvider(Now);
        var tasks = new ScriptedTaskService(
            Note("Release plan", null, Now.AddHours(-2)),
            Note("Weekly sync", "meeting", Now.AddHours(-1)));
        var inbox = new ScriptedInboxService(new InboxItem(Guid.CreateVersion7(), "Buy filters", "mobile", Now.AddHours(-3)));
        var host = ShellHost.Paired(inbox: inbox, clock: clock, tasks: tasks);

        return (host, host.Open("inbox"));
    }

    /// <summary>The meetings this phone started, apart from the seeded "Weekly sync".</summary>
    private static IReadOnlyList<NoteViewRow> Meetings(ShellHost host) =>
        [.. host.Service<NoteViewProjection>().Notes().Where(note => note.IsMeeting && note.Title.StartsWith("Meeting · ", StringComparison.Ordinal))];

    private static TaskChange Note(string title, string? noteKind, DateTimeOffset at) =>
        NoteViewProjection.NewNote(Guid.CreateVersion7(), title, "Body.", [], at, noteKind);
}
