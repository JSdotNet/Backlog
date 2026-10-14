using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A remark on a Brain note (local ADR 0026 §3,
/// <c>.devbook/domain/devbook/features.md#brain-mode-of-the-devbook-pane</c>): the
/// reader leaves one on a block the way they do on a repository chapter, and it is
/// stored under the Brain's unscoped alias, keyed by the note's path from the Brain's
/// root — where <c>list_annotations</c> and <c>resolve_annotation</c> on
/// <c>#brain</c> look for it.
/// </summary>
public sealed class DevbookPaneBrainRemarkTests
{
    private const string Conventions = "## Areas\n\n- Projects — `projects`\n";
    private const string NotePath = "projects/plan.md";

    [Fact]
    public async Task A_remark_left_on_a_note_is_stored_under_the_brain_alias_by_the_notes_path()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var store = new SessionDevbookAnnotationStore(folders: harness.RepositoryFolders);
        harness.Context.Services.AddSingleton<IDevbookAnnotationStore>(store);
        harness.WriteNote(BrainConventions.FileName, Conventions);
        harness.WriteNote(NotePath, "# Plan\n\nShip the remark.\n");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='brain-note'] [data-testid^='markdown-comment-']")));

        await component.Find("[data-testid='brain-note'] [data-testid='markdown-comment-1']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".md-comment__edit textarea")));
        component.Find(".md-comment__edit textarea").Input("Which week?");
        component.Find(".md-comment__edit-actions [data-testid^='markdown-comment-save-']").Click();
        component.WaitForAssertion(() => Assert.Equal(
            "Which week?",
            component.Find(".md-block-row[data-block='1'] .md-comment__body").TextContent.Trim()));

        var stored = Assert.Single(store.List(BrainScope.Value, NotePath));
        Assert.Equal("Which week?", stored.Body);
        Assert.Equal(NotePath, stored.ChapterPath);
        Assert.Equal(1, stored.BlockIndex);
        Assert.Equal(MdBlockDigest.Of(MarkdownPreview.ParseDocument("# Plan\n\nShip the remark.\n"), 1), stored.BlockHash);
        Assert.Empty(store.List(harness.RepositoryAlias, NotePath));

        // Under its block, not in a margin: the pane is a narrow column of a wide
        // window, and the margin only falls back on the window's width.
        Assert.Empty(component.FindAll("[data-testid='brain-devbook-panel'] .md-view--margin"));
    }

    [Fact]
    public async Task A_remark_resolved_elsewhere_is_drawn_resolved_without_reopening_the_note()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var store = new SessionDevbookAnnotationStore();
        harness.Context.Services.AddSingleton<IDevbookAnnotationStore>(store);
        harness.WriteNote(BrainConventions.FileName, Conventions);
        harness.WriteNote(NotePath, "# Plan\n\nShip the remark.\n");
        var remark = store.Add(BrainScope.Value, NotePath, 1, "agent", null);
        store.Edit(remark.Id, "Answered over MCP?");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains(
            "Answered over MCP?",
            component.Find("[data-testid='brain-note'] .md-block-row[data-block='1']").TextContent,
            StringComparison.Ordinal));
        Assert.Empty(component.FindAll(".md-comment--resolved"));

        // What resolve_annotation does on #brain: the store, from another thread.
        await Task.Run(() => store.SetResolved(remark.Id, true), TestContext.Current.CancellationToken);

        component.WaitForAssertion(() => Assert.Single(component.FindAll(".md-comment--resolved")));
    }

    [Fact]
    public async Task A_repository_remark_on_the_same_path_is_not_the_notes()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var store = new SessionDevbookAnnotationStore();
        harness.Context.Services.AddSingleton<IDevbookAnnotationStore>(store);
        harness.WriteNote(BrainConventions.FileName, Conventions);
        harness.WriteNote(NotePath, "# Plan\n\nShip the remark.\n");
        var elsewhere = store.Add(harness.RepositoryAlias, NotePath, 1, "someone", null);
        store.Edit(elsewhere.Id, "About the repository's file.");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains("Ship the remark.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal));

        Assert.DoesNotContain("About the repository's file.", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("[data-testid='brain-note'] .md-comment"));
    }

    [Fact]
    public async Task Without_a_composed_store_a_remark_still_works_for_the_session()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, Conventions);
        harness.WriteNote(NotePath, "# Plan\n\nShip the remark.\n");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='brain-note'] [data-testid^='markdown-comment-']")));

        await component.Find("[data-testid='brain-note'] [data-testid='markdown-comment-1']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".md-comment__edit textarea")));
        component.Find(".md-comment__edit textarea").Input("Kept for now.");
        component.Find(".md-comment__edit-actions [data-testid^='markdown-comment-save-']").Click();

        component.WaitForAssertion(() => Assert.Contains("Kept for now.", component.Find(".md-block-row[data-block='1']").TextContent, StringComparison.Ordinal));
    }
}
