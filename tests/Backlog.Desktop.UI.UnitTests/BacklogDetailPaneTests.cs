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
        Assert.Contains("Provision the box", pane.Find("[data-testid='entry-detail']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_close_button_leaves_the_pane_with_nothing_open()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Provision the box\n`task`\n");

        var pane = host.Render();
        await pane.Find("[data-testid='close-entry-button']").ClickAsync(new());

        Assert.Null(host.State.SelectedRow);
        Assert.Single(pane.FindAll("[data-testid='entry-detail-empty']"));
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

        Assert.Equal("Wire up the store", pane.Find("[data-testid='subitem-list-0-title']").TextContent);
        Assert.Equal("Write the rows", pane.Find("[data-testid='subitem-list-1-title']").TextContent);

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

    /// <summary>"Next step" asks for a name before it adds one. A step called nothing
    /// is a chapter the reader cannot see, select or remove.</summary>
    [Fact]
    public async Task Next_step_adds_a_named_step_at_the_end()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        // Unset, so there is no field yet.
        Assert.Empty(pane.FindAll("[data-testid='entry-step-input']"));

        await pane.Find("[data-testid='entry-action-step-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-step-input']").InputAsync(new() { Value = "Run the migration" });
        await pane.Find("[data-testid='entry-step-input']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(3, row.PreviewSubItems.Count);
        Assert.Equal("Run the migration", EntryTextParser.GetSubItemTitle(row.RawText, 2));
    }

    [Fact]
    public async Task An_unnamed_step_is_not_added()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-step-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-step-input']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(2, row.PreviewSubItems.Count);
    }

    // --- The note ----------------------------------------------------------

    [Fact]
    public async Task The_note_editor_writes_the_entrys_prose_and_leaves_its_steps_alone()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        var editor = pane.Find("[data-testid='entry-note-editor'] textarea");
        Assert.Contains("Notes on the parent.", editor.TextContent, StringComparison.Ordinal);

        await editor.InputAsync(new() { Value = "Rewritten prose." });

        Assert.Contains("Rewritten prose.", row.RawText, StringComparison.Ordinal);
        Assert.DoesNotContain("Notes on the parent.", row.RawText, StringComparison.Ordinal);
        Assert.Equal(2, row.PreviewSubItems.Count);
        Assert.Contains("`*high`", row.RawText, StringComparison.Ordinal);
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

        await pane.Find("[data-testid='priority-badge'] select").ChangeAsync(new() { Value = nameof(Priority.High) });
        Assert.Contains("`*high`", row.RawText, StringComparison.Ordinal);

        await pane.Find("[data-testid='status-badge'] select").ChangeAsync(new() { Value = nameof(EntryStatus.Ready) });
        Assert.Contains("`!ready`", row.RawText, StringComparison.Ordinal);

        await pane.Find("[data-testid='entry-tags-input']").ChangeAsync(new() { Value = "#sync desktop" });
        Assert.Contains("`#sync`", row.RawText, StringComparison.Ordinal);
        Assert.Contains("`#desktop`", row.RawText, StringComparison.Ordinal);

        Assert.Single(pane.FindAll("[data-testid='area-badge']"));
    }

    // --- What is deliberately absent --------------------------------------

    /// <summary>
    /// Two controls the To Do layout has and this pane does not.
    /// <para>
    /// The star, because <c>TaskRow.Important</c> exists and a backlog entry has no
    /// importance field: whatever ends up setting it is a decision the product has not
    /// made, and a control that set it here would be inventing domain state — the flag
    /// would go nowhere and the reader would think they had said something.
    /// </para>
    /// <para>
    /// "Add file", because the domain has no attachment concept at all. The storybook
    /// shows it disabled as a placeholder, which is what a storybook is for; shipping a
    /// permanently dead control is not.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Neither_the_star_nor_add_file_is_offered()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-action-important']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-action-file']"));
        Assert.DoesNotContain("Add file", pane.Markup, StringComparison.Ordinal);

        // Nothing sets Important either, so no row can be wearing the star.
        Assert.Empty(pane.FindAll(".task-item__detail--important"));
    }
}
