using Backlog.Modules.Tasks.Abstractions.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook pages and chapters an entry points at, on the open entry and on its
/// row: one chip per reference with what the devbook says about it now, a way to
/// drop one and add another, and a way to open one in the Devbook pane.
/// <para>
/// The references are the entry's own field rather than tokens in its text, so
/// every write here goes through <see cref="ITaskItems.SetDevbookReferencesAsync"/>
/// and is asserted against the store, not the markdown. What a reference resolves
/// to comes from <see cref="FakeDevbookReferenceResolver"/>; the adapter that reads a
/// real folder is tested where it lives.
/// </para>
/// <para>
/// bUnit swallows an exception thrown by an event handler, and the chips resolve
/// off the render thread, so every outcome is asserted with
/// <c>WaitForAssertion</c> rather than straight after the gesture.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class EntryDevbookReferencesTests
{
    private const string TaskChapter = ".devbook/domain/tasks/domain.md#task";
    private const string TasksPage = ".devbook/domain/tasks/domain.md";
    private const string RenamedChapter = ".devbook/domain/tasks/domain.md#renamed";
    private const string Entry = "# Link the chapter\n`task` `repo:backlog`\n\nPoint at the spec.\n";

    private static readonly string[] Repositories = ["backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs"];

    [Fact]
    public async Task Each_reference_is_a_chip_with_its_title_its_status_and_a_mark_when_broken()
    {
        var devbook = new FakeDevbookReferenceResolver()
            .Chapter(TaskChapter, "Task", "active")
            .Page(TasksPage, "Tasks domain")
            .Broken(RenamedChapter, DevbookReferenceState.UnknownHeading);
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, [TaskChapter, TasksPage, RenamedChapter]);

        var pane = host.Render();

        pane.WaitForAssertion(() =>
        {
            var chips = pane.FindAll("[data-testid='entry-devbook-reference']");
            Assert.Equal(["Task", "Tasks domain", "renamed"], chips.Select(chip => chip.QuerySelector(".devbook-chip__label")!.TextContent.Trim()));

            Assert.Equal(TaskChapter, chips[0].QuerySelector(".devbook-chip__label")!.GetAttribute("title"));
            Assert.Equal("active", chips[0].QuerySelector(".badge--status")!.TextContent.Trim());

            // A page has no status, and a page is not broken.
            Assert.Null(chips[1].QuerySelector(".badge--status"));
            Assert.Null(chips[1].QuerySelector(".devbook-chip__broken"));

            var mark = chips[2].QuerySelector(".devbook-chip__broken");
            Assert.NotNull(mark);
            Assert.Contains("heading", mark!.GetAttribute("aria-label"), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(DevbookReferenceState.UnknownPage, "page")]
    [InlineData(DevbookReferenceState.OutsideDevbook, "devbook folder")]
    public async Task Every_broken_state_says_what_is_wrong(DevbookReferenceState state, string words)
    {
        const string reference = "docs/handbook.md";
        var devbook = new FakeDevbookReferenceResolver().Broken(reference, state);
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, [reference]);

        var pane = host.Render();

        pane.WaitForAssertion(() =>
            Assert.Contains(words, pane.Find("[data-testid='entry-devbook-reference'] .devbook-chip__broken").GetAttribute("aria-label"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task References_are_read_against_the_repository_the_entry_is_filed_under()
    {
        var devbook = new FakeDevbookReferenceResolver().Chapter(TaskChapter, "Task");
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync("# Write the changelog\n`task` `repo:docs`\n\nSay what shipped.\n");
        await host.State.SetDevbookReferencesAsync(row, [TaskChapter]);

        var pane = host.Render();

        pane.WaitForAssertion(() => Assert.Equal("Task", pane.Find("[data-testid='entry-devbook-reference'] .devbook-chip__label").TextContent.Trim()));
        Assert.Contains("docs", devbook.ResolvedFor);
    }

    [Fact]
    public async Task An_entry_filed_nowhere_reads_its_references_against_the_scoped_repository()
    {
        var devbook = new FakeDevbookReferenceResolver().Chapter(TaskChapter, "Task");
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync("# Unfiled\n`task`\n\nNo repository.\n");
        host.State.SetRepositoryFilter("docs");

        Assert.Null(host.State.RepositoryFor(row));
        Assert.Equal("docs", host.State.DevbookRepositoryAliasFor(row));
    }

    [Fact]
    public async Task Removing_a_chip_writes_the_list_without_it()
    {
        var devbook = new FakeDevbookReferenceResolver().Chapter(TaskChapter, "Task").Page(TasksPage, "Tasks domain");
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, [TaskChapter, TasksPage]);

        var pane = host.Render();
        pane.WaitForAssertion(() => Assert.Equal(2, pane.FindAll("[data-testid='entry-devbook-reference']").Count));

        await pane.Find($"[data-reference='{TaskChapter}'] .devbook-chip__remove").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([TasksPage], row.DevbookReferences);
            Assert.Equal([TasksPage], pane.FindAll("[data-testid='entry-devbook-reference']").Select(chip => chip.GetAttribute("data-reference")));
        });

        var stored = (await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken)).Single(entry => entry.Id == row.Id);
        Assert.Equal([TasksPage], stored.DevbookReferences);
    }

    [Fact]
    public async Task The_picker_offers_the_repository_s_pages_and_chapters_by_folder_and_adds_one()
    {
        var devbook = new FakeDevbookReferenceResolver().Chapter(TaskChapter, "Task", "active");
        devbook.Targets["backlog"] =
        [
            new DevbookReferenceTarget(TasksPage, "Tasks domain", "domain", 0, null),
            new DevbookReferenceTarget(TaskChapter, "Task", "domain", 2, "active"),
            new DevbookReferenceTarget(".devbook/arc42/09-decisions.md", "Decisions", "arc42", 0, null)
        ];
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();

        await pane.Find("[data-testid='entry-action-devbook-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-devbook-select'] input").FocusAsync(new());

        pane.WaitForAssertion(() =>
        {
            var groups = pane.FindAll("[data-testid='entry-devbook-select'] .tag-select__group-label").Select(label => label.TextContent.Trim());
            Assert.Equal(["domain", "arc42"], groups);

            var options = pane.FindAll("[data-testid='entry-devbook-select'] [role='option']");
            Assert.Equal(["Tasks domain", "Task", "Decisions"], options.Select(option => option.QuerySelector(".tag-select__option-label")!.TextContent));

            // A chapter says which page it is a heading of; a page says it is one.
            Assert.Contains("Tasks domain", options[1].QuerySelector(".tag-select__hint")!.TextContent, StringComparison.Ordinal);
        });

        await pane.FindAll("[data-testid='entry-devbook-select'] [role='option']")[1].ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([TaskChapter], row.DevbookReferences);
            Assert.Equal("Task", pane.Find("[data-testid='entry-devbook-reference'] .devbook-chip__label").TextContent.Trim());
        });

        // The picker draws no chips of its own: the chips above are the selection.
        Assert.Empty(pane.FindAll("[data-testid='entry-devbook-select'] .tag-select__chip"));
    }

    [Fact]
    public async Task A_typed_path_and_anchor_is_accepted()
    {
        var devbook = new FakeDevbookReferenceResolver();
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();

        await pane.Find("[data-testid='entry-action-devbook-set']").ClickAsync(new());
        var input = pane.Find("[data-testid='entry-devbook-select'] input");
        await input.FocusAsync(new());
        await input.InputAsync(new ChangeEventArgs { Value = ".devbook/arc42/09-decisions.md#storage" });
        await pane.Find("[data-testid='entry-devbook-select'] input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        pane.WaitForAssertion(() => Assert.Equal([".devbook/arc42/09-decisions.md#storage"], row.DevbookReferences));
    }

    [Fact]
    public async Task A_refused_reference_is_said_on_the_entry_and_on_a_toast_and_nothing_is_written()
    {
        var devbook = new FakeDevbookReferenceResolver().Chapter(TaskChapter, "Task");
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, [TaskChapter]);

        var pane = host.Render();

        await pane.Find("[data-testid='entry-action-devbook-set']").ClickAsync(new());
        var input = pane.Find("[data-testid='entry-devbook-select'] input");
        await input.FocusAsync(new());
        await input.InputAsync(new ChangeEventArgs { Value = "not a reference" });
        await pane.Find("[data-testid='entry-devbook-select'] input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        pane.WaitForAssertion(() =>
        {
            Assert.NotEmpty(pane.Find("[data-testid='entry-devbook-error']").TextContent.Trim());
            Assert.Equal(AppSaveState.Error, host.State.SaveState);
        });

        Assert.Contains(host.Toasts.Visible, toast => toast.TestId == "entry-devbook-refused");
        Assert.Equal([TaskChapter], row.DevbookReferences);

        var stored = (await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken)).Single(entry => entry.Id == row.Id);
        Assert.Equal([TaskChapter], stored.DevbookReferences);
    }

    [Fact]
    public async Task The_next_accepted_write_clears_the_refusal()
    {
        var devbook = new FakeDevbookReferenceResolver().Chapter(TaskChapter, "Task");
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);

        await host.State.SetDevbookReferencesAsync(row, ["not a reference"]);
        Assert.NotNull(row.DevbookReferenceError);

        await host.State.AddDevbookReferenceAsync(row, TaskChapter);

        Assert.Null(row.DevbookReferenceError);
        Assert.Equal([TaskChapter], row.DevbookReferences);
    }

    [Fact]
    public async Task Pressing_a_chip_asks_the_shell_to_open_it_in_the_entry_s_repository()
    {
        var devbook = new FakeDevbookReferenceResolver().Chapter(TaskChapter, "Task");
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync("# Write the changelog\n`task` `repo:docs`\n\nSay what shipped.\n");
        await host.State.SetDevbookReferencesAsync(row, [TaskChapter]);

        DevbookReferenceRequest? asked = null;
        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.OnOpenDevbookReference, (DevbookReferenceRequest request) => asked = request));

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='entry-devbook-reference'] button.devbook-chip__label")));
        await pane.Find("[data-testid='entry-devbook-reference'] button.devbook-chip__label").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Equal(new DevbookReferenceRequest("docs", TaskChapter), asked));
    }

    // --- the acceptance checklist ----------------------------------------------

    private const string ScenarioPage = ".devbook/domain/work/set-up-and-fill-the-backlog.md";
    private const string Requirement = ".devbook/domain/work/requirements.md#requirement-columns-follow-the-status-order";

    private static ScenarioPartEvidence Part(string anchor, string title, ScenarioPartState state, bool ran = true) =>
        new($"{ScenarioPage}#{anchor}", ScenarioPage, "Set up and fill the backlog", "set-up-and-fill-the-backlog", anchor, title, state,
            ran ? new DateTimeOffset(2026, 10, 7, 8, 30, 0, TimeSpan.Zero) : null);

    [Fact]
    public async Task Scenario_parts_are_an_acceptance_checklist_with_how_many_pass()
    {
        var devbook = new FakeDevbookReferenceResolver()
            .Chapter(TaskChapter, "Task")
            .Scenario($"{ScenarioPage}#statuses-are-set-up", "Statuses are set up", Part("statuses-are-set-up", "Statuses are set up", ScenarioPartState.Passed))
            .Scenario(Requirement, "Requirement: Columns follow the status order",
                Part("statuses-are-set-up", "Statuses are set up", ScenarioPartState.Passed),
                Part("an-item-is-moved", "An item is moved", ScenarioPartState.Stale),
                Part("a-new-item-lands-in-backlog", "A new item lands in Backlog", ScenarioPartState.NeverRun, ran: false));
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, [TaskChapter, $"{ScenarioPage}#statuses-are-set-up", Requirement]);

        var pane = host.Render();

        pane.WaitForAssertion(() =>
        {
            var list = pane.Find("[data-testid='entry-acceptance']");
            Assert.Equal("Acceptance", list.GetAttribute("aria-label"));

            // A part named by its page and by the requirement is one row.
            Assert.Equal("1 of 3 passing", pane.Find("[data-testid='entry-acceptance-count']").TextContent.Trim());
            Assert.Empty(pane.FindAll("[data-testid='entry-acceptance-proved']"));

            var rows = pane.FindAll("[data-testid='entry-acceptance-part']");
            Assert.Equal(["passed", "stale", "never-run"], rows.Select(part => part.GetAttribute("data-state")));
            Assert.Equal("Statuses are set up", rows[0].QuerySelector(".scenario-checklist__part")!.TextContent.Trim());
            Assert.Equal("Set up and fill the backlog", rows[0].QuerySelector(".scenario-checklist__page")!.TextContent.Trim());
            Assert.StartsWith("Ran ", rows[0].QuerySelector(".scenario-checklist__run")!.TextContent.Trim(), StringComparison.Ordinal);
            Assert.Equal("Never run", rows[2].QuerySelector(".scenario-checklist__run")!.TextContent.Trim());
            Assert.Equal("Scenario stale", rows[1].QuerySelector(".devbook-scenario-dot")!.GetAttribute("aria-label"));
        });
    }

    /// <summary>Proved is a signal: every part passed, the entry says so, and its
    /// status stays where it was.</summary>
    [Fact]
    public async Task Every_part_passing_says_proved_and_leaves_the_status_alone()
    {
        var devbook = new FakeDevbookReferenceResolver()
            .Scenario(ScenarioPage, "Set up and fill the backlog",
                Part("statuses-are-set-up", "Statuses are set up", ScenarioPartState.Passed),
                Part("an-item-is-moved", "An item is moved", ScenarioPartState.Passed));
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, [ScenarioPage]);
        var before = row.Status;

        var pane = host.Render();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("2 of 2 passing", pane.Find("[data-testid='entry-acceptance-count']").TextContent.Trim());
            Assert.Equal("Proved", pane.Find("[data-testid='entry-acceptance-proved']").TextContent.Trim());
        });
        Assert.Equal(before, row.Status);
    }

    [Fact]
    public async Task References_that_stand_for_no_scenario_part_draw_no_checklist()
    {
        var devbook = new FakeDevbookReferenceResolver().Chapter(TaskChapter, "Task");
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, [TaskChapter]);

        var pane = host.Render();

        pane.WaitForAssertion(() => Assert.Equal("Task", pane.Find("[data-testid='entry-devbook-reference'] .devbook-chip__label").TextContent.Trim()));
        Assert.Empty(pane.FindAll("[data-testid='entry-acceptance']"));
    }

    [Fact]
    public async Task Pressing_a_part_opens_it_in_the_devbook()
    {
        var devbook = new FakeDevbookReferenceResolver()
            .Scenario(ScenarioPage, "Set up and fill the backlog", Part("an-item-is-moved", "An item is moved", ScenarioPartState.Failed));
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, [ScenarioPage]);

        DevbookReferenceRequest? asked = null;
        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.OnOpenDevbookReference, (DevbookReferenceRequest request) => asked = request));

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='entry-acceptance-part'] button.scenario-checklist__part")));
        await pane.Find("[data-testid='entry-acceptance-part'] button.scenario-checklist__part").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Equal(new DevbookReferenceRequest("backlog", $"{ScenarioPage}#an-item-is-moved"), asked));
    }

    [Fact]
    public async Task A_reference_to_no_page_is_not_offered_as_something_to_open()
    {
        var devbook = new FakeDevbookReferenceResolver().Broken("docs/handbook.md", DevbookReferenceState.OutsideDevbook);
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, ["docs/handbook.md"]);

        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.OnOpenDevbookReference, (DevbookReferenceRequest _) => { }));

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='entry-devbook-reference'] .devbook-chip__broken")));
        Assert.Empty(pane.FindAll("[data-testid='entry-devbook-reference'] button.devbook-chip__label"));
    }

    [Fact]
    public async Task A_row_with_references_shows_a_book_and_how_many()
    {
        var devbook = new FakeDevbookReferenceResolver();
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var linked = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(linked, [TaskChapter, TasksPage]);
        await host.WriteEntryAsync("# Nothing linked\n`task`\n\nPlain.\n");

        var pane = host.Render();

        var indicators = pane.FindAll("[data-testid='entry-list'] [data-testid='entry-devbook-indicator']");
        var indicator = Assert.Single(indicators);
        Assert.Contains("2", indicator.TextContent, StringComparison.Ordinal);
        Assert.Equal("2 Devbook references", indicator.GetAttribute("aria-label"));
    }

    // --- The picker while the devbook is still being read ---------------------

    private static DevbookReferenceTarget[] TasksTargets =>
    [
        new(TasksPage, "Tasks domain", "domain", 0, null),
        new(TaskChapter, "Task", "domain", 2, "active"),
        new(".devbook/domain/tasks/features.md", "Tasks", "domain", 0, null)
    ];

    private static async Task OpenPickerAsync(IRenderedComponent<TasksPane> pane)
    {
        await pane.Find("[data-testid='entry-action-devbook-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-devbook-select'] input").FocusAsync(new());
    }

    private static IEnumerable<string> OfferedTitles(IRenderedComponent<TasksPane> pane) =>
        pane.FindAll("[data-testid='entry-devbook-select'] [role='option'] .tag-select__option-label").Select(label => label.TextContent);

    /// <summary>
    /// A devbook with nothing to list yet — its database still being built when the
    /// picker first asked — is asked again while the picker stays open, and what it
    /// lists then is offered without the reader having to close and reopen it.
    /// <para>
    /// This is the reported new-task picker that offered only <c>Add "task"</c>: the
    /// empty first answer was held for the repository and nothing ever asked again.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Targets_the_devbook_lists_after_an_empty_first_answer_reach_the_open_picker()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
        var devbook = new FakeDevbookReferenceResolver();
        using var host = await TasksPaneHost.CreateAsync(roadmapTags: null, Repositories, clock: clock, devbookReferences: devbook);
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        await OpenPickerAsync(pane);

        pane.WaitForAssertion(() => Assert.Contains("backlog", devbook.ListedFor));
        Assert.Empty(OfferedTitles(pane));

        devbook.Targets["backlog"] = TasksTargets;
        await pane.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(5)));

        pane.WaitForAssertion(() => Assert.Equal(["Tasks domain", "Task", "Tasks"], OfferedTitles(pane)));

        // Still the same open picker, still with the focus in it.
        Assert.Equal("true", pane.Find("[data-testid='entry-devbook-select'] input").GetAttribute("aria-expanded"));
    }

    /// <summary>An entry with no repository yet has no devbook to list; once its
    /// repository is set the open picker offers that repository's pages.</summary>
    [Fact]
    public async Task The_open_picker_follows_the_entry_s_repository_once_it_is_set()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] = TasksTargets;
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync("# Unfiled\n`task`\n\nNo repository yet.\n");

        var pane = host.Render();
        await OpenPickerAsync(pane);

        pane.WaitForAssertion(() => Assert.Contains(null, devbook.ListedFor));
        Assert.Empty(OfferedTitles(pane));

        await pane.InvokeAsync(() => host.State.ChangeRepositoryAsync(host.State.SelectedRow ?? row, "backlog"));

        pane.WaitForAssertion(() => Assert.Equal(["Tasks domain", "Task", "Tasks"], OfferedTitles(pane)));
        Assert.Equal("true", pane.Find("[data-testid='entry-devbook-select'] input").GetAttribute("aria-expanded"));
    }

    /// <summary>A reload of the list builds a new row for every entry, the open one
    /// included. The picker, its query and its list are the reader's and stay put.</summary>
    [Fact]
    public async Task The_open_picker_keeps_its_query_across_a_reload_of_the_entry()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] = TasksTargets;
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        await OpenPickerAsync(pane);
        await pane.Find("[data-testid='entry-devbook-select'] input").InputAsync(new ChangeEventArgs { Value = "Tasks d" });
        pane.WaitForAssertion(() => Assert.Equal(["Tasks domain"], OfferedTitles(pane)));

        await host.WriteFromElsewhereAsync("# Another\n`task`\n\nFrom the other machine.\n");
        await pane.InvokeAsync(host.State.ReloadFromStoreAsync);

        pane.WaitForAssertion(() =>
        {
            var input = pane.Find("[data-testid='entry-devbook-select'] input");
            Assert.Equal("Tasks d", input.GetAttribute("value"));
            Assert.Equal("true", input.GetAttribute("aria-expanded"));
            Assert.Equal(["Tasks domain"], OfferedTitles(pane));
        });
    }

    /// <summary>The placeholder invites a path, so a path finds the page: the query
    /// is matched against the stored reference as well as the title.</summary>
    [Fact]
    public async Task The_picker_finds_a_page_by_its_path()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] = TasksTargets;
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        await OpenPickerAsync(pane);
        await pane.Find("[data-testid='entry-devbook-select'] input").InputAsync(new ChangeEventArgs { Value = "features.md" });

        pane.WaitForAssertion(() => Assert.Equal(["Tasks"], OfferedTitles(pane)));
    }

    /// <summary>A real devbook offers well over a thousand pages and chapters. An
    /// empty query draws a page of them and says how many more there are, rather
    /// than drawing all of them on every keystroke.</summary>
    [Fact]
    public async Task A_large_devbook_draws_a_page_of_options_and_says_how_many_more_match()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] =
        [
            .. Enumerable.Range(1, 400).Select(i => new DevbookReferenceTarget($".devbook/domain/tasks/page-{i}.md", $"Page {i}", "domain", 0, null))
        ];
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        await OpenPickerAsync(pane);

        pane.WaitForAssertion(() =>
        {
            var drawn = pane.FindAll("[data-testid='entry-devbook-select'] [role='option']").Count;
            Assert.InRange(drawn, 1, 100);
            Assert.Contains($"{400 - drawn} more", pane.Find("[data-testid='entry-devbook-select'] .tag-select__more").TextContent, StringComparison.Ordinal);
        });
    }

    // --- The picker's open state belongs to the entry it was opened on ----------

    private const string FocusIdentifier = "Blazor._internal.domWrapper.focus";

    private static int FocusCalls(TasksPaneHost host) =>
        host.Context.JSInterop.Invocations.Count(invocation => invocation.Identifier == FocusIdentifier);

    /// <summary>
    /// The picker is one per pane, not one per entry, so it has to be put away when
    /// the entry it was opened on goes. Switching rows did; closing the panel and
    /// then starting a new entry did not, and the new entry's panel opened with
    /// the picker already open — pressing "Link a Devbook chapter" then closed it.
    /// </summary>
    [Fact]
    public async Task Closing_the_panel_puts_the_picker_away_so_a_new_entry_opens_without_it()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] = TasksTargets;
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        await OpenPickerAsync(pane);
        Assert.NotEmpty(pane.FindAll("[data-testid='entry-devbook-select']"));

        await pane.Find("[data-testid='close-entry-button']").ClickAsync(new());
        pane.WaitForAssertion(() => Assert.Null(host.State.SelectedRow));

        await pane.InvokeAsync(() => host.WriteEntryAsync("# Next one\n`task` `repo:backlog`\n\nA new entry.\n"));

        pane.WaitForAssertion(() =>
        {
            Assert.NotNull(host.State.SelectedRow);
            Assert.Equal("false", pane.Find("[data-testid='entry-action-devbook-set']").GetAttribute("aria-expanded"));
            Assert.Empty(pane.FindAll("[data-testid='entry-devbook-select']"));
        });

        // And the first press opens it, rather than closing a picker left over.
        await pane.Find("[data-testid='entry-action-devbook-set']").ClickAsync(new());
        Assert.NotEmpty(pane.FindAll("[data-testid='entry-devbook-select']"));
    }

    /// <summary>Escape in the picker is about the picker: it closes, the entry
    /// stays open, and the focus goes back to the action that opened it rather
    /// than off the panel.</summary>
    [Fact]
    public async Task Escape_in_the_picker_closes_the_picker_and_not_the_entry()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] = TasksTargets;
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        await OpenPickerAsync(pane);
        var before = FocusCalls(host);

        await pane.Find("[data-testid='entry-devbook-select'] input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='entry-devbook-select']"));
            Assert.Equal(row.TaskId, host.State.SelectedRow?.TaskId);
            Assert.True(FocusCalls(host) > before);
        });

        // A second Escape, now on the panel itself, is what closes the entry.
        await pane.Find("[data-testid='entry-action-devbook-set']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        pane.WaitForAssertion(() => Assert.Null(host.State.SelectedRow));
    }

    /// <summary>Pressing "Link a Devbook chapter" puts the caret in the picker, so
    /// the next key is the query.</summary>
    [Fact]
    public async Task Opening_the_picker_puts_the_focus_in_its_input()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] = TasksTargets;
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        var before = FocusCalls(host);

        await pane.Find("[data-testid='entry-action-devbook-set']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Equal(before + 1, FocusCalls(host)));
    }

    // --- A typed path without its .devbook/ --------------------------------------

    /// <summary>The chips and the picker show a page's path without the
    /// <c>.devbook/</c> every one of them starts with, so that is the form a reader
    /// types back. It is stored in the form the repository's own pages have.</summary>
    [Fact]
    public async Task A_typed_path_without_its_devbook_root_is_stored_as_the_page_it_names()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] = TasksTargets;
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);

        await host.State.AddDevbookReferenceAsync(row, "domain/tasks/domain.md#task");

        Assert.Equal([TaskChapter], row.DevbookReferences);
    }

    /// <summary>A repository on the legacy layout keeps its folders at the root as
    /// <c>.domain/</c>; the page it lists is the form that is stored.</summary>
    [Fact]
    public async Task A_typed_path_takes_the_form_of_the_repository_s_own_page_on_the_legacy_layout()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] = [new DevbookReferenceTarget(".domain/tasks/domain.md", "Tasks domain", "domain", 0, null)];
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);

        // What the open picker was offered is what the write is measured against.
        Assert.Single(host.State.DevbookTargetsFor(row));

        await host.State.AddDevbookReferenceAsync(row, "domain/tasks/domain.md#task");

        Assert.Equal([".domain/tasks/domain.md#task"], row.DevbookReferences);
    }

    /// <summary>With nothing listed — a devbook still being read — the screen
    /// leaves the typed path alone, and the module's own rule stores a path whose
    /// first folder is a devbook area under <c>.devbook/</c>; one that is not is
    /// stored exactly as typed.</summary>
    [Theory]
    [InlineData("arc42/09-decisions.md#storage", ".devbook/arc42/09-decisions.md#storage")]
    [InlineData(".devbook/tech/shared.md", ".devbook/tech/shared.md")]
    [InlineData("docs/handbook.md", "docs/handbook.md")]
    public async Task With_nothing_listed_an_area_path_is_stored_under_the_devbook_root(string typed, string stored)
    {
        var devbook = new FakeDevbookReferenceResolver();
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);

        Assert.Equal(typed, host.State.NormalizeDevbookReference(row, typed));

        await host.State.AddDevbookReferenceAsync(row, typed);

        Assert.Equal([stored], row.DevbookReferences);
    }

    // --- Writes that overlap ------------------------------------------------------

    /// <summary>
    /// Two picks in quick succession both land. Each write used to be the list the
    /// row had plus the pick, and the row only learned of a write once it had
    /// landed — so a second pick made while the first was saving was computed from
    /// the list without the first, and its save put that list back.
    /// </summary>
    [Fact]
    public async Task Two_overlapping_additions_both_land()
    {
        var devbook = new FakeDevbookReferenceResolver();
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);

        await Task.WhenAll(
            host.State.AddDevbookReferenceAsync(row, TaskChapter),
            host.State.AddDevbookReferenceAsync(row, TasksPage));

        Assert.Equal([TaskChapter, TasksPage], row.DevbookReferences);

        var stored = (await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken)).Single(entry => entry.Id == row.Id);
        Assert.Equal([TaskChapter, TasksPage], stored.DevbookReferences);
    }

    [Fact]
    public async Task An_addition_and_a_removal_that_overlap_both_land()
    {
        var devbook = new FakeDevbookReferenceResolver();
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SetDevbookReferencesAsync(row, [TaskChapter]);

        await Task.WhenAll(
            host.State.AddDevbookReferenceAsync(row, TasksPage),
            host.State.RemoveDevbookReferenceAsync(row, TaskChapter));

        Assert.Equal([TasksPage], row.DevbookReferences);
    }

    /// <summary>The picker hands back the whole list it holds, which after a re-render
    /// is the row's list from before a pick still saving. What the pane writes is
    /// what changed in it — the pick — so the save in flight is not undone.</summary>
    [Fact]
    public async Task A_pick_made_while_the_last_one_is_saving_keeps_both()
    {
        var devbook = new FakeDevbookReferenceResolver();
        devbook.Targets["backlog"] = TasksTargets;
        using var host = await TasksPaneHost.CreateAsync(devbook, Repositories);
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        await OpenPickerAsync(pane);

        // The first pick is still on its way to the store when the second is made
        // from the list the picker was drawn with.
        var first = host.State.AddDevbookReferenceAsync(row, TasksPage);
        await pane.FindAll("[data-testid='entry-devbook-select'] [role='option']")
            .Single(option => option.QuerySelector(".tag-select__option-label")!.TextContent == "Task")
            .ClickAsync(new());
        await first;

        pane.WaitForAssertion(() => Assert.Equal([TasksPage, TaskChapter], row.DevbookReferences));
    }
}
