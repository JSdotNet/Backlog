using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Components.Pages;
using Backlog.Mobile.UI.Notes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Notes tab and its editor as a person meets them: every note newest first,
/// a search that narrows the list on the phone, an empty list that says how to
/// start one, and an editor that saves on its own a second after typing stops —
/// saying "Saved", or "Waiting to sync" while the outbox still holds the note.
/// </summary>
public sealed class NotesScreenTests
{
    /// <summary>Wednesday 7 October, half past two, on a phone whose local zone
    /// is UTC — the fake clock's.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_list_shows_every_note_newest_first_with_its_time_and_the_opening_of_its_body()
    {
        var books = Note("Books to read", "Shape Up. *The Making of a Manager*.", Now.AddDays(-5));
        var call = Note("Conflict cases from the call", "- Last write wins for the **title**.", Now.AddHours(-4).AddMinutes(-40));
        var sprint = Note("Sprint goals — draft", "Ship the phone Today view.", Now.AddDays(-1));
        var tasks = new ScriptedTaskService(books, call, sprint);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);

        var app = host.Open("notes");

        app.WaitForAssertion(() => Assert.Equal(3, app.FindAll("[data-testid='note-row']").Count));
        Assert.Equal("Notes", app.Find("h1").TextContent);
        Assert.Equal("3 notes", app.Find("[data-testid='notes-count']").TextContent);

        var rows = app.FindAll("[data-testid='note-row']");
        Assert.Equal(
            ["Conflict cases from the call", "Sprint goals — draft", "Books to read"],
            rows.Select(row => row.QuerySelector("[data-testid='note-row-title']")!.TextContent));
        Assert.Equal(
            ["09:50", "Yesterday", "Fri"],
            rows.Select(row => row.QuerySelector("[data-testid='note-row-when']")!.TextContent));
        Assert.Equal("Last write wins for the title.", rows[0].QuerySelector("[data-testid='note-row-snippet']")!.TextContent);
        Assert.Equal($"notes/{call.Id}", rows[0].GetAttribute("href"));

