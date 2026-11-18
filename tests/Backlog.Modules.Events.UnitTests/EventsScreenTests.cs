using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Events.Services;
using Backlog.Modules.Events.UI;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Feedback;

using Bunit;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The Events pane's screens over the real pane service, its in-memory stores and the task and
/// Brain targets at the boundary: the list grouped by state, adding by title alone, the state
/// choice and its skip dialog, Remove and its confirmation, the Harvest chips, the task chips and Add prep task, and the jumps
/// out through the window's navigator.
/// </summary>
public sealed class EventsScreenTests : EventsPaneFixture, IDisposable
{
    private readonly BunitContext _context = new();
    private readonly RecordingNavigator _navigator = new();

    /// <summary>The Inbox's flag, <c>InboxFeatures.Pane</c>, as a string: the Events tests
    /// reference no Inbox project, as the Events module references none.</summary>
    private const string InboxPane = "inbox-pane";

    /// <summary>The device's flags, the Inbox on unless a test switches it off.</summary>
    private readonly FakeFeatureSwitches _features = new(new Dictionary<string, bool>(StringComparer.Ordinal) { [InboxPane] = true });

    public EventsScreenTests()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, Cet));
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("CET-screen", Cet, "CET", "CET"));

        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton(Pane);
        _context.Services.AddSingleton<ISurfaceNavigator>(_navigator);
        _context.Services.AddSingleton<TimeProvider>(clock);

        // The shell's catalog over the Inbox's surface as InboxSurfaceRegistration registers it:
        // offered only while the Inbox's flag is on.
        _context.Services.AddSingleton<ISurfaceCatalog>(new SurfaceCatalog(
            [new ShellSurface("inbox", "Inbox", ShellSurfaceGroup.Takeover, 10, typeof(InboxStandIn), InboxPane, QueryKeys: ["item"], EscapeCloses: false)],
            _features));
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<EventsSurface> RenderPane(IReadOnlyDictionary<string, string>? query = null) =>
        _context.Render<EventsSurface>(p => p.Add(c => c.Query, query));

    private IRenderedComponent<EventsSurface> RenderOn(Guid id) =>
        RenderPane(new Dictionary<string, string>(StringComparer.Ordinal) { [EventsSurfaceRegistration.EventKey] = id.ToString() });

    private const string StateSelect = "[data-testid='events-editor-state'] select";

    /// <summary>The state the editor's state choice stands on.</summary>
    private static string? State(IRenderedComponent<EventsSurface> pane) =>
        pane.Find(StateSelect).GetAttribute("value");

    /// <summary>What the state choice offers, in order: where the event stands first.</summary>
    private static string[] StateChoices(IRenderedComponent<EventsSurface> pane) =>
        [.. pane.FindAll(StateSelect + " option").Select(option => option.TextContent.Trim())];

    /// <summary>Picks a state and presses the button that applies the pick.</summary>
    private static void Choose(IRenderedComponent<EventsSurface> pane, string value)
    {
        pane.Find(StateSelect).Change(value);
        pane.Find("[data-testid='events-state-apply']").Click();
    }

    private static string Text(IRenderedComponent<EventsSurface> pane, string testId) =>
        pane.Find($"[data-testid='{testId}']").TextContent.Trim();

    // --- The list ------------------------------------------------------------------------

    [Fact]
    public void With_no_events_the_pane_says_so_and_offers_to_add_one()
    {
        var pane = RenderPane();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='events-pane-none']")));
        Assert.Empty(pane.FindAll("[data-testid='events-add']"));

        pane.Find("[data-testid='events-pane-none-add']").Click();

        Assert.NotNull(pane.Find("[data-testid='events-add']"));
    }

    [Fact]
    public async Task The_list_groups_events_by_state_with_upcoming_first_and_draws_no_empty_group()
    {
        var later = await Dated("Techorama Belgium", new DateOnly(2027, 5, 18));
        var sooner = await Dated("dotNET Friday", new DateOnly(2026, 11, 27));
        var going = await Dated("NDC Oslo", new DateOnly(2027, 6, 7));
        Assert.True((await Pane.MoveToAsync(going, KnowledgeEventState.Going, Token)).IsSuccess);

        var pane = RenderPane();

        pane.WaitForAssertion(() => Assert.Equal(2, pane.FindAll("[data-testid='events-group']").Count));
        var groups = pane.FindAll("[data-testid='events-group']");
        Assert.Equal(["Going", "Spotted"], groups.Select(group => group.GetAttribute("data-state")));
        Assert.Equal(
            ["dotNET Friday", "Techorama Belgium"],
            groups[1].QuerySelectorAll(".events-list__title").Select(title => title.TextContent));
        Assert.Equal("2", groups[1].QuerySelector("[data-testid='events-group-tally']")!.TextContent);
        Assert.Equal(
            "27 Nov 2026",
            pane.Find($"[data-testid='events-row-{sooner:N}'] [data-testid='events-row-line']").TextContent);
        Assert.NotNull(pane.Find($"[data-testid='events-row-{later:N}']"));
    }

    // --- Adding --------------------------------------------------------------------------

    [Fact]
    public async Task A_title_alone_adds_a_spotted_event_and_opens_it()
    {
        var pane = RenderPane();
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-add-open']"));

        pane.Find("[data-testid='events-add-open']").Click();
        pane.Find("[data-testid='events-add-title'] input").Input("dotNET Friday, 27 November");
        pane.Find("[data-testid='events-add-confirm']").Click();

        pane.WaitForAssertion(() => Assert.Equal("dotNET Friday, 27 November", Text(pane, "events-editor-title")));
        var listed = Assert.Single(await Pane.ListAsync(Token));
        Assert.Equal(KnowledgeEventState.Spotted, listed.State);
        Assert.Equal("Spotted", State(pane));
        Assert.Empty(pane.FindAll("[data-testid='events-add']"));
    }

    [Fact]
    public async Task A_blank_title_is_refused_in_the_modules_words_and_nothing_is_added()
    {
        var refusal = (await Pane.AddAsync("  ", cancellationToken: Token)).Error.Message;
        var pane = RenderPane();
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-add-open']"));

        pane.Find("[data-testid='events-add-open']").Click();
        pane.Find("[data-testid='events-add-confirm']").Click();

        pane.WaitForAssertion(() => Assert.Contains(refusal, pane.Find("[data-testid='events-add']").TextContent, StringComparison.Ordinal));
        Assert.Empty(await Pane.ListAsync(Token));
    }

    [Fact]
    public async Task A_series_name_that_matches_none_starts_a_series_for_the_new_event()
    {
        var pane = RenderPane();
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-add-open']"));

        pane.Find("[data-testid='events-add-open']").Click();
        pane.Find("[data-testid='events-add-title'] input").Input("Techorama Belgium 2027");
        pane.Find("[data-testid='events-add-series'] input").Input("Techorama");
        pane.Find("[data-testid='events-add-confirm']").Click();

        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium 2027", Text(pane, "events-editor-title")));
        Assert.Equal("Techorama", Assert.Single(await Pane.ListSeriesAsync(Token)).Name);
        Assert.Equal("Techorama", Assert.Single(Assert.Single(await Pane.ListAsync(Token)).Events).SeriesName);
    }

    // --- Deciding ------------------------------------------------------------------------

    [Fact]
    public async Task A_spotted_event_offers_every_later_state_and_skip_and_marked_going_offers_skip_but_no_way_back()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find(StateSelect));

        Assert.Equal(["Spotted", "Considering", "Going", "Attended", "Harvested", "Skip…"], StateChoices(pane));

        Choose(pane, "Going");

        pane.WaitForAssertion(() => Assert.Equal("Going", State(pane)));
        Assert.Equal(["Going", "Attended", "Harvested", "Skip…"], StateChoices(pane));
        Assert.Equal(KnowledgeEventState.Going, (await Pane.GetAsync(id, Token)).Value.State);
    }

    [Fact]
    public async Task A_pick_alone_moves_nothing_and_cancel_puts_the_choice_back()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find(StateSelect));

        pane.Find(StateSelect).Change("Attended");

        Assert.Equal("Move to Attended", Text(pane, "events-state-apply"));
        Assert.Equal(KnowledgeEventState.Spotted, (await Pane.GetAsync(id, Token)).Value.State);

        pane.Find("[data-testid='events-state-cancel']").Click();

        Assert.Equal("Spotted", State(pane));
        Assert.Empty(pane.FindAll("[data-testid='events-state-apply']"));
    }

    [Fact]
    public async Task Skip_asks_why_refuses_a_blank_reason_and_shows_the_reason_once_skipped()
    {
        var id = await Add("Techorama Belgium");
        Assert.True((await Pane.MoveToAsync(id, KnowledgeEventState.Considering, Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find(StateSelect));

        Choose(pane, EventPresentation.SkipChoice);
        pane.Find("[data-testid='events-skip-confirm']").Click();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='events-skip-dialog'] .app-error-message, [data-testid='events-skip-dialog'] .field__error")));
        Assert.Equal(KnowledgeEventState.Considering, (await Pane.GetAsync(id, Token)).Value.State);

        pane.Find("[data-testid='events-skip-reason'] textarea").Input("Clashes with the release");
        pane.Find("[data-testid='events-skip-confirm']").Click();

        pane.WaitForAssertion(() => Assert.Equal("Skipped", State(pane)));
        Assert.Empty(pane.FindAll("[data-testid='events-skip-dialog']"));
        Assert.Equal("Skipped: Clashes with the release", Text(pane, "events-skip-reason-shown"));
        Assert.Equal(["Skipped"], StateChoices(pane));
        Assert.True(pane.Find(StateSelect).HasAttribute("disabled"));
    }

    // --- Details -------------------------------------------------------------------------

    [Fact]
    public async Task The_place_is_an_address_with_a_map_and_no_learning_goal_is_asked()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-field-venue']"));

        Assert.Empty(pane.FindAll("[data-testid='events-field-learn']"));
        Assert.Empty(pane.FindAll("[data-testid='events-venue-map']"));
        Assert.Equal("street-address", pane.Find("[data-testid='events-field-venue'] input").GetAttribute("autocomplete"));

        pane.Find("[data-testid='events-field-venue'] input").Input("Kinepolis, Antwerp");

        Assert.Equal(
            "https://www.google.com/maps/search/?api=1&query=Kinepolis%2C%20Antwerp",
            pane.Find("[data-testid='events-venue-map']").GetAttribute("href"));
    }

    // --- Removing ------------------------------------------------------------------------

    [Fact]
    public async Task Remove_asks_to_confirm_and_takes_the_event_off_the_list()
    {
        var kept = await Add("Techorama Belgium");
        var duplicate = await Add("Techorama Belgium 2027");
        var pane = RenderOn(duplicate);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-remove']"));

        pane.Find("[data-testid='events-remove']").Click();

        pane.WaitForAssertion(() => Assert.Contains(
            "Remove Techorama Belgium 2027?",
            pane.Find("[data-testid='events-remove-dialog']").TextContent,
            StringComparison.Ordinal));

        pane.Find("[data-testid='events-remove-cancel']").Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='events-remove-dialog']")));
        Assert.True((await Pane.GetAsync(duplicate, Token)).IsSuccess);
        Assert.NotNull(pane.Find($"[data-testid='events-row-{duplicate:N}']"));

        pane.Find("[data-testid='events-remove']").Click();
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-remove-confirm']"));
        pane.Find("[data-testid='events-remove-confirm']").Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll($"[data-testid='events-row-{duplicate:N}']")));
        Assert.NotNull(pane.Find("[data-testid='events-pane-nothing-picked']"));
        Assert.Empty(pane.FindAll("[data-testid='events-remove-dialog']"));
        Assert.Equal("knowledge_event.not_found", (await Pane.GetAsync(duplicate, Token)).Error.Code);
        Assert.NotNull(pane.Find($"[data-testid='events-row-{kept:N}']"));
        var addEventId = pane.Find("[data-testid='events-add-open']").Id;
        pane.WaitForAssertion(() => Assert.Equal(addEventId, FocusCalls().LastOrDefault()));
    }

    [Fact]
    public async Task An_event_with_a_harvest_task_offers_no_remove()
    {
        var spotted = await Add("dotNET Friday");
        var going = await GoingWithItsHarvestTaskDue("Techorama Belgium");
        await new HarvestTaskPolicy(Store, Tasks).FileDueAsync(new DateOnly(2026, 11, 10), Token);
        var pane = RenderOn(going);
        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium", Text(pane, "events-editor-title")));

        Assert.Empty(pane.FindAll("[data-testid='events-remove']"));

        pane.Find($"[data-testid='events-row-{spotted:N}']").Click();

        pane.WaitForAssertion(() => Assert.Equal("dotNET Friday", Text(pane, "events-editor-title")));
        Assert.NotNull(pane.Find("[data-testid='events-remove']"));
    }

    [Fact]
    public async Task A_remove_refused_because_the_harvest_task_was_filed_meanwhile_says_why_and_keeps_the_event()
    {
        var going = await GoingWithItsHarvestTaskDue("Techorama Belgium");
        var pane = RenderOn(going);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-remove']"));

        // Filed after the editor read the event, so Remove is still on screen.
        await new HarvestTaskPolicy(Store, Tasks).FileDueAsync(new DateOnly(2026, 11, 10), Token);
        pane.Find("[data-testid='events-remove']").Click();
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-remove-confirm']"));
        pane.Find("[data-testid='events-remove-confirm']").Click();

        pane.WaitForAssertion(() => Assert.Equal(
            "The event holds its harvest task, so it is kept.",
            Text(pane, "events-remove-error")));
        Assert.Empty(pane.FindAll("[data-testid='events-remove-dialog']"));
        Assert.Empty(pane.FindAll("[data-testid='events-editor-save'].save-indicator--error"));
        Assert.Equal("Techorama Belgium", Text(pane, "events-editor-title"));
        Assert.NotNull(pane.Find($"[data-testid='events-row-{going:N}']"));
        Assert.True((await Pane.GetAsync(going, Token)).IsSuccess);

        // Read again, the event no longer offers Remove; the refusal still says why, and a
        // save that holds afterwards reads saved.
        Tasks.RaiseChanged();
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='events-remove']")));
        Assert.Equal("The event holds its harvest task, so it is kept.", Text(pane, "events-remove-error"));

        var title = pane.Find("[data-testid='events-field-title'] input");
        title.Input("Techorama Belgium 2026");
        title.Change("Techorama Belgium 2026");

        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium 2026", Text(pane, "events-editor-title")));
        Assert.Empty(pane.FindAll("[data-testid='events-editor-save'].save-indicator--error"));
    }

    [Fact]
    public async Task Text_still_waiting_is_written_before_a_remove()
    {
        var going = await GoingWithItsHarvestTaskDue("Techorama");
        var pane = RenderOn(going);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-remove']"));
        await new HarvestTaskPolicy(Store, Tasks).FileDueAsync(new DateOnly(2026, 11, 10), Token);

        // Typed and still waiting on its delay, which the pinned clock never ends.
        pane.Find("[data-testid='events-field-title'] input").Input("Techorama Belgium");
        Assert.Equal("Techorama", Saved(going).Title);

        pane.Find("[data-testid='events-remove']").Click();
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-remove-confirm']"));
        pane.Find("[data-testid='events-remove-confirm']").Click();

        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium", Saved(going).Title));
        pane.WaitForAssertion(() => Assert.Equal(
            "The event holds its harvest task, so it is kept.",
            Text(pane, "events-remove-error")));
    }

    [Fact]
    public async Task An_event_removed_elsewhere_meanwhile_counts_as_removed()
    {
        var kept = await Add("dotNET Friday");
        var duplicate = await Add("Techorama Belgium");
        var pane = RenderOn(duplicate);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-remove']"));

        // Another desktop removed it, and the replica caught up, after the editor read it.
        Assert.True((await Pane.RemoveAsync(duplicate, Token)).IsSuccess);
        pane.Find("[data-testid='events-remove']").Click();
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-remove-confirm']"));
        pane.Find("[data-testid='events-remove-confirm']").Click();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='events-pane-nothing-picked']")));
        Assert.Empty(pane.FindAll($"[data-testid='events-row-{duplicate:N}']"));
        Assert.Empty(pane.FindAll("[data-testid='events-remove-error']"));
        Assert.NotNull(pane.Find($"[data-testid='events-row-{kept:N}']"));
    }

    /// <summary>A Going event that ended before the pinned clock's day, so the Harvest Task
    /// Policy files its harvest task on the next run.</summary>
    private async Task<Guid> GoingWithItsHarvestTaskDue(string title)
    {
        var id = await Dated(title, new DateOnly(2026, 11, 7));
        Assert.True((await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token)).IsSuccess);
        return id;
    }

    [Fact]
    public async Task A_title_typed_in_the_editor_saves_when_the_field_is_left()
    {
        var id = await Add("Techorama");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-field-title'] input"));

        var input = pane.Find("[data-testid='events-field-title'] input");
        input.Input("Techorama Belgium");
        input.Change("Techorama Belgium");

        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium", Text(pane, "events-editor-title")));
        Assert.Equal("Techorama Belgium", (await Pane.GetAsync(id, Token)).Value.Title);
    }

    [Fact]
    public async Task A_start_date_typed_saves_with_date_source_typed_and_moves_the_row_line()
    {
        var id = await Add("dotNET Friday");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-field-start-date'] input"));

        pane.Find("[data-testid='events-field-start-date'] input").Change("2026-11-27");

        pane.WaitForAssertion(() => Assert.Equal(
            "27 Nov 2026",
            pane.Find($"[data-testid='events-row-{id:N}'] [data-testid='events-row-line']").TextContent));
        var saved = (await Pane.GetAsync(id, Token)).Value;
        Assert.Equal(new DateOnly(2026, 11, 27), saved.Start!.Date);
        Assert.Equal(DateSource.Typed, saved.DateSource);
    }

    // --- Harvest -------------------------------------------------------------------------

    [Fact]
    public async Task A_linked_note_that_is_there_opens_in_the_devbook_and_a_moved_one_reads_missing()
    {
        Brain.Notes["areas/learning/aspire.md"] = "Aspire takeaways";
        var id = await Add("Techorama Belgium");
        Assert.True((await Pane.LinkBrainNoteAsync(id, "areas/learning/aspire.md", cancellationToken: Token)).IsSuccess);
        Assert.True((await Pane.LinkBrainNoteAsync(id, "areas/learning/techorama.md", "Techorama", Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => Assert.Equal(2, pane.FindAll("[data-testid='events-note']").Count));

        var notes = pane.FindAll("[data-testid='events-note']");
        var missing = Assert.Single(notes, note => note.GetAttribute("data-missing") == "true");
        Assert.Contains(EventNoteLinks.Missing, missing.TextContent, StringComparison.Ordinal);
        Assert.Contains("Techorama", missing.TextContent, StringComparison.Ordinal);
        Assert.NotNull(missing.QuerySelector("[data-testid='events-note-relink']"));

        pane.Find("[data-testid='events-note'][data-missing='false'] .repo-link-chip__open").Click();

        var shown = Assert.Single(_navigator.Shown);
        Assert.Equal("devbook", shown.SurfaceId);
        Assert.Equal("brain", shown.Query["scope"]);
        Assert.Equal("areas/learning/aspire.md", shown.Query["chapter"]);
    }

    [Fact]
    public async Task A_missing_note_is_linked_again_to_the_note_it_moved_to()
    {
        Brain.Notes["areas/events/techorama.md"] = "Techorama notes";
        var id = await Add("Techorama Belgium");
        Assert.True((await Pane.LinkBrainNoteAsync(id, "areas/learning/techorama.md", "Techorama", Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-note-relink']"));

        pane.Find("[data-testid='events-note-relink']").Click();
        var picker = pane.Find("[data-testid='events-note-picker'] input");
        picker.Input("areas/events/techorama.md");
        picker.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        pane.WaitForAssertion(() => Assert.Equal("false", pane.Find("[data-testid='events-note']").GetAttribute("data-missing")));
        var note = Assert.Single((await Pane.GetAsync(id, Token)).Value.Notes);
        Assert.Equal("areas/events/techorama.md", note.Reference);
    }

    [Fact]
    public async Task With_no_brain_link_note_is_disabled_and_says_why()
    {
        Brain.UnavailableReason = "Set up your Brain in Settings › Storage";
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-note-unavailable']"));

        Assert.Equal("Set up your Brain in Settings › Storage", Text(pane, "events-note-unavailable"));
        var picker = pane.Find("[data-testid='events-note-picker'] input");
        Assert.True(picker.HasAttribute("disabled"));
        Assert.Equal(pane.Find("[data-testid='events-note-unavailable']").Id, picker.GetAttribute("aria-describedby"));
    }

    [Fact]
    public async Task The_verdict_is_offered_only_once_the_event_was_attended()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-harvest']"));

        Assert.Empty(pane.FindAll("[data-testid='events-field-verdict']"));

        Choose(pane, "Attended");

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='events-field-verdict']")));
    }

    // --- Tasks ---------------------------------------------------------------------------

    [Fact]
    public async Task Add_prep_task_files_the_title_alone_and_lists_it_with_its_status()
    {
        var id = await Add("Techorama Belgium");
        Assert.True((await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-prep-title'] input"));

        pane.Find("[data-testid='events-prep-title'] input").Input("Book hotel in Antwerp");
        pane.Find("[data-testid='events-prep-add']").Click();

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='events-task']")));
        Assert.Equal(["Book hotel in Antwerp"], Tasks.Filed);
        var task = pane.Find("[data-testid='events-task']");
        Assert.Contains("Book hotel in Antwerp", task.QuerySelector("[data-testid='events-task-chip']")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("Draft", task.QuerySelector("[data-testid='events-task-status']")!.TextContent.Trim());
        Assert.Equal("Prep", task.QuerySelector("[data-testid='events-task-purpose']")!.TextContent.Trim());

        task.QuerySelector(".repo-link-chip__open")!.Click();

        var shown = Assert.Single(_navigator.Shown);
        Assert.Equal("tasks", shown.SurfaceId);
        Assert.Equal(task.GetAttribute("data-task"), shown.Query["entry"]);
    }

    [Fact]
    public async Task A_linked_task_this_backlog_no_longer_holds_reads_not_in_this_backlog()
    {
        var taskId = Tasks.Hold("Ask about training budget", "Ready");
        var id = await Add("Techorama Belgium");
        Assert.True((await Pane.LinkExistingTaskAsync(id, taskId, Token)).IsSuccess);
        Tasks.Held.Remove(taskId);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-task']"));

        var chip = pane.Find("[data-testid='events-task-chip']");
        Assert.Equal("false", chip.GetAttribute("data-resolved"));
        Assert.Contains(TaskLinkChipText.NotInThisBacklog, chip.TextContent, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='events-task-status']"));
        Assert.Equal("Linked", Text(pane, "events-task-purpose"));
    }

    [Fact]
    public async Task Link_task_refuses_an_id_this_backlog_does_not_hold_in_words()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-link-task-id'] input"));

        pane.Find("[data-testid='events-link-task-id'] input").Input(Guid.NewGuid().ToString());
        pane.Find("[data-testid='events-link-task']").Click();

        pane.WaitForAssertion(() => Assert.Contains(
            "No task with that id in this backlog",
            pane.Find("[data-testid='events-link-task-id']").TextContent,
            StringComparison.Ordinal));
        Assert.Empty((await Pane.GetAsync(id, Token)).Value.Tasks);
    }

    [Fact]
    public async Task A_task_changing_underneath_reads_the_open_events_chips_again()
    {
        var taskId = Tasks.Hold("Book hotel", "Draft");
        var id = await Add("Techorama Belgium");
        Assert.True((await Pane.LinkExistingTaskAsync(id, taskId, Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => Assert.Equal("Draft", Text(pane, "events-task-status")));

        Tasks.Held[taskId] = new EventTaskFactsDto(taskId, "Book hotel", "Done");
        Tasks.RaiseChanged();

        pane.WaitForAssertion(() => Assert.Equal("Done", Text(pane, "events-task-status")));
    }

    // --- Round 2: what the review found ---------------------------------------------------

    [Fact]
    public async Task Picking_another_event_starts_its_sections_fresh()
    {
        var first = await Add("Techorama Belgium");
        Assert.True((await Pane.LinkBrainNoteAsync(first, "areas/learning/techorama.md", "Techorama", Token)).IsSuccess);
        var second = await Add("dotNET Friday");
        var pane = RenderOn(first);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-note-relink']"));

        pane.Find("[data-testid='events-note-relink']").Click();
        pane.Find("[data-testid='events-prep-title'] input").Input("Book hotel in Antwerp");
        Assert.Equal("Link Techorama again", pane.Find("[data-testid='events-note-picker'] label").TextContent);

        pane.Find($"[data-testid='events-row-{second:N}']").Click();

        pane.WaitForAssertion(() => Assert.Equal("dotNET Friday", Text(pane, "events-editor-title")));
        Assert.Equal("Link a Brain note", pane.Find("[data-testid='events-note-picker'] label").TextContent);
        Assert.Equal(string.Empty, pane.Find("[data-testid='events-prep-title'] input").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public async Task A_refusal_written_on_leaving_an_event_is_raised_as_a_toast()
    {
        var toasts = new ToastChannel();
        _context.Services.AddSingleton<IToastChannel>(toasts);
        var first = await Add("Techorama Belgium");
        var second = await Add("dotNET Friday");
        var pane = RenderOn(first);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-field-title'] input"));

        // Typed and still waiting on its delay when the event is left.
        pane.Find("[data-testid='events-field-title'] input").Input("   ");
        pane.Find($"[data-testid='events-row-{second:N}']").Click();

        pane.WaitForAssertion(() => Assert.Single(toasts.Visible));
        var toast = toasts.Visible[0];
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Equal("events-editor-left-refusal", toast.TestId);
        Assert.StartsWith("Techorama Belgium: not saved.", toast.Message, StringComparison.Ordinal);
        Assert.Equal("Techorama Belgium", (await Pane.GetAsync(first, Token)).Value.Title);
    }

    [Fact]
    public async Task Cancelling_the_skip_dialog_after_a_refusal_leaves_nothing_reading_unsaved()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find(StateSelect));

        Choose(pane, EventPresentation.SkipChoice);
        pane.Find("[data-testid='events-skip-confirm']").Click();
        pane.WaitForAssertion(() => Assert.Contains("save-indicator--error", pane.Find("[data-testid='events-editor-save']").ClassName, StringComparison.Ordinal));

        pane.Find("[data-testid='events-skip-cancel']").Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='events-skip-dialog']")));
        Assert.Empty(pane.FindAll("[data-testid='events-editor-save'].save-indicator--error"));
    }

    [Fact]
    public async Task Cancelling_a_refused_new_series_leaves_nothing_reading_unsaved()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-new-series-open']"));

        pane.Find("[data-testid='events-new-series-open']").Click();
        pane.Find("[data-testid='events-new-series-start']").Click();
        pane.WaitForAssertion(() => Assert.Contains("save-indicator--error", pane.Find("[data-testid='events-editor-save']").ClassName, StringComparison.Ordinal));

        pane.Find("[data-testid='events-new-series-cancel']").Click();

        Assert.Empty(pane.FindAll("[data-testid='events-editor-save'].save-indicator--error"));
    }

    [Fact]
    public async Task Emptying_the_amount_of_a_saved_cost_clears_the_cost()
    {
        var id = await Add("Techorama Belgium");
        Assert.True((await Pane.SetCostAsync(id, new CostDto(100m, "EUR"), Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-field-cost'] input"));

        pane.Find("[data-testid='events-field-cost'] input").Change(string.Empty);

        pane.WaitForAssertion(() => Assert.Null(Saved(id).Cost));
    }

    [Fact]
    public async Task An_amount_typed_before_its_currency_waits_for_it_without_reading_unsaved()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-field-cost'] input"));

        pane.Find("[data-testid='events-field-cost'] input").Change("749");

        Assert.Empty(pane.FindAll("[data-testid='events-editor-save'].save-indicator--error"));
        Assert.Null((await Pane.GetAsync(id, Token)).Value.Cost);

        pane.Find("[data-testid='events-field-currency'] input").Change("eur");

        pane.WaitForAssertion(() => Assert.Equal(new CostDto(749m, "EUR"), Saved(id).Cost));
    }

    [Fact]
    public async Task A_rename_while_a_prep_task_is_being_filed_waits_for_it_and_is_not_undone()
    {
        var id = await Add("Techorama");
        Assert.True((await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-prep-title'] input"));

        Tasks.FileGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pane.Find("[data-testid='events-prep-title'] input").Input("Book hotel in Antwerp");
        pane.Find("[data-testid='events-prep-add']").Click();

        var title = pane.Find("[data-testid='events-field-title'] input");
        title.Input("Techorama Belgium");
        title.Change("Techorama Belgium");

        Tasks.FileGate.SetResult();

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='events-task']")));
        pane.WaitForAssertion(() =>
        {
            var saved = Saved(id);
            Assert.Equal("Techorama Belgium", saved.Title);
            Assert.Single(saved.Tasks);
        });
    }

    [Fact]
    public void Opening_the_add_row_focuses_its_title_and_closing_it_returns_to_add_event()
    {
        var pane = RenderPane();
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-add-open']"));
        var addEventId = pane.Find("[data-testid='events-add-open']").Id;

        pane.Find("[data-testid='events-add-open']").Click();

        var titleId = pane.Find("[data-testid='events-add-title'] input").Id;
        pane.WaitForAssertion(() => Assert.Equal(titleId, FocusCalls().LastOrDefault()));

        pane.Find("[data-testid='events-add-cancel']").Click();

        pane.WaitForAssertion(() => Assert.Equal(addEventId, FocusCalls().LastOrDefault()));
        Assert.Equal(addEventId, pane.Find("[data-testid='events-add-open']").Id);
    }

    [Fact]
    public async Task A_new_key_date_row_has_a_remove_button_with_a_name_of_its_own()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-key-date-add']"));

        pane.Find("[data-testid='events-key-date-add']").Click();

        Assert.Equal("Remove this key date", pane.Find("[data-testid='events-key-date-remove']").GetAttribute("aria-label"));
    }

    /// <summary>The event as stored now. The stores in memory answer at once, so a wait can poll it.</summary>
    private KnowledgeEventDto Saved(Guid id) => Pane.GetAsync(id, Token).GetAwaiter().GetResult().Value;

    [Fact]
    public async Task The_series_page_is_a_link_wearing_the_panes_link_style_that_opens_outside()
    {
        var series = (await Pane.StartSeriesAsync("Techorama", "Techorama vzw", "https://techorama.be", Token)).Value;
        var id = await Add("Techorama Belgium 2027");
        Assert.True((await Pane.SetSeriesAsync(id, series.Id, Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-series-link']"));

        var link = pane.Find("[data-testid='events-series-link']");
        Assert.Equal("events-editor__link", link.ClassName);
        Assert.Equal("https://techorama.be", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    private List<string?> FocusCalls() =>
        [.. _context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "backlogFocus")
            .Select(invocation => invocation.Arguments[0] as string)];

    // --- Round 3: what the plan review found ----------------------------------------------

    [Fact]
    public async Task A_talk_note_opens_the_inbox_on_that_note()
    {
        var noteId = Guid.NewGuid().ToString("D");
        var id = await Add("Techorama Belgium");
        await Change(id, e => e.LinkNote(NoteLink.Create(NoteLinkKind.Talk, noteId, "Keynote: Aspire in production")));
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-note']"));

        var chip = pane.Find("[data-testid='events-note']");
        Assert.Contains("Keynote: Aspire in production", chip.TextContent, StringComparison.Ordinal);
        chip.QuerySelector(".repo-link-chip__open")!.Click();

        var shown = Assert.Single(_navigator.Shown);
        Assert.Equal("inbox", shown.SurfaceId);
        Assert.Equal(noteId, shown.Query["item"]);
    }

    /// <summary>With the Inbox switched off there is no surface to jump to, so the chip reads as
    /// plain text: its title, and nothing to click.</summary>
    [Fact]
    public async Task A_talk_note_offers_no_jump_while_the_inbox_is_off()
    {
        _features.SetEnabled(InboxPane, false);
        var id = await Add("Techorama Belgium");
        await Change(id, e => e.LinkNote(NoteLink.Create(NoteLinkKind.Talk, Guid.NewGuid().ToString("D"), "Keynote: Aspire in production")));
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-note']"));

        var chip = pane.Find("[data-testid='events-note']");
        Assert.Contains("Keynote: Aspire in production", chip.TextContent, StringComparison.Ordinal);
        Assert.Null(chip.QuerySelector(".repo-link-chip__open"));
        Assert.Empty(_navigator.Shown);
    }

    [Fact]
    public async Task The_dates_show_where_they_came_from_and_an_undated_event_shows_no_source()
    {
        var paged = await Add("dotNET Friday");
        var start = EventDate.Create(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), Cet).Value;
        await Change(paged, e => e.SetDates(start, null, DateSource.EventPage));
        var typed = await Dated("Techorama Belgium", new DateOnly(2027, 5, 18));
        var undated = await Add("NDC Oslo");
        var pane = RenderOn(paged);

        pane.WaitForAssertion(() => Assert.Equal("Source: Event page", Text(pane, "events-date-source")));

        pane.Find($"[data-testid='events-row-{typed:N}']").Click();
        pane.WaitForAssertion(() => Assert.Equal("Source: Typed", Text(pane, "events-date-source")));

        pane.Find($"[data-testid='events-row-{undated:N}']").Click();
        pane.WaitForAssertion(() => Assert.Equal("NDC Oslo", Text(pane, "events-editor-title")));
        Assert.Empty(pane.FindAll("[data-testid='events-date-source']"));
    }

    [Fact]
    public async Task A_date_typed_over_the_event_pages_reads_typed()
    {
        var id = await Add("dotNET Friday");
        var start = EventDate.Create(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), Cet).Value;
        await Change(id, e => e.SetDates(start, null, DateSource.EventPage));
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => Assert.Equal("Source: Event page", Text(pane, "events-date-source")));

        pane.Find("[data-testid='events-field-start-time'] input").Change("17:00");

        pane.WaitForAssertion(() => Assert.Equal("Source: Typed", Text(pane, "events-date-source")));
        Assert.Equal(DateSource.Typed, Saved(id).DateSource);
    }

    [Fact]
    public async Task Emptying_the_currency_of_a_saved_cost_keeps_the_cost_and_says_a_currency_is_needed()
    {
        var id = await Add("Techorama Belgium");
        Assert.True((await Pane.SetCostAsync(id, new CostDto(749m, "EUR"), Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-field-currency'] input"));

        pane.Find("[data-testid='events-field-currency'] input").Change(string.Empty);

        pane.WaitForAssertion(() => Assert.Contains(
            EventEditor.CurrencyNeeded,
            pane.Find("[data-testid='events-field-currency']").TextContent,
            StringComparison.Ordinal));
        Assert.Contains("save-indicator--error", pane.Find("[data-testid='events-editor-save']").ClassName, StringComparison.Ordinal);
        Assert.Equal(new CostDto(749m, "EUR"), Saved(id).Cost);

        pane.Find("[data-testid='events-field-currency'] input").Change("usd");

        pane.WaitForAssertion(() => Assert.Equal(new CostDto(749m, "USD"), Saved(id).Cost));
        pane.WaitForAssertion(() => Assert.DoesNotContain(
            EventEditor.CurrencyNeeded,
            pane.Find("[data-testid='events-field-currency']").TextContent,
            StringComparison.Ordinal));
        Assert.Empty(pane.FindAll("[data-testid='events-editor-save'].save-indicator--error"));
    }

    [Fact]
    public async Task A_second_jump_to_the_same_event_after_picking_another_opens_it_again()
    {
        var first = await Add("Techorama Belgium");
        var second = await Add("dotNET Friday");
        var pane = RenderOn(first);
        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium", Text(pane, "events-editor-title")));

        pane.Find($"[data-testid='events-row-{second:N}']").Click();
        pane.WaitForAssertion(() => Assert.Equal("dotNET Friday", Text(pane, "events-editor-title")));

        // The same query handed back by a re-render is not a jump: the pick stands.
        pane.Render(p => p.Add(c => c.Query, pane.Instance.Query));
        Assert.Equal("dotNET Friday", Text(pane, "events-editor-title"));

        pane.Render(p => p.Add(c => c.Query, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EventsSurfaceRegistration.EventKey] = first.ToString()
        }));

        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium", Text(pane, "events-editor-title")));
    }

    [Fact]
    public async Task The_skip_dialog_is_one_dialog()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find(StateSelect));

        Choose(pane, EventPresentation.SkipChoice);

        pane.WaitForAssertion(() => pane.Find("[data-testid='events-skip-dialog']"));
        var dialog = Assert.Single(pane.FindAll("dialog"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Empty(pane.FindAll("[role='dialog']"));
        Assert.Contains("Skip Techorama Belgium", dialog.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_note_picker_narrows_its_list_to_the_notes_matching_what_is_typed()
    {
        Brain.Notes["areas/learning/aspire.md"] = "Aspire takeaways";
        Brain.Notes["areas/learning/techorama.md"] = "Techorama notes";
        Brain.Notes["areas/events/ndc.md"] = "NDC Oslo";
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-note-picker'] input"));

        var picker = pane.Find("[data-testid='events-note-picker'] input");
        picker.Input("TECHO");

        pane.WaitForAssertion(() => Assert.Equal(
            ["areas/learning/techorama.md"],
            pane.FindAll("[data-testid='events-note-picker'] [role='option']").Select(option => option.GetAttribute("data-value"))));

        pane.Find("[data-testid='events-note-picker'] input").Input("areas/learning");

        pane.WaitForAssertion(() => Assert.Equal(2, pane.FindAll("[data-testid='events-note-picker'] [role='option']").Count));
    }

    /// <summary>Changes the stored event on the aggregate, as a capture or an applied attachment
    /// request would, where the pane offers no write of its own for it.</summary>
    private async Task Change(Guid id, Func<KnowledgeEvent, Result> change)
    {
        var stored = await Store.GetAsync(id, Token);
        Assert.NotNull(stored);
        Assert.True(change(stored).IsSuccess);
        await Store.SaveAsync(stored, Token);
    }

    // --- The surface ---------------------------------------------------------------------

    [Fact]
    public void Add_events_surface_registers_one_flagged_takeover_with_the_event_key_and_a_second_call_adds_nothing()
    {
        var services = new ServiceCollection();
        services.AddEventsSurface();
        services.AddEventsSurface();

        var surface = Assert.Single(services.Select(descriptor => descriptor.ImplementationInstance).OfType<ShellSurface>());
        Assert.Equal("events", surface.Id);
        Assert.Equal("Events", surface.Title);
        Assert.Equal(ShellSurfaceGroup.Takeover, surface.Group);
        Assert.Equal(25, surface.Order);
        Assert.Equal(EventsFeatures.Events, surface.FeatureKey);
        Assert.Equal(["event"], surface.QueryKeys);
        Assert.Equal(typeof(EventsSurface), surface.Component);
    }

    // --- Saves made elsewhere: another window, the MCP server -----------------------------

    [Fact]
    public async Task An_event_added_elsewhere_shows_in_the_open_list_without_a_reload()
    {
        await Add("Techorama Belgium");
        var pane = RenderPane();
        pane.WaitForAssertion(() => Assert.Single(pane.FindAll(".events-list__title")));

        var added = await Elsewhere().AddAsync(new NewKnowledgeEventDto("NDC Oslo", Start: new EventDateDto(new DateOnly(2027, 6, 7))), Token);

        pane.WaitForAssertion(() => Assert.Equal(
            "NDC Oslo",
            pane.Find($"[data-testid='events-row-{added.Value.Id:N}'] .events-list__title").TextContent));
        Assert.Equal("2 events", Text(pane, "events-pane-subtitle"));
    }

    [Fact]
    public async Task A_change_elsewhere_to_the_open_event_shows_and_keeps_what_the_person_is_typing()
    {
        var id = await Add("Techorama");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => Assert.Equal("Spotted", Text(pane, "events-editor-state")));

        var title = pane.Find("[data-testid='events-field-title'] input");
        title.Input("Techorama Belgium 2027");

        Assert.True((await Elsewhere().MoveToAsync(id, KnowledgeEventState.Considering, Token)).IsSuccess);

        pane.WaitForAssertion(() => Assert.Equal("Considering", Text(pane, "events-editor-state")));
        Assert.Equal("Techorama Belgium 2027", pane.Find("[data-testid='events-field-title'] input").GetAttribute("value"));

        // The typed title is written over the event as it now stands, so the move made
        // elsewhere is kept: the latest write wins field by field.
        pane.Find("[data-testid='events-field-title'] input").Change("Techorama Belgium 2027");
        pane.WaitForAssertion(() => Assert.Equal(
            ("Techorama Belgium 2027", KnowledgeEventState.Considering),
            (Saved(id).Title, Saved(id).State)));
    }

    [Fact]
    public async Task A_change_elsewhere_while_the_editor_has_a_write_out_reloads_the_list_and_is_kept_once_the_write_lands()
    {
        var id = await Add("Techorama");
        Assert.True((await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-prep-title'] input"));

        Tasks.FileGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pane.Find("[data-testid='events-prep-title'] input").Input("Book hotel in Antwerp");
        pane.Find("[data-testid='events-prep-add']").Click();

        Assert.True((await Elsewhere().RenameAsync(id, "Techorama Belgium", Token)).IsSuccess);

        pane.WaitForAssertion(() => Assert.Equal(
            "Techorama Belgium",
            pane.Find($"[data-testid='events-row-{id:N}'] .events-list__title").TextContent));
        Assert.Equal("Techorama", Text(pane, "events-editor-title"));

        Tasks.FileGate.SetResult();
        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='events-task']")));
        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium", Text(pane, "events-editor-title")));
        Assert.Equal("Techorama Belgium", Saved(id).Title);
    }

    /// <summary>A change elsewhere that lands after the editor's own write saved, but before its
    /// answer is back, is not in that answer. The open event is read again once the answer is
    /// in, so the editor does not stay on the older event.</summary>
    [Fact]
    public async Task A_change_elsewhere_landing_while_the_editors_answer_is_out_is_read_once_the_answer_is_in()
    {
        var id = await Add("Techorama");
        Assert.True((await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-prep-title'] input"));

        // The prep task is filed and the event saved; the answer waits on its task chips.
        Tasks.LookupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pane.Find("[data-testid='events-prep-title'] input").Input("Book hotel in Antwerp");
        pane.Find("[data-testid='events-prep-add']").Click();
        pane.WaitForAssertion(() => Assert.Single(Store.GetAsync(id, Token).GetAwaiter().GetResult()!.Tasks));

        Assert.True((await Elsewhere().RenameAsync(id, "Techorama Belgium", Token)).IsSuccess);
        pane.WaitForAssertion(() => Assert.Equal(
            "Techorama Belgium",
            pane.Find($"[data-testid='events-row-{id:N}'] .events-list__title").TextContent));

        var gate = Tasks.LookupGate;
        Tasks.LookupGate = null;
        gate.SetResult();

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='events-task']")));
        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium", Text(pane, "events-editor-title")));
    }

    /// <summary>A change elsewhere skipped while the editor's write was out is still read when
    /// that write is refused: no answer comes back to read it after, so the queue running dry
    /// does.</summary>
    [Fact]
    public async Task A_change_elsewhere_skipped_during_a_write_that_is_refused_is_read_once_the_queue_is_empty()
    {
        var id = await Add("Techorama");
        Assert.True((await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-prep-title'] input"));

        Tasks.FileGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Tasks.RefuseWith = Backlog.SharedKernel.Results.Error.Validation("entry.needs_title", "Refused.");
        pane.Find("[data-testid='events-prep-title'] input").Input("Book hotel in Antwerp");
        pane.Find("[data-testid='events-prep-add']").Click();

        Assert.True((await Elsewhere().RenameAsync(id, "Techorama Belgium", Token)).IsSuccess);
        pane.WaitForAssertion(() => Assert.Equal(
            "Techorama Belgium",
            pane.Find($"[data-testid='events-row-{id:N}'] .events-list__title").TextContent));
        Assert.Equal("Techorama", Text(pane, "events-editor-title"));

        Tasks.FileGate.SetResult();

        pane.WaitForAssertion(() => Assert.Equal("Techorama Belgium", Text(pane, "events-editor-title")));
        Assert.Empty(Saved(id).Tasks);
    }

    /// <summary>Stands in for the Inbox's screen in the catalog; never rendered here.</summary>
    private sealed class InboxStandIn : Microsoft.AspNetCore.Components.ComponentBase;

    /// <summary>The window's navigator, recording each jump.</summary>
    private sealed class RecordingNavigator : ISurfaceNavigator
    {
        public List<SurfaceRequest> Shown { get; } = [];

        public event Action<SurfaceRequest>? ShowRequested;

        public void Show(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
        {
            var request = new SurfaceRequest(surfaceId, query ?? new Dictionary<string, string>(StringComparer.Ordinal));
            Shown.Add(request);
            ShowRequested?.Invoke(request);
        }
    }

    /// <summary>The words an unresolved task chip says, as the library writes them.</summary>
    private static class TaskLinkChipText
    {
        public const string NotInThisBacklog = Backlog.UI.Components.Devbook.TaskLinkChip.NotInThisBacklog;
    }
}
