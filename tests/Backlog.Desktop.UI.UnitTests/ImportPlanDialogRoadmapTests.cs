using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Import dialog's roadmap half (ADR 0013, ruling 3): the preview of what the
/// document will produce, the "Lay out on the roadmap" option, and the sentence the
/// result is reported in. The dialog is handed the module's own preview function, as
/// the pane hands it, so what these show is what Import will do.
/// </summary>
public sealed class ImportPlanDialogRoadmapTests
{
    private const string TaskDocument =
        "# First\n`prompt` `+myplan`\n\n# Second\n`prompt` `+myplan`\n\n# Third\n`prompt` `+other`\n";

    private const string MixedDocument =
        "# The item\n`plan` `+myplan`\n\n# First\n`prompt` `+myplan`\n";

    [Fact]
    public void The_preview_states_how_many_roadmap_items_and_tasks_the_document_produces()
    {
        using var dialog = Render();

        Paste(dialog.Component, MixedDocument);

        Assert.Equal(
            "This document will produce 1 roadmap item and 1 task.",
            dialog.Component.Find("[data-testid='import-plan-preview']").TextContent.Trim());
    }

    [Fact]
    public void The_dialog_and_paste_box_carry_the_hooks_that_size_them_for_reading_a_plan()
    {
        using var dialog = Render();

        // app.css widens `.import-plan-dialog` past the shared modal width and gives
        // `.import-plan-form__text` the viewport's height; without either hook a pasted
        // plan is read through the default ten-row box.
        Assert.Single(dialog.Component.FindAll(".modal.import-plan-dialog"));
        Assert.Single(dialog.Component.FindAll(
            ".import-plan-form__text [data-testid='import-plan-text'], .import-plan-form__text textarea"));
    }

    [Fact]
    public void Nothing_typed_shows_no_preview_and_no_option()
    {
        using var dialog = Render();

        Assert.Empty(dialog.Component.FindAll("[data-testid='import-plan-preview']"));
        Assert.Empty(dialog.Component.FindAll("[data-testid='import-plan-lay-out']"));
    }

    [Fact]
    public void Lay_out_is_offered_off_for_a_task_document_and_counts_its_tags_when_on()
    {
        using var dialog = Render();
        Paste(dialog.Component, TaskDocument);

        var option = dialog.Component.Find("[data-testid='import-plan-lay-out'] input");
        Assert.False(option.HasAttribute("checked"));
        Assert.Contains("0 roadmap items and 3 tasks", Preview(dialog.Component));

        option.Change(true);

        Assert.Contains("2 roadmap items and 3 tasks", Preview(dialog.Component));
        dialog.Component.Find("[data-testid='import-plan-submit']").Click();
        Assert.True(Assert.Single(dialog.Submissions).LayOutOnRoadmap);
    }

    [Fact]
    public void Lay_out_is_not_offered_for_a_document_that_wrote_plan_entries()
    {
        using var dialog = Render();
        Paste(dialog.Component, TaskDocument);
        dialog.Component.Find("[data-testid='import-plan-lay-out'] input").Change(true);

        // Edited into a document that says which items it means: the option goes,
        // and a choice made before it did is not submitted behind the reader's back.
        Paste(dialog.Component, MixedDocument);
        dialog.Component.Find("[data-testid='import-plan-submit']").Click();

        Assert.Empty(dialog.Component.FindAll("[data-testid='import-plan-lay-out']"));
        Assert.False(Assert.Single(dialog.Submissions).LayOutOnRoadmap);
    }

    [Fact]
    public void Left_alone_the_option_submits_off()
    {
        using var dialog = Render();
        Paste(dialog.Component, TaskDocument);

        dialog.Component.Find("[data-testid='import-plan-submit']").Click();

        Assert.False(Assert.Single(dialog.Submissions).LayOutOnRoadmap);
    }

    [Fact]
    public void An_ordinary_task_import_reads_exactly_as_it_always_did()
    {
        var sentence = ImportPlanResultSentence.Describe(new ImportPlanResultDto(2, 0, 0, 0, 0, []));

        Assert.Equal("Imported: 2 created, 0 replaced, 0 updated, 0 skipped, 0 removed.", sentence);
    }

    [Fact]
    public void The_result_reports_both_halves()
    {
        var sentence = ImportPlanResultSentence.Describe(new ImportPlanResultDto(
            2, 0, 0, 0, 0, [],
            new RoadmapIntakeResultDto(1, 1, 1, ["No tag"], ["Sized"], ["shared"], [new ImportUnresolvedDependencyDto("+a", "first")]),
            [new ImportUnresolvedDependencyDto("first", "the-item")]));

        Assert.StartsWith("Imported: 2 created, 0 replaced, 0 updated, 0 skipped, 0 removed. Roadmap: 1 created, 1 updated, 1 re-lengthened.", sentence);
        Assert.Contains("No tag", sentence);
        Assert.Contains("Sized", sentence);
        Assert.Contains("+shared", sentence);
        Assert.Contains("first → the-item", sentence);
        Assert.Contains("+a → first", sentence);
    }

    [Fact]
    public void A_roadmap_refusal_says_the_tasks_were_kept()
    {
        var sentence = ImportPlanResultSentence.Describe(new ImportPlanResultDto(
            2, 0, 0, 0, 0, [],
            RoadmapIntakeResultDto.Refused("That dependency would make a cycle.")));

        Assert.EndsWith("Roadmap: nothing laid out — That dependency would make a cycle. The tasks were kept.", sentence);
    }

    private static string Preview(IRenderedComponent<ImportPlanDialog> component) =>
        component.Find("[data-testid='import-plan-preview']").TextContent;

    private static void Paste(IRenderedComponent<ImportPlanDialog> component, string text) =>
        component.Find("[data-testid='import-plan-text'] textarea").Input(text);

    private static DialogRenderContext Render()
    {
        var context = new BunitContext();

        // The dialog's Modal pulls focus onto itself when it opens, which is a
        // JS call and nothing this test is about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var submissions = new List<ImportPlanSubmission>();

        var component = context.Render<ImportPlanDialog>(parameters => parameters
            .Add(dialog => dialog.Open, true)
            .Add(dialog => dialog.Preview, ImportPlanPreview.Of)
            .Add(dialog => dialog.OnImport, submissions.Add));

        return new DialogRenderContext(context, component, submissions);
    }

    private sealed record DialogRenderContext(
        BunitContext Context,
        IRenderedComponent<ImportPlanDialog> Component,
        List<ImportPlanSubmission> Submissions) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }
}
