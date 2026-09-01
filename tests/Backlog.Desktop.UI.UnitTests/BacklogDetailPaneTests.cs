using Backlog.Modules.Backlog.DomainModels;
using Microsoft.AspNetCore.Components.Web;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pane's two halves: a list of entries and the open one beside it.
/// <para>
/// Almost every assertion here is about <c>RawText</c>, for the reason
/// <see cref="EntryScheduleControlsTests"/> gives about the scheduling rows — the
/// text <em>is</em> the entry, so a control that changed a field without changing
/// the text changed nothing that survives a save. What is new is where the controls
/// are: the list row completes and renames, and everything else is in the pane
/// beside it.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class BacklogDetailPaneTests
{
    private const string WithSteps =
        "# Ship the sync spike\n" +
        "`task` `*high` `!in-progress` `@backlog`\n\n" +
        "Notes on the parent.\n\n" +
        "## [ ] Wire up the store\n" +
        "How the store gets wired.\n\n" +
        "## Write the rows\n";

    private static string RowTestId(EntryRow row) => $"entry-list-{(row.Id ?? row.Key)}";

    // --- Selection ---------------------------------------------------------

    /// <summary>Pressing a row in the list opens it beside the list. The row does not
    /// decide that it is open — the pane hands it <c>SelectedId</c> — which is what
    /// makes "which entry is open" one answer rather than one per row.</summary>
    [Fact]
    public async Task Pressing_a_row_opens_it_in_the_detail_pane()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync("# Provision the box\n`task`\n");
        var second = await host.WriteEntryAsync("# Deploy it\n`task`\n");

        // Writing the second left it open, so pressing the first is a real move.
        Assert.Same(second, host.State.SelectedRow);

        var pane = host.Render();
        await pane.Find($"[data-testid='{RowTestId(first)}-open']").ClickAsync(new());

        Assert.Same(first, host.State.SelectedRow);

        // The open entry is the panel's heading, and the heading is the control that
        // retitles it — there is no pencil, so the title is its own target. That is
        // TaskPanel's decision rather than this pane's, which is the point of the
        // pane rendering the panel instead of a row of its own.
        Assert.Equal("Provision the box", pane.Find("[data-testid='entry-panel-title']").TextContent);

        await pane.Find("[data-testid='entry-panel-title']").ClickAsync(new());

        Assert.Equal(
            "Provision the box",
            pane.Find("[data-testid='entry-panel-rename']").GetAttribute("value"));
    }

    [Fact]
    public async Task The_close_button_leaves_the_pane_with_nothing_open()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Provision the box\n`task`\n");

        var pane = host.Render();
        await pane.Find("[data-testid='close-entry-button']").ClickAsync(new());

        Assert.Null(host.State.SelectedRow);
        Assert.Empty(pane.FindAll("[data-testid='entry-detail']"));
    }

    /// <summary>Selection follows the list. A filter that empties the list used to
    /// leave the pane beside it open on an entry that was no longer in it — a panel
    /// for "tes" standing next to "Nothing here yet." Filtered out is the same fact
    /// as deleted as far as this half of the split is concerned.</summary>
    [Fact]
    public async Task Filtering_the_open_entry_out_of_the_list_closes_the_pane()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `!draft`\n");
        await host.OpenAsync(row);

        var pane = host.Render();
        Assert.Single(pane.FindAll("[data-testid='entry-detail']"));

        host.State.SetStatusFilter("done");
        pane.Render();

        Assert.Empty(host.State.FilteredRows);
        Assert.Null(host.State.SelectedRow);
        Assert.Empty(pane.FindAll("[data-testid='entry-detail']"));

        // And the split collapses to the one column it now has something in.
        Assert.Contains("backlog-split--solo", pane.Find("[data-testid='backlog-split']").ClassList);
    }

    /// <summary>The entry that is still in view stays open, which is the other half
    /// of the same rule: closing the pane on every filter change would shut it on a
    /// reader who narrowed the list around the entry they were reading.</summary>
    [Fact]
    public async Task Filtering_leaves_an_entry_that_is_still_in_view_open()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Provision the box\n`task` `!draft`\n");
        var kept = await host.WriteEntryAsync("# Ship the spike\n`task` `!ready`\n");
        await host.OpenAsync(kept);

        var pane = host.Render();

        host.State.SetStatusFilter("ready");
        pane.Render();

        Assert.Same(kept, host.State.SelectedRow);
        Assert.Single(pane.FindAll("[data-testid='entry-detail']"));
    }

    /// <summary>Deleting the open entry closes the pane. A detail pane pointed at a
    /// deleted entry is a pane about nothing, and leaving the view to notice would be
    /// two answers to "what is selected".</summary>
    [Fact]
    public async Task Deleting_the_open_entry_closes_the_pane()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Provision the box\n`task`\n");

        var pane = host.Render();
        await pane.Find("[data-testid='delete-entry-button']").ClickAsync(new());

        Assert.Empty(host.State.Rows);
        Assert.Null(host.State.SelectedRow);
    }

    // --- Where "New entry" sits --------------------------------------------

    /// <summary>
    /// The control that writes the next entry is above the Completed section, not
    /// under it.
    /// <para>
    /// It used to render after the whole list, and the list owns that section — so
    /// with anything finished in the backlog the button sat below a fold of work
    /// already done, which is the one place in the column where nothing new is ever
    /// going to appear. It reaches its place through a slot on the list rather than by
    /// the pane rebuilding the section itself.
    /// </para>
    /// </summary>
    [Fact]
    public async Task New_entry_sits_above_the_completed_section()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Finished already\n`task` `!done`\n");
        await host.WriteEntryAsync("# Still going\n`task` `!in-progress`\n");

        var pane = host.Render();

        // Read off the rendered markup, because what is under test is order rather
        // than presence — the button was always present, and always in the wrong place.
        var markup = pane.Markup;
        var add = markup.IndexOf("new-entry-button", StringComparison.Ordinal);
        var completed = markup.IndexOf("entry-list-completed", StringComparison.Ordinal);

        Assert.True(add >= 0 && completed >= 0);
        Assert.True(add < completed, "The Completed section should come after the New entry button.");
    }

    /// <summary>With nothing in the list there is no list for the slot to be a slot
    /// in, and the control that writes the first entry is the one thing on this half
    /// that still has to be reachable.</summary>
    [Fact]
    public async Task New_entry_is_still_there_with_an_empty_list()
    {
        using var host = await BacklogPaneHost.CreateAsync();

        var pane = host.Render();

        Assert.Single(pane.FindAll("[data-testid='empty-state']"));
        Assert.Single(pane.FindAll("[data-testid='new-entry-button']"));
    }

    // --- Completing --------------------------------------------------------

    [Fact]
    public async Task The_circle_completes_the_entry_and_puts_it_back()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Ship it\n`task` `!in-progress`\n");

        var pane = host.Render();
        await pane.Find($"[data-testid='{RowTestId(row)}-check']").ClickAsync(new());

        Assert.Equal(EntryStatus.Done, row.PreviewStatus);
        Assert.Contains("`!done`", row.RawText, StringComparison.Ordinal);

        // Done goes back to InProgress, which is the only legal way off the finish
        // line. Read from the completed section, where the shared list moved it.
        pane.Render();
        await pane.Find("[data-testid='entry-list-completed-toggle']").ClickAsync(new());
        await pane.Find($"[data-testid='{RowTestId(row)}-check']").ClickAsync(new());

        Assert.Equal(EntryStatus.InProgress, row.PreviewStatus);
    }

    /// <summary>
    /// The circle completes a draft entry too, and this is why it is a control on
    /// every row rather than on some of them.
    /// <para>
    /// Draft to Done is not a step <c>.domain/backlog/flow.md#backlog-entry-lifecycle</c>
    /// lists, but the text route does not enforce the lifecycle: saving an entry from
    /// its markdown calls <c>SetStatus</c>, not <c>ChangeStatus</c>, so any status a
    /// person can type is a status the entry takes. The circle writes the same
    /// <c>!done</c> token through the same save, so it inherits exactly that — which
    /// is what makes it honest. A circle that silently did nothing on drafts would be
    /// the alternative, and a checkbox that does nothing is worse than none.
    /// </para>
    /// <para>
    /// The escape hatch's "reads as" line still flags the transition, because
    /// <c>EntryRow</c> derives that hint from the lifecycle rather than from what the
    /// save did. That divergence predates this pane and is left alone here rather than
    /// asserted either way: it is a question about whether the text route should be
    /// checked, and only the product can answer it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_circle_completes_a_draft_entry_because_the_text_route_is_unchecked()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Ship it\n`task` `!draft`\n");

        var pane = host.Render();
        await pane.Find($"[data-testid='{RowTestId(row)}-check']").ClickAsync(new());

        Assert.Equal(EntryStatus.Done, row.PreviewStatus);
        Assert.Contains("`!done`", row.RawText, StringComparison.Ordinal);
    }

    // --- Renaming ----------------------------------------------------------

    [Fact]
    public async Task Renaming_a_row_rewrites_the_title_line_and_nothing_else()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Ship it\n`task` `*high` `!draft`\n\nKeep this prose.\n");

        var pane = host.Render();
        await pane.Find($"[data-testid='{RowTestId(row)}-edit']").ClickAsync(new());
        await pane.Find($"[data-testid='{RowTestId(row)}-rename']").InputAsync(new() { Value = "Ship it properly" });
        await pane.Find($"[data-testid='{RowTestId(row)}-rename']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Ship it properly", row.PreviewTitle);
        Assert.StartsWith("# Ship it properly\n", row.RawText, StringComparison.Ordinal);
        Assert.Contains("`*high`", row.RawText, StringComparison.Ordinal);
        Assert.Contains("Keep this prose.", row.RawText, StringComparison.Ordinal);
    }

    // --- Reordering --------------------------------------------------------

    /// <summary>
    /// A drop moves the row into the target's place, arithmetic and all.
    /// <para>
    /// The arithmetic has to match <c>TaskMove.ApplyTo</c> exactly, because the list
    /// previews a drop by applying that method to the rows it was handed. Anything
    /// else here would land the row somewhere the reader had not been shown, which is
    /// the one way a reorder can be wrong without looking wrong.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_row_dropped_on_another_takes_its_place()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync("# One\n`task`\n");
        var second = await host.WriteEntryAsync("# Two\n`task`\n");
        var third = await host.WriteEntryAsync("# Three\n`task`\n");

        Assert.Equal(["One", "Two", "Three"], host.State.Rows.Select(row => row.PreviewTitle));

        // Down the list: the moved row ends up after the target, which is what
        // ApplyTo produces and therefore what the preview showed.
        await host.State.MoveEntryAsync(first, third);
        Assert.Equal(["Two", "Three", "One"], host.State.Rows.Select(row => row.PreviewTitle));

        // Up the list: before the target.
        await host.State.MoveEntryAsync(first, second);
        Assert.Equal(["One", "Two", "Three"], host.State.Rows.Select(row => row.PreviewTitle));
    }

    // --- Steps -------------------------------------------------------------

    [Fact]
    public async Task The_open_entrys_steps_are_a_list_of_their_own()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        // A step's title is a field rather than a line of text: the steps list is
        // DirectRename, so there is no pencil to press and nothing to read as text.
        Assert.Equal("Wire up the store", pane.Find("[data-testid='subitem-list-0-rename']").GetAttribute("value"));
        Assert.Equal("Write the rows", pane.Find("[data-testid='subitem-list-1-rename']").GetAttribute("value"));

        // The step's notes fold under it rather than opening somewhere else.
        await pane.Find("[data-testid='subitem-list-0-body-toggle']").ClickAsync(new());
        Assert.Contains(
            "How the store gets wired.",
            pane.Find("[data-testid='subitem-list-0-body']").TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>A step whose heading carries a literal <c>[ ]</c> has its marker
    /// flipped — the checkbox glyph is reserved for literal task-list syntax, so a
    /// step that wrote one gets one back.</summary>
    [Fact]
    public async Task Completing_a_step_with_a_marker_flips_the_marker()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();
        await pane.Find("[data-testid='subitem-list-0-check']").ClickAsync(new());

        Assert.Contains("## [x] Wire up the store", row.RawText, StringComparison.Ordinal);
        Assert.True(row.PreviewSubItems[0].Done);
    }

    /// <summary>
    /// A step with no marker is completed on its own metadata line instead.
    /// <para>
    /// Writing a <c>[ ]</c> into somebody's heading because they pressed a control
    /// would put checkbox syntax in a document that did not have it, and
    /// <c>#backlog-entry-structure</c> reserves that glyph for lines that do. The
    /// <c>!done</c> form is the same one a cascading parent status change already
    /// writes, and the read view already reads it back — so the circle is honest for
    /// every step rather than silently doing nothing on half of them.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Completing_a_step_without_a_marker_writes_its_status()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();
        await pane.Find("[data-testid='subitem-list-1-check']").ClickAsync(new());

        Assert.True(row.PreviewSubItems[1].Done);
        Assert.DoesNotContain("## [x] Write the rows", row.RawText, StringComparison.Ordinal);
        Assert.Contains("`!done`", EntryTextParser.GetSubItemText(row.RawText, 1), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renaming_a_step_keeps_its_level_and_its_marker()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        await host.State.RenameSubItemAsync(row, 0, "Wire up the store properly");

        Assert.Contains("## [ ] Wire up the store properly", row.RawText, StringComparison.Ordinal);
        Assert.Contains("How the store gets wired.", row.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_a_step_rewrites_the_entry_text()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        await host.State.MoveSubItemAsync(row, 1, 0);

        Assert.Equal("Write the rows", EntryTextParser.GetSubItemTitle(row.RawText, 0));
        Assert.Equal("Wire up the store", EntryTextParser.GetSubItemTitle(row.RawText, 1));
    }

    /// <summary>
    /// The bin at the end of a step's row deletes that step and nothing else.
    /// <para>
    /// Asked of the rendered control rather than of the state method beside it,
    /// because the thing worth proving is that the positional id the shared list
    /// hands back is read as the chapter the reader was pointing at — the step's id
    /// <em>is</em> its index, so an off-by-one here would delete the neighbour and
    /// look exactly like a working control.
    /// </para>
    /// <para>
    /// It takes no confirmation, on the same terms as the entry-level bin in the
    /// pane's footer: deleting a whole entry asks nothing, and a step is cheaper
    /// than the entry that holds it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Deleting_a_step_removes_that_step_and_leaves_the_rest_of_the_entry()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();
        await pane.Find("[data-testid='subitem-list-0-delete']").ClickAsync(new());

        Assert.Single(row.PreviewSubItems);
        Assert.Equal("Write the rows", EntryTextParser.GetSubItemTitle(row.RawText, 0));

        // The step's own notes go with it, and the entry's prose does not.
        Assert.DoesNotContain("How the store gets wired.", row.RawText, StringComparison.Ordinal);
        Assert.Contains("Notes on the parent.", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>An index that names no step changes nothing — the same guard the
    /// move has, and for the same reason: the id came off a row that may already
    /// have gone.</summary>
    [Fact]
    public async Task Deleting_a_step_that_is_not_there_changes_nothing()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);
        var before = row.RawText;

        await host.State.RemoveSubItemAsync(row, 7);
        await host.State.RemoveSubItemAsync(row, -1);

        Assert.Equal(before, row.RawText);
        Assert.Equal(2, row.PreviewSubItems.Count);
    }

    /// <summary>The steps list names a step before it adds one, from the add row it
    /// draws under the open steps. A step called nothing is a chapter the reader
    /// cannot see, select or remove.</summary>
    [Fact]
    public async Task The_add_row_adds_a_named_step_at_the_end()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        // There from the moment the list is, rather than behind a press: the shared
        // list draws its composer whenever a host is listening for new tasks, so
        // there is no unset state left to assert first.
        var field = pane.Find("[data-testid='subitem-list-add-input']");

        await field.InputAsync(new() { Value = "Run the migration" });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(3, row.PreviewSubItems.Count);
        Assert.Equal("Run the migration", EntryTextParser.GetSubItemTitle(row.RawText, 2));
    }

    [Fact]
    public async Task An_unnamed_step_is_not_added()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();
        await pane.Find("[data-testid='subitem-list-add-input']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(2, row.PreviewSubItems.Count);
    }

    /// <summary>An entry with no chapters yet still offers somewhere to write the
    /// first one. That is what the count guard in front of this list used to take
    /// away: the list was drawn only once a step existed, so the one control that
    /// could create one was behind the thing it created.</summary>
    [Fact]
    public async Task An_entry_with_no_steps_can_still_be_given_one()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Ship the sync spike\n`task`\n\nNotes on the parent.\n");

        var pane = host.Render();

        // An entry with no chapters opens on the markdown block, so the steps are one
        // press away — the same switch the block's own test uses, now a tab.
        await pane.Find("[data-testid='entry-view-steps']").ClickAsync(new());

        var field = pane.Find("[data-testid='subitem-list-add-input']");
        await field.InputAsync(new() { Value = "Wire up the store" });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Single(row.PreviewSubItems);
        Assert.Equal("Wire up the store", EntryTextParser.GetSubItemTitle(row.RawText, 0));
    }

    /// <summary>An entry with no steps draws the add row and nothing above it. The
    /// field is the empty state — a line saying "No steps yet." over the top of it
    /// is the same fact twice, and the one that cannot be typed into.</summary>
    [Fact]
    public async Task An_entry_with_no_steps_says_so_with_the_add_row_and_nothing_else()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Ship the sync spike\n`task`\n\nNotes on the parent.\n");

        var pane = host.Render();
        await pane.Find("[data-testid='entry-view-steps']").ClickAsync(new());

        Assert.Empty(pane.FindAll("[data-testid='subitem-list'] .task-list__empty"));
        Assert.DoesNotContain("No steps yet.", pane.Markup, StringComparison.Ordinal);
    }

    /// <summary>The field asks for the next step, in the one word that is true of
    /// every press of it. "Name the step" describes what typing does; "Next" is what
    /// the reader is writing down.</summary>
    [Fact]
    public async Task The_step_field_asks_for_the_next_one()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Ship the sync spike\n`task`\n\nNotes on the parent.\n");

        var pane = host.Render();
        await pane.Find("[data-testid='entry-view-steps']").ClickAsync(new());

        var field = pane.Find("[data-testid='subitem-list-add-input']");

        Assert.Equal("Next", field.GetAttribute("placeholder"));
        Assert.Equal("Next", field.GetAttribute("aria-label"));
    }

    /// <summary>
    /// The two readings are tabs, and the strip is wired as one.
    /// <para>
    /// It was a pressed ButtonGroup, and before that a row of chips: the same
    /// one-of-two choice drawn three ways, the last of which mimed a tab strip
    /// without any of what makes one. What a reader gets from the real thing is the
    /// part that was missing — the strip announces itself, the reading below is a
    /// panel named by the tab above it, and the pair is one stop in the tab order
    /// with the arrow keys moving between them rather than two stops that each
    /// toggle.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_bodys_two_readings_are_tabs()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        var strip = pane.Find("[data-testid='entry-view-switch']");
        Assert.Equal("tablist", strip.GetAttribute("role"));

        var steps = pane.Find("[data-testid='entry-view-steps']");
        var notes = pane.Find("[data-testid='entry-view-notes']");
        Assert.Equal("tab", steps.GetAttribute("role"));
        Assert.Equal("tab", notes.GetAttribute("role"));

        // This entry has chapters, so it opens on the steps.
        Assert.Equal("true", steps.GetAttribute("aria-selected"));
        Assert.Equal("false", notes.GetAttribute("aria-selected"));

        // One stop for the pair, not one each.
        Assert.Equal("0", steps.GetAttribute("tabindex"));
        Assert.Equal("-1", notes.GetAttribute("tabindex"));

        // And the reading below is the panel that tab names.
        var panel = pane.Find("[data-testid='entry-view-steps-panel']");
        Assert.Equal("tabpanel", panel.GetAttribute("role"));
        Assert.Equal(steps.Id, panel.GetAttribute("aria-labelledby"));
        Assert.Equal(panel.Id, steps.GetAttribute("aria-controls"));

        // The other reading is not on screen, and its panel is not drawn into.
        Assert.NotNull(pane.Find("[data-testid='entry-view-notes-panel']").GetAttribute("hidden"));
        Assert.Empty(pane.FindAll("[data-testid='entry-body-editor']"));
    }

    /// <summary>The arrow keys move between the tabs, which is the half of a tab
    /// strip a row of buttons cannot have: two toggles are two stops, and a reader
    /// tabbing through the pane should pass the choice once.</summary>
    [Fact]
    public async Task An_arrow_key_moves_between_the_bodys_readings()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        await pane.Find("[data-testid='entry-view-steps']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Equal("true", pane.Find("[data-testid='entry-view-notes']").GetAttribute("aria-selected"));
        Assert.Single(pane.FindAll("[data-testid='entry-body-editor']"));
    }

    // --- The body, as the markdown block -----------------------------------

    /// <summary>
    /// The markdown block is the whole body — prose and <c>##</c> chapters together —
    /// and it is the same text the steps list is a view of.
    /// <para>
    /// The whole body rather than the prose in front of the first chapter, which is
    /// what this used to write. A block scoped to the prose half would silently
    /// discard a step typed into it, and a surface whose promise is "this is the
    /// markdown" must not be a surface that eats half of it.
    /// </para>
    /// <para>
    /// The title and the metadata line are not in it, and stay put: those have
    /// controls of their own above, and the raw hatch is where the line itself is
    /// edited.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_markdown_block_writes_the_whole_body_and_leaves_the_metadata_line_alone()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        // This entry has chapters, so it opens on the steps. The block is one press
        // away, which is the switch this test is also exercising.
        await pane.Find("[data-testid='entry-view-notes']").ClickAsync(new());

        var editor = pane.Find("[data-testid='entry-body-editor'] textarea");
        Assert.Contains("Notes on the parent.", editor.TextContent, StringComparison.Ordinal);
        Assert.Contains("## [ ] Wire up the store", editor.TextContent, StringComparison.Ordinal);

        await editor.InputAsync(new() { Value = "Rewritten prose.\n\n## Only step now\n" });

        Assert.Contains("Rewritten prose.", row.RawText, StringComparison.Ordinal);
        Assert.DoesNotContain("Notes on the parent.", row.RawText, StringComparison.Ordinal);
        Assert.Single(row.PreviewSubItems);

        // The title line and its metadata survived a rewrite of everything under them.
        Assert.Contains("# Ship the sync spike", row.RawText, StringComparison.Ordinal);
        Assert.Contains("`*high`", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The steps view says when it is not showing everything.
    /// <para>
    /// It lists chapters, and the prose an entry opens with is not one — so a reader
    /// who saw only the steps would read the missing paragraph as text the app had
    /// lost. Announced rather than hidden, with the block that does show it one press
    /// away, and the press is the same one the switch makes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_steps_view_says_when_the_body_holds_prose_it_is_not_showing()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        Assert.Equal(EntryView.Steps, row.EffectiveView);
        Assert.Contains(
            "There is text on this entry the steps do not show.",
            pane.Find("[data-testid='entry-view-elsewhere']").TextContent,
            StringComparison.Ordinal);

        await pane.Find("[data-testid='entry-view-elsewhere-link']").ClickAsync(new());

        Assert.Equal(EntryView.Notes, row.EffectiveView);
        Assert.Single(pane.FindAll("[data-testid='entry-body-editor']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-view-elsewhere']"));
    }

    /// <summary>An entry whose body is nothing but chapters has no prose to leave
    /// off the screen, so the line does not appear. A notice that showed up
    /// regardless would be furniture rather than information.</summary>
    [Fact]
    public async Task Nothing_is_announced_when_the_steps_are_the_whole_body()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(
            "# Ship it\n" +
            "`task` `*high` `!ready`\n\n" +
            "## Wire up the store\n" +
            "How the store gets wired.\n");

        var pane = host.Render();

        Assert.Single(pane.FindAll("[data-testid='subitem-list-0']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-view-elsewhere']"));
    }

    /// <summary>An entry with no chapters opens on the markdown, without anything
    /// being written down. A default written into the text would be a preference
    /// nobody expressed, and it would have to be unwritten from every entry before
    /// the default could ever change.</summary>
    [Fact]
    public async Task An_entry_with_no_steps_opens_on_the_markdown_and_is_given_no_token()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Just prose\n`task`\n\nAll of it is a paragraph.\n");

        var pane = host.Render();

        Assert.Equal(EntryView.Notes, row.EffectiveView);
        Assert.Null(row.PreviewView);
        Assert.DoesNotContain("view:", row.RawText, StringComparison.Ordinal);
        Assert.Single(pane.FindAll("[data-testid='entry-body-editor']"));
    }

    // --- What the selectors and the tags still reach -----------------------

    /// <summary>Type, priority, repository, status and the tags editor all moved into
    /// the pane, and all five still edit the open entry. They were existing capability
    /// and the move was not allowed to lose any of them.</summary>
    [Fact]
    public async Task Every_selector_and_the_tag_editor_still_edit_the_open_entry()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var row = await host.WriteEntryAsync("# Ship it\n`task` `*medium` `!draft` `@backlog`\n");

        var pane = host.Render();

        await pane.Find("[data-testid='type-badge'] select").ChangeAsync(new() { Value = nameof(EntryType.Prompt) });
        Assert.Contains("`prompt`", row.RawText, StringComparison.Ordinal);

        // Priority is a row in the Ranking group now rather than a badge on this
        // strip, so setting it is press-the-row-then-pick — the same two steps as
        // a repeat.
        await pane.Find("[data-testid='entry-action-priority-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-priority-select'] select").ChangeAsync(new() { Value = nameof(Priority.High) });
        Assert.Contains("`*high`", row.RawText, StringComparison.Ordinal);

        await pane.Find("[data-testid='status-badge'] select").ChangeAsync(new() { Value = nameof(EntryStatus.Ready) });
        Assert.Contains("`!ready`", row.RawText, StringComparison.Ordinal);

        // The tags are chips and a field to type the next one into — TagMultiSelect
        // with AllowCreate, because a tag is whatever somebody typed. Typing and
        // pressing Enter is what the old space-separated line was; what is new is
        // that the tags already on the entry are chips beside the field rather than
        // more text inside it.
        await AddTagAsync(pane, "sync");
        await AddTagAsync(pane, "desktop");

        Assert.Contains("`#sync`", row.RawText, StringComparison.Ordinal);
        Assert.Contains("`#desktop`", row.RawText, StringComparison.Ordinal);

        // And taking one off is the chip's ✕, not editing a string back down.
        await pane.Find("[data-testid='entry-tags-input'] .tag-chip__remove").ClickAsync(new());

        Assert.DoesNotContain("`#sync`", row.RawText, StringComparison.Ordinal);
        Assert.Contains("`#desktop`", row.RawText, StringComparison.Ordinal);

        Assert.Single(pane.FindAll("[data-testid='area-badge']"));
    }

    /// <summary>Types a tag into the picker and commits it, which is the gesture the
    /// picker calls "create": the popup opens on input, the new tag is the active
    /// option when nothing else matches, and Enter takes it.</summary>
    private static async Task AddTagAsync(IRenderedComponent<BacklogPane> pane, string tag)
    {
        var field = pane.Find("[data-testid='entry-tags-input'] input");
        await field.InputAsync(new() { Value = tag });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }

    // --- What is deliberately absent --------------------------------------

    /// <summary>
    /// The one control the To Do layout has and this pane does not: the star.
    /// <para>
    /// <c>TaskRow.Important</c> exists and a backlog entry has no importance field,
    /// so whatever would set it is a decision the product has not made. A control
    /// here would be inventing domain state — the flag would go nowhere and the
    /// reader would think they had said something.
    /// </para>
    /// <para>
    /// Attachments used to be listed beside it for the same reason and are not any
    /// more: the entry carries one now, written on the metadata line as
    /// <c>files:</c>, so the row has somewhere to put what it is handed. Which is
    /// the rule these assertions are really about — the pane offers a control when
    /// the model can hold the answer, and not before.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_star_is_not_offered_because_nothing_could_hold_it()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-action-important']"));

        // Nothing sets Important either, so no row can be wearing the star.
        Assert.Empty(pane.FindAll(".task-item__detail--important"));
    }

    /// <summary>
    /// Attaching a place, and detaching it again.
    /// <para>
    /// One row and one path, which is the whole of the model: what is attached is a
    /// folder or an archive, so the row says which and what it is called. The ✕ and
    /// an emptied field are the same gesture, because an unset field carries no
    /// token rather than an empty one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_folder_can_be_attached_to_an_entry_and_taken_off_again()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Review the panel\n`task`\n");

        var pane = host.Render();

        // Nothing attached: the row says what it would do rather than what it is.
        Assert.Contains(
            "Attach a folder or zip",
            pane.Find("[data-testid='entry-action-files-set']").TextContent,
            StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='entry-action-files-clear']"));

        await pane.Find("[data-testid='entry-action-files-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-files-input']")
            .ChangeAsync(new() { Value = "D:/reviews/panel-review" });

        Assert.Contains("`files:D:/reviews/panel-review`", row.RawText, StringComparison.Ordinal);

        // The row names the place rather than reciting the path to it, and calls it
        // what it is.
        var set = pane.Find("[data-testid='entry-action-files-set']").TextContent;
        Assert.Contains("Folder", set, StringComparison.Ordinal);
        Assert.Contains("panel-review", set, StringComparison.Ordinal);

        await pane.Find("[data-testid='entry-action-files-clear']").ClickAsync(new());

        Assert.DoesNotContain("files:", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>A zip is an archive and reads as one, because "Folder" over a file
    /// is a word the row would be keeping untrue.</summary>
    [Fact]
    public async Task A_zip_reads_as_an_archive_rather_than_as_a_folder()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Review the panel\n`task` `files:D:/reviews/panel.zip`\n");

        var pane = host.Render();
        var set = pane.Find("[data-testid='entry-action-files-set']").TextContent;

        Assert.Contains("Archive", set, StringComparison.Ordinal);
        Assert.Contains("panel.zip", set, StringComparison.Ordinal);
        Assert.DoesNotContain("Folder", set, StringComparison.Ordinal);
    }

    // --- Where the status is -----------------------------------------------

    /// <summary>
    /// The status is on the panel's heading line and on the row in the list, and it
    /// is the same fact and the same control in both places.
    /// <para>
    /// It used to be a badge in the classification strip below the heading, which
    /// made a reader who opened a row look for it in a second place — and it is the
    /// one thing in that strip that is a state rather than something the entry is
    /// filed under. The row had no status at all, so the column could not be
    /// scanned for what was in progress without opening entries one at a time.
    /// </para>
    /// <para>
    /// The row's copy is a picker now rather than the read-only badge it started as,
    /// which is what makes this a pair of controls over one fact instead of a
    /// control and a rumour of it. It reads from the same entry either way, so a
    /// write on the heading line has to land on it — that is the assertion at the
    /// end, and it is the one that would catch the row being drawn from a snapshot
    /// of its own.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_status_reads_on_the_heading_line_and_on_the_row()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Ship it\n`task` `!in-progress`\n");

        var pane = host.Render();

        // On the heading line, as the panel's status slot rather than in Filing.
        var header = pane.Find("[data-testid='entry-panel'] .task-panel__header");
        Assert.NotNull(header.QuerySelector("[data-testid='status-badge']"));
        Assert.Null(pane.Find(".task-panel__filing").QuerySelector("[data-testid='status-badge']"));

        // And on the row, as the picker at the end of it. Not the badge that used to
        // be beside the title: BacklogRowPickersTests pins its absence, because a
        // word the reader cannot act on beside a control that says the same word is
        // the row telling them twice.
        var picker = pane.Find($"[data-testid='{RowTestId(row)}']").QuerySelector("[data-testid='row-status-badge'] select");
        Assert.NotNull(picker);
        Assert.Equal(nameof(EntryStatus.InProgress), picker.GetAttribute("value"));

        // Still editable from the heading line, and the write still goes to the text.
        await pane.Find("[data-testid='status-badge'] select").ChangeAsync(new() { Value = nameof(EntryStatus.Ready) });

        Assert.Contains("`!ready`", row.RawText, StringComparison.Ordinal);
        Assert.Equal(
            nameof(EntryStatus.Ready),
            pane.Find($"[data-testid='{RowTestId(row)}']")
                .QuerySelector("[data-testid='row-status-badge'] select")!
                .GetAttribute("value"));
    }

    /// <summary>The detail pane's groups carry no visible caption — the layout does
    /// that work — but each one is still named for a reader who cannot see the
    /// layout.</summary>
    [Fact]
    public async Task The_action_groups_are_named_without_drawing_a_caption()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Ship it\n`task`\n");

        var pane = host.Render();

        foreach (var testId in new[] { "entry-schedule-scheduling", "entry-schedule-ranking", "entry-schedule-attachments", "entry-schedule-dependencies" })
        {
            var group = pane.Find($"[data-testid='{testId}']");
            var caption = group.QuerySelector(".task-action-group__caption");

            Assert.NotNull(caption);
            Assert.Contains("sr-only", caption!.ClassList);
            Assert.Equal(caption.Id, group.GetAttribute("aria-labelledby"));
            Assert.False(string.IsNullOrWhiteSpace(caption.TextContent));
        }
    }

    /// <summary>What the entry waits on is under the columns rather than in one of
    /// them. Its value is a list of other entries where every other row's is a word,
    /// and a column is about sixteen rem wide: in one the picker's chips wrapped one
    /// per line and the row read as a paragraph. Out of the columns it is also out of
    /// their balancing, which is what leaves them three rows each and level.</summary>
    [Fact]
    public async Task Waiting_for_sits_below_the_columns_across_the_whole_pane()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Ship it\n`task`\n");

        var pane = host.Render();
        var schedule = pane.Find("[data-testid='entry-schedule']");

        Assert.Equal(
            ["task-action-pane__lead", "task-action-pane__columns", "task-action-pane__trailing"],
            schedule.Children.Select(child => child.ClassName));

        // The three that balance into two columns, and nothing else.
        Assert.Equal(
            ["entry-schedule-scheduling", "entry-schedule-ranking", "entry-schedule-attachments"],
            schedule.QuerySelector(".task-action-pane__columns")!
                .Children.Select(child => child.GetAttribute("data-testid")));

        Assert.Equal(
            "entry-schedule-dependencies",
            schedule.QuerySelector(".task-action-pane__trailing")!
                .Children.Single().GetAttribute("data-testid"));
    }

    // --- Cross-repository dependencies --------------------------------------

    /// <summary>The regression this guards: an entry that waits on one filed
    /// under a different repository must still read the true state of that wait
    /// once the repository filter has scoped it out of view.
    /// <para>
    /// Before <c>TaskListView.Universe</c> existed, the entry list's dependency
    /// lookup was built from the same <c>FilteredRows</c> it draws rows from —
    /// so scoping the list to one repository made every dependency filed under
    /// another one indistinguishable from an id naming nothing at all: Blocked,
    /// shown by its raw id, no matter how finished the entry behind it actually
    /// was. The row list itself stays repo-scoped; only the lookup a dependency
    /// resolves against had to widen.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_dependency_in_another_repository_still_resolves_under_a_repository_filter()
    {
        using var host = await BacklogPaneHost.CreateAsync(
            "repox = JSdotNet/RepoX",
            "repoy = JSdotNet/RepoY");

        var acrossRepo = await host.WriteEntryAsync("# Ship the library\n`task` `!done` `@repoy`\n");
        var scoped = await host.WriteEntryAsync(
            $"# Ship the app\n`task` `@repox` `after:{acrossRepo.Id!.Value}`\n");

        host.State.SetRepositoryFilter("repox");

        var pane = host.Render();

        // The filter really did narrow the list — the entry it depends on is
        // out of view, which is the precondition the bug needed to reproduce.
        Assert.Contains(scoped, host.State.FilteredRows);
        Assert.DoesNotContain(acrossRepo, host.State.FilteredRows);

        var row = pane.Find($"[data-testid='{RowTestId(scoped)}']");

        // Ready, not Blocked: the dependency is done, and that fact does not
        // stop being true because the row that records it scrolled out of the
        // filtered view.
        Assert.Empty(row.QuerySelectorAll(".task-item__detail--blocked"));
        Assert.NotNull(pane.Find($"[data-testid='{RowTestId(scoped)}-next']"));
    }
}
