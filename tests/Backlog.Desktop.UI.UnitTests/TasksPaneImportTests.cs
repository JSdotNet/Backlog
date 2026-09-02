using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Import as the pane wires it: the button beside "New entry", the dialog it
/// opens, and where the sentence the module hands back is shown.
/// <para>
/// The handler's own behaviour is covered where it lives
/// (<c>Backlog.Modules.Tasks.UnitTests.ImportPlanTests</c>). What is under test
/// here is only the routing <c>TasksPane.OnImportAsync</c> performs — a result
/// closes the dialog and moves to the pane, a refusal stays under the paste box
/// with the text that caused it — because that decision is the pane's alone and
/// no handler test can see it.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksPaneImportTests
{
    private const string TwoPrompts =
        "# First prompt\n`prompt` `#myplan` `id:first`\n\nDo the first thing.\n\n"
        + "# Second prompt\n`prompt` `#myplan` `id:second` `after:first`\n\nDo the second thing.\n";

    /// <summary>Both ways of putting an entry in the list sit on one row. The row
    /// is the assertion: `.entry-add` is full width on its own, so the two buttons
    /// only read as alternatives while something holds them side by side.</summary>
    [Fact]
    public async Task Import_sits_in_the_add_row_beside_new_entry()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var pane = host.Render();
        var row = pane.Find(".entry-add-row");

        Assert.NotNull(row.QuerySelector("[data-testid='new-entry-button']"));
        Assert.NotNull(row.QuerySelector("[data-testid='import-plan-open']"));
    }

    [Fact]
    public async Task The_import_button_opens_the_dialog()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var pane = host.Render();
        Assert.Empty(pane.FindAll("[data-testid='import-plan-dialog']"));

        pane.Find("[data-testid='import-plan-open']").Click();

        Assert.NotNull(pane.Find("[data-testid='import-plan-dialog']"));
    }

    /// <summary>A run that produced something closes the dialog and moves the
    /// sentence to the pane, because the pane is where the new rows now are.</summary>
    [Fact]
    public async Task A_plan_that_imports_closes_the_dialog_and_reports_the_counts_on_the_pane()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var pane = host.Render();
        await SubmitAsync(pane, TwoPrompts);

        Assert.Empty(pane.FindAll("[data-testid='import-plan-dialog']"));
        Assert.Equal(
            "Imported: 2 created, 0 updated, 0 skipped.",
            pane.Find("[data-testid='import-plan-result']").TextContent.Trim());

        // A row carries the canonical text rather than a title of its own, so the
        // heading each imported entry landed with is read back off that.
        Assert.Equal(
            ["# First prompt", "# Second prompt"],
            host.State.Rows.Select(r => r.RawText.Split('\n')[0].Trim()).ToArray());
    }

    /// <summary>A refusal is the other half of that decision: the dialog stays
    /// open with the text still in it, and the pane says nothing — there is
    /// nothing new on it to talk about.
    /// <para>
    /// Driven through a plan whose two entries claim one <c>id:</c>, which is the
    /// refusal a person is most likely to actually hit: it is a plausible thing to
    /// write, and every use Import makes of an <c>id:</c> depends on it being one
    /// prompt's name.
    /// </para></summary>
    [Fact]
    public async Task A_plan_the_module_refuses_keeps_the_dialog_open_and_shows_why_there()
    {
        using var host = await TasksPaneHost.CreateAsync();

        const string duplicated =
            "# First prompt\n`prompt` `#myplan` `id:same`\n\n"
            + "# Second prompt\n`prompt` `#myplan` `id:same`\n";

        var pane = host.Render();
        await SubmitAsync(pane, duplicated);

        Assert.NotNull(pane.Find("[data-testid='import-plan-dialog']"));
        Assert.Contains("id:same", pane.Find(".import-plan-form__error").TextContent, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='import-plan-result']"));

        // Nothing half-landed while the refusal was being reported.
        Assert.Empty(host.State.Rows);
    }

    /// <summary>Opens the dialog, types the plan into the paste box, and presses
    /// Import — the three steps a person takes, in the order they take them.</summary>
    private static async Task SubmitAsync(IRenderedComponent<TasksPane> pane, string plan)
    {
        pane.Find("[data-testid='import-plan-open']").Click();
        pane.Find("[data-testid='import-plan-text'] textarea").Input(plan);

        await pane.Find("[data-testid='import-plan-submit']").ClickAsync(new());
    }
}