        Assert.Equal("notes/new", app.Find("[data-testid='notes-new']").GetAttribute("href"));
        Assert.Contains("New note", app.Find("[data-testid='notes-new']").TextContent, StringComparison.Ordinal);
        Assert.Contains("tab-bar__tab--active", app.Find("[data-testid='tab-bar-notes']").ClassList);
    }

    [Fact]
    public void Search_narrows_the_list_on_title_and_body_and_says_when_nothing_matches()
    {
        var tasks = new ScriptedTaskService(
            Note("Conflict cases from the call", "Last write wins.", Now.AddHours(-1)),
            Note("Offsite ideas", "Somewhere reachable by train.", Now.AddHours(-2)),
            Note("Books to read", "A Philosophy of Software Design.", Now.AddHours(-3)));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.Equal(3, app.FindAll("[data-testid='note-row']").Count));

        // A word in one note's body, and a title word in another's, in any case.
        app.Find("[data-testid='notes-search'] input").Input("TRAIN");
        app.WaitForAssertion(() => Assert.Equal(
            ["Offsite ideas"],
            app.FindAll("[data-testid='note-row-title']").Select(title => title.TextContent)));
        Assert.Equal("1 note", app.Find("[data-testid='notes-count']").TextContent);

        app.Find("[data-testid='notes-search'] input").Input("books design");
        app.WaitForAssertion(() => Assert.Equal(
            ["Books to read"],
            app.FindAll("[data-testid='note-row-title']").Select(title => title.TextContent)));

        app.Find("[data-testid='notes-search'] input").Input("roadmap");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notes-no-match']")));
        Assert.Empty(app.FindAll("[data-testid='note-row']"));
        Assert.Contains("“roadmap”", app.Find("[data-testid='notes-no-match']").TextContent, StringComparison.Ordinal);

        // Searching asks nothing of the service: the one pull was the page's own.
        Assert.Equal(1, tasks.Pulls);
    }

    [Fact]
    public void With_no_notes_the_tab_says_how_to_start_one_and_offers_no_search()
    {
        var tasks = new ScriptedTaskService();
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);

        var app = host.Open("notes");

        app.WaitForAssertion(() => Assert.Equal(1, tasks.Pulls));
        var empty = app.Find("[data-testid='notes-empty']");
        Assert.Contains("No notes yet", empty.TextContent, StringComparison.Ordinal);
        Assert.Contains("New note", empty.TextContent, StringComparison.Ordinal);
        Assert.Empty(app.FindAll("[data-testid='notes-search']"));
        Assert.Equal("0 notes", app.Find("[data-testid='notes-count']").TextContent);
        Assert.NotNull(app.Find("[data-testid='notes-new']"));
    }

    [Fact]
    public void A_new_note_is_created_a_second_after_typing_stops_and_is_then_in_the_list()
    {
        var clock = new FakeTimeProvider(Now);
        var tasks = new ScriptedTaskService();
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var notes = host.Service<NoteViewProjection>();
        var app = OpenEditor(host, "notes/new");

        // A new note says nothing about saving until it has something in it.
        Assert.Empty(app.FindAll("[data-testid='note-editor-save']"));
        Assert.Empty(app.FindAll("[data-testid='note-editor-when']"));

        app.Find("[data-testid='note-editor-title'] input").Input("Standup");
        clock.Advance(TimeSpan.FromMilliseconds(600));
        app.Find("[data-testid='note-editor-body'] textarea").Input("- shipped sync");

        // Each keystroke restarts the wait: 600 ms after the first and 900 ms
        // after the last, nothing is written yet.
        clock.Advance(TimeSpan.FromMilliseconds(900));
        Assert.Empty(notes.Notes());
        Assert.Equal("Saving...", app.Find("[data-testid='note-editor-save']").TextContent.Trim());

        clock.Advance(TimeSpan.FromMilliseconds(100));

        app.WaitForAssertion(() => Assert.Equal("Saved", app.Find("[data-testid='note-editor-save']").TextContent.Trim()));
        var note = Assert.Single(notes.Notes());
        Assert.Equal("Standup", note.Title);
        Assert.Equal("- shipped sync", note.Body);
        Assert.Equal(note.Id, Assert.Single(tasks.Pushed).Id);
        Assert.Equal("Wed 7 Oct · 14:30", app.Find("[data-testid='note-editor-when']").TextContent);

        // Typing on edits the same note rather than making a second one.
        app.Find("[data-testid='note-editor-body'] textarea").Input("- shipped sync\n- notes next");
        clock.Advance(NotePage.SaveDelay);

        app.WaitForAssertion(() => Assert.Equal(2, tasks.Pushed.Count));
        Assert.Equal("- shipped sync\n- notes next", Assert.Single(notes.Notes()).Body);
        Assert.All(tasks.Pushed, pushed => Assert.Equal(note.Id, pushed.Id));

        host.Navigation.NavigateTo("notes");
        app.WaitForAssertion(() => Assert.Equal(
            "Standup",
            app.Find("[data-testid='note-row-title']").TextContent));
    }

    [Fact]
    public void Opening_a_new_note_and_leaving_it_empty_creates_nothing()
    {
        var clock = new FakeTimeProvider(Now);
        var tasks = new ScriptedTaskService();
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = OpenEditor(host, "notes/new");

        app.Find("[data-testid='note-editor-title'] input").Input("   ");
        clock.Advance(NotePage.SaveDelay);
        host.Navigation.NavigateTo("notes");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notes-empty']")));
        Assert.Empty(host.Service<NoteViewProjection>().Notes());
        Assert.Empty(tasks.Pushed);
    }

    [Fact]
    public void An_existing_note_opens_with_its_words_and_an_edit_is_saved_over_it()
    {
        var clock = new FakeTimeProvider(Now);
        var call = Note("Conflict cases from the call", "Last write wins for the title.", Now.AddHours(-4).AddMinutes(-40));
        var tasks = new ScriptedTaskService(call);
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));

        host.Navigation.NavigateTo($"notes/{call.Id}");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-editor']")));

        Assert.Equal("Conflict cases from the call", app.Find("[data-testid='note-editor-title'] input").GetAttribute("value"));
        Assert.Equal("Last write wins for the title.", app.Find("[data-testid='note-editor-body'] textarea").GetAttribute("value"));
        Assert.Equal("Wed 7 Oct · 09:50", app.Find("[data-testid='note-editor-when']").TextContent);
        Assert.Equal("Saved", app.Find("[data-testid='note-editor-save']").TextContent.Trim());
        Assert.Equal("notes", app.Find("[data-testid='note-editor-back']").GetAttribute("href"));

        app.Find("[data-testid='note-editor-body'] textarea").Input("Last write wins for the title.\nSub-items merge by id.");
        clock.Advance(NotePage.SaveDelay);

        app.WaitForAssertion(() => Assert.Single(tasks.Pushed));
        var pushed = tasks.Pushed[0];
        Assert.Equal(call.Id, pushed.Id);
        Assert.Equal("Conflict cases from the call", pushed.Task.Title);
        Assert.Equal("Last write wins for the title.\nSub-items merge by id.", pushed.Task.ContentMd);
        Assert.Equal(NoteFold.NoteType, pushed.Task.Type);
        app.WaitForAssertion(() => Assert.Equal("Wed 7 Oct · 14:30", app.Find("[data-testid='note-editor-when']").TextContent));
    }

    [Fact]
    public void Leaving_the_editor_before_the_second_is_up_still_saves_the_edit()
    {
        var clock = new FakeTimeProvider(Now);
        var call = Note("Standup", "Shipped sync.", Now.AddHours(-1));
        var tasks = new ScriptedTaskService(call);
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));
        host.Navigation.NavigateTo($"notes/{call.Id}");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-editor']")));

        app.Find("[data-testid='note-editor-title'] input").Input("Standup, Wednesday");
        host.Navigation.NavigateTo("notes");

        app.WaitForAssertion(() => Assert.Equal(
            "Standup, Wednesday",
            app.Find("[data-testid='note-row-title']").TextContent));
        Assert.Equal("Standup, Wednesday", Assert.Single(tasks.Pushed).Task.Title);
    }

    /// <summary>
    /// Back is pressed straight after typing: the field's blur saves the new note
    /// and the link still lands on the list, with the note in it.
    /// </summary>
    [Fact]
    public void Leaving_a_new_note_by_its_back_link_saves_it_and_lands_on_the_list()
    {
        var clock = new FakeTimeProvider(Now);
        var tasks = new ScriptedTaskService();
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = OpenEditor(host, "notes/new");

        app.Find("[data-testid='note-editor-body'] textarea").Input("Offsite: somewhere reachable by train.");
        app.Find("[data-testid='note-editor-body'] textarea").Blur();
        host.Navigation.NavigateTo("notes");

        app.WaitForAssertion(() => Assert.Equal(
            "Offsite: somewhere reachable by train.",
            app.Find("[data-testid='note-row-title']").TextContent));
        Assert.EndsWith("/notes", host.Navigation.Uri, StringComparison.Ordinal);
        Assert.Single(tasks.Pushed);
    }

    [Fact]
    public async Task The_indicator_says_waiting_to_sync_while_the_outbox_holds_the_note_and_saved_once_it_is_sent()
    {
        var clock = new FakeTimeProvider(Now);
        var tasks = new ScriptedTaskService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = OpenEditor(host, "notes/new");

        app.Find("[data-testid='note-editor-body'] textarea").Input("Written on the train.");
        clock.Advance(NotePage.SaveDelay);

        app.WaitForAssertion(() =>
        {
            var save = app.Find("[data-testid='note-editor-save']");
            Assert.Equal("Waiting to sync", save.TextContent.Trim());
            Assert.Equal("true", save.GetAttribute("data-waiting"));
        });

        // Kept on the phone: in the list at once, and in the outbox to send.
        var note = Assert.Single(host.Service<NoteViewProjection>().Notes());
        Assert.Equal("Written on the train.", note.Title);
        Assert.Single(host.Outbox.Entries);

        tasks.State = InboxServiceState.Answering;
        await host.Outbox.ResumeAsync(TestContext.Current.CancellationToken);

        app.WaitForAssertion(() =>
        {
            var save = app.Find("[data-testid='note-editor-save']");
            Assert.Equal("Saved", save.TextContent.Trim());
            Assert.Equal("false", save.GetAttribute("data-waiting"));
        });
        Assert.Empty(host.Outbox.Entries);
    }

    [Fact]
    public void A_picked_file_is_saved_with_the_note_at_once_and_a_refused_one_stays_off_it_saying_why()
    {
        var clock = new FakeTimeProvider(Now);
        var tasks = new ScriptedTaskService();
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = OpenEditor(host, "notes/new");

        host.Picker.Next = AttachmentPick.Of([host.Picker.Text("handout.pdf", "application/pdf", "%PDF-1.4 handout")]);
        app.Find("[data-testid='note-choose-files']").Click();

        // No typing delay for a pick: the note exists at once, naming the file.
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='note-attachments'] [data-testid='attachment-tile']")));
        var note = Assert.Single(host.Service<NoteViewProjection>().Notes());
        Assert.Equal("handout.pdf", Assert.Single(note.Attachments).Name);
        Assert.Equal("handout.pdf", note.Title);
        Assert.Empty(app.FindAll("[data-testid='note-attachments'] [data-testid='attachment-tile-remove']"));

        host.Picker.Next = AttachmentPick.Of([host.Picker.Text("setup.exe", "application/x-msdownload", "MZ")]);
        app.Find("[data-testid='note-choose-files']").Click();

        app.WaitForAssertion(() => Assert.Contains(
            ".exe",
            app.Find("[data-testid='note-pending-attachments'] [data-testid='attachment-tile-refusal']").TextContent,
            StringComparison.Ordinal));
        Assert.NotNull(app.Find("[data-testid='note-refused-notice']"));
        Assert.Single(Assert.Single(host.Service<NoteViewProjection>().Notes()).Attachments);

        app.Find("[data-testid='note-pending-attachments'] [data-testid='attachment-tile-remove']").Click();
        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='note-refused-notice']")));
    }

    [Fact]
    public void A_note_the_phone_does_not_hold_says_so_and_links_back()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));

        var app = host.Open($"notes/{Guid.CreateVersion7()}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-missing']")));
        Assert.Equal("notes", app.Find("[data-testid='note-missing-back']").GetAttribute("href"));
        Assert.Empty(app.FindAll("[data-testid='note-editor']"));
    }

    private static IRenderedComponent<Routes> OpenEditor(ShellHost host, string route)
    {
        var app = host.Open(route);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-editor']")));
        return app;
    }

    private static TaskChange Note(string title, string body, DateTimeOffset at) =>
        NoteViewProjection.NewNote(Guid.CreateVersion7(), title, body, [], at);
}
