using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Lifting a task's text off the screen and into a prompt.
/// <para>
/// Both lists in this pane used to refuse. The entry list refused on a taste
/// argument — a clipboard button on every row would be the loudest control in a
/// column whose job is scanning — which the shared row has since answered in CSS:
/// the button sits in the disabled-text colour until the row is reached. The steps
/// list refused for no recorded reason at all.
/// </para>
/// <para>
/// What each hands over is the point, not that a button exists. An entry row
/// carries no <c>Body</c> — that is deliberate, a body would give every row in the
/// column a disclosure — so left alone it would copy a bare title, which is not a
/// prompt. It is given the whole written entry instead. A step already carries its
/// note, so it needs nothing but permission.
/// </para>
/// <para>
/// Neither copies the metadata line. `task` `*high` `!ready` `@backlog` is this
/// app's bookkeeping about the entry, not something the reader wrote, and pasting
/// it into a model is pasting noise.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksCopyForPromptTests
{
    private const string EntryWithSteps =
        "# Ship the sync spike\n" +
        "`task` `*high` `!ready` `@backlog`\n\n" +
        "Work out whether the delta protocol survives a three-way merge.\n\n" +
        "## Wire up the store\n" +
        "How the store gets wired.\n";

    private static string EntryTaskId(EntryRow row) => (row.Id ?? row.Key).ToString();

    [Fact]
    public async Task An_entry_row_copies_the_whole_written_entry()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var row = await host.WriteEntryAsync(EntryWithSteps);
        var pane = host.Render();

        pane.Find($"[data-testid='entry-list-{EntryTaskId(row)}-copy']").Click();

        var copied = (string)Assert.Single(
            host.Context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]!;

        // The title leads, because a headless paragraph no longer says which task
        // it was. Then everything under the metadata line — the prose and the
        // steps both, since a brief that stopped before its steps is half a brief.
        Assert.StartsWith("Ship the sync spike\n\n", copied, StringComparison.Ordinal);
        Assert.Contains("Work out whether the delta protocol survives a three-way merge.", copied, StringComparison.Ordinal);
        Assert.Contains("## Wire up the store", copied, StringComparison.Ordinal);
        Assert.Contains("How the store gets wired.", copied, StringComparison.Ordinal);

        Assert.DoesNotContain("`task`", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("!ready", copied, StringComparison.Ordinal);
    }

    /// <summary>An entry that is nothing but its title copies the title. The
    /// fallback is the shared row's own, and it matters here because the hook this
    /// pane fills in must not turn "no body" into a copy of an empty line.</summary>
    [Fact]
    public async Task An_entry_with_nothing_under_the_title_copies_the_title()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var row = await host.WriteEntryAsync("# Rename the pane\n`task` `@backlog`\n");
        var pane = host.Render();

        pane.Find($"[data-testid='entry-list-{EntryTaskId(row)}-copy']").Click();

        Assert.Equal(
            "Rename the pane",
            Assert.Single(host.Context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    [Fact]
    public async Task A_step_copies_its_own_title_and_note()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        await host.WriteEntryAsync(EntryWithSteps);
        var pane = host.Render();

        pane.Find("[data-testid='subitem-list-0-copy']").Click();

        // The step, and only the step: the parent's prose is a different task's
        // brief and would arrive unlabelled in the middle of this one.
        Assert.Equal(
            "Wire up the store\n\nHow the store gets wired.",
            Assert.Single(host.Context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    /// <summary>The panel a row opens into copies the same task, so a reader who
    /// went in to read the brief does not have to go back out to lift it.</summary>
    [Fact]
    public async Task The_side_panel_copies_the_same_thing_the_row_does()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var row = await host.WriteEntryAsync(EntryWithSteps);
        await host.OpenAsync(row);
        var pane = host.Render();

        pane.Find("[data-testid='entry-panel-copy']").Click();
        pane.Find($"[data-testid='entry-list-{EntryTaskId(row)}-copy']").Click();

        var copies = host.Context.JSInterop.Invocations["backlogClipboard.copy"]
            .Select(i => (string)i.Arguments[0]!)
            .ToList();

        Assert.Equal(2, copies.Count);
        Assert.Equal(copies[0], copies[1]);
        Assert.Contains("## Wire up the store", copies[0], StringComparison.Ordinal);
    }

    /// <summary>An entry with nothing but a title still has a copy in the panel.
    /// The row falls back to the title on its own; the panel has no default to
    /// fall back to, so the pane hands it the title outright rather than leaving
    /// the reader a panel with a button missing from it.</summary>
    [Fact]
    public async Task The_side_panel_of_a_bare_entry_still_offers_its_title()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var row = await host.WriteEntryAsync("# Rename the pane\n`task` `@backlog`\n");
        await host.OpenAsync(row);
        var pane = host.Render();

        pane.Find("[data-testid='entry-panel-copy']").Click();

        Assert.Equal(
            "Rename the pane",
            Assert.Single(host.Context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }
}
