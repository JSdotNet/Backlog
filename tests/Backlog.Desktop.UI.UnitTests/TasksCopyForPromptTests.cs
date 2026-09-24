using Microsoft.AspNetCore.Components.Web;
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
/// it into a model is pasting noise. What an entry copies instead is one line
/// ahead of the title — <see cref="EntryRunMarker"/>, the command that runs the
/// entry, with the stored id the session needs to find it again. That is what
/// turns the paste into a runnable prompt, and it is the reason a bare entry no
/// longer copies a bare title.
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

    private static string MarkerFor(EntryRow row) =>
        $"/backlog-tools:backlog-run-plan-item entry `{row.Id}`:";

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

        // The command leads, the title directly under it, because a headless
        // paragraph no longer says which task it was. Then everything under the
        // metadata line — the prose and the steps both, since a brief that
        // stopped before its steps is half a brief. A `task` gets the command
        // like any other entry: the skill it invokes is what declines to run one.
        Assert.StartsWith($"{MarkerFor(row)}\nShip the sync spike\n\n", copied, StringComparison.Ordinal);
        Assert.Contains("Work out whether the delta protocol survives a three-way merge.", copied, StringComparison.Ordinal);
        Assert.Contains("## Wire up the store", copied, StringComparison.Ordinal);
        Assert.Contains("How the store gets wired.", copied, StringComparison.Ordinal);

        Assert.DoesNotContain("`task`", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("!ready", copied, StringComparison.Ordinal);
    }

    /// <summary>An entry that is nothing but its title copies the command and
    /// the title, and nothing after: "no body" must not become a copy with an
    /// empty line welded on the end.</summary>
    [Fact]
    public async Task An_entry_with_nothing_under_the_title_copies_the_command_and_the_title()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var row = await host.WriteEntryAsync("# Rename the pane\n`task` `@backlog`\n");
        var pane = host.Render();

        pane.Find($"[data-testid='entry-list-{EntryTaskId(row)}-copy']").Click();

        Assert.Equal(
            $"{MarkerFor(row)}\nRename the pane",
            Assert.Single(host.Context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    /// <summary>An imported prompt copies the command over the entry the plan
    /// wrote — its plan-item marker included, since that is body prose and the
    /// one place the plan's own slugs survive the metadata line being left
    /// behind. The command carries the stored id and nothing else; the plan, the
    /// repositories and the dependencies are the connector's to answer.</summary>
    [Fact]
    public async Task An_imported_prompt_copies_the_command_over_its_plan_item_marker()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var pane = host.Render();
        pane.Find("[data-testid='import-plan-open']").Click();
        pane.Find("[data-testid='import-plan-text'] textarea").Input(
            "# First prompt\n`prompt` `+myplan` `id:first` `repo:backlog`\n\n"
            + "Backlog plan item `first` of plan `myplan` for `backlog` — run it with the `backlog-run-plan-item` skill.\n\nDo the first thing.\n");
        await pane.Find("[data-testid='import-plan-submit']").ClickAsync(new());

        var row = host.State.Rows.Single(r => r.PreviewImportItemId == "first");

        pane.Find($"[data-testid='entry-list-{EntryTaskId(row)}-copy']").Click();

        Assert.Equal(
            $"{MarkerFor(row)}\nFirst prompt\n\n"
            + "Backlog plan item `first` of plan `myplan` for `backlog` — run it with the `backlog-run-plan-item` skill.\n\nDo the first thing.",
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
        // brief and would arrive unlabelled in the middle of this one. No command
        // either — a step is not an entry Backlog can be asked about.
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
        Assert.StartsWith(MarkerFor(row), copies[0], StringComparison.Ordinal);
        Assert.Contains("## Wire up the store", copies[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Copying from the row leaves the side panel open.
    /// <para>
    /// The click itself never reached the row — the copy button stops it — but the
    /// focus did move: pressing the button focuses it, the focusout climbs to the
    /// pane, and a focus landing in the list outside the detail half used to read
    /// as the reader walking away from the entry. The stand-in for
    /// <c>backlogFocusOutside</c> answers the question against this render's own
    /// DOM, with the row's copy button as the focused element, so it is the
    /// selectors the pane actually sends that decide.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Copying_from_the_row_leaves_the_side_panel_open()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var row = await host.WriteEntryAsync(EntryWithSteps);
        await host.OpenAsync(row);
        var pane = host.Render();

        var rowCopy = $"[data-testid='entry-list-{EntryTaskId(row)}-copy']";

        bool FocusIsOutside(JSRuntimeInvocation invocation) =>
            !invocation.Arguments.Cast<string>().Any(selector =>
                pane.FindAll(selector).Any(region => region.Matches(rowCopy) || region.QuerySelector(rowCopy) is not null));

        host.Context.JSInterop.Setup<bool>("backlogFocusOutside", FocusIsOutside).SetResult(true);
        host.Context.JSInterop.Setup<bool>("backlogFocusOutside", invocation => !FocusIsOutside(invocation)).SetResult(false);

        pane.Find("[data-testid='entry-panel-copy']").Click();
        pane.Find(rowCopy).Click();
        await pane.Find("[data-testid='backlog-pane']").TriggerEventAsync("onfocusout", new FocusEventArgs());

        Assert.Same(row, host.State.SelectedRow);
        Assert.Single(pane.FindAll("[data-testid='entry-panel-copy']"));
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
            $"{MarkerFor(row)}\nRename the pane",
            Assert.Single(host.Context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }
}
