using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.UI;
using Backlog.SharedKernel;
using Backlog.UI.Components.Feedback;

using Bunit;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The Events pane's screens over the real pane service, its in-memory stores and the task and
/// Brain targets at the boundary: the list grouped by state, adding by title alone, the state
/// choice and its skip dialog, the Harvest chips, the task chips and Add prep task, and the jumps
/// out through the window's navigator.
/// </summary>
public sealed class EventsScreenTests : EventsPaneFixture, IDisposable
{
    private readonly BunitContext _context = new();
    private readonly RecordingNavigator _navigator = new();

    public EventsScreenTests()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, Cet));
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("CET-screen", Cet, "CET", "CET"));

        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton(Pane);
        _context.Services.AddSingleton<ISurfaceNavigator>(_navigator);
        _context.Services.AddSingleton<TimeProvider>(clock);
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<EventsSurface> RenderPane(IReadOnlyDictionary<string, string>? query = null) =>
        _context.Render<EventsSurface>(p => p.Add(c => c.Query, query));

    private IRenderedComponent<EventsSurface> RenderOn(Guid id) =>
        RenderPane(new Dictionary<string, string>(StringComparer.Ordinal) { [EventsSurfaceRegistration.EventKey] = id.ToString() });

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
        Assert.Equal("Spotted", Text(pane, "events-editor-state"));
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
    public async Task A_spotted_event_offers_every_later_state_and_skip_and_marked_going_offers_neither_back_nor_skip()
    {
        var id = await Add("Techorama Belgium");
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-moves']"));

        Assert.Equal(
            ["Considering", "Going", "Attended", "Harvested"],
            pane.FindAll("[data-testid='events-moves'] button").Select(button => button.TextContent.Trim()));
        Assert.NotNull(pane.Find("[data-testid='events-skip']"));

        pane.Find("[data-testid='events-move-going']").Click();

        pane.WaitForAssertion(() => Assert.Equal("Going", Text(pane, "events-editor-state")));
        Assert.Equal(
            ["Attended", "Harvested"],
            pane.FindAll("[data-testid='events-moves'] button").Select(button => button.TextContent.Trim()));
        Assert.Empty(pane.FindAll("[data-testid='events-skip']"));
        Assert.Equal(KnowledgeEventState.Going, (await Pane.GetAsync(id, Token)).Value.State);
    }

    [Fact]
    public async Task Skip_asks_why_refuses_a_blank_reason_and_shows_the_reason_once_skipped()
    {
        var id = await Add("Techorama Belgium");
        Assert.True((await Pane.MoveToAsync(id, KnowledgeEventState.Considering, Token)).IsSuccess);
        var pane = RenderOn(id);
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-skip']"));

        pane.Find("[data-testid='events-skip']").Click();
        pane.Find("[data-testid='events-skip-confirm']").Click();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='events-skip-dialog'] .app-error-message, [data-testid='events-skip-dialog'] .field__error")));
        Assert.Equal(KnowledgeEventState.Considering, (await Pane.GetAsync(id, Token)).Value.State);

        pane.Find("[data-testid='events-skip-reason'] textarea").Input("Clashes with the release");
        pane.Find("[data-testid='events-skip-confirm']").Click();

        pane.WaitForAssertion(() => Assert.Equal("Skipped", Text(pane, "events-editor-state")));
        Assert.Empty(pane.FindAll("[data-testid='events-skip-dialog']"));
        Assert.Equal("Skipped: Clashes with the release", Text(pane, "events-skip-reason-shown"));
        Assert.Empty(pane.FindAll("[data-testid='events-moves']"));
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

        pane.Find("[data-testid='events-move-attended']").Click();

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
        pane.WaitForAssertion(() => pane.Find("[data-testid='events-skip']"));

        pane.Find("[data-testid='events-skip']").Click();
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
