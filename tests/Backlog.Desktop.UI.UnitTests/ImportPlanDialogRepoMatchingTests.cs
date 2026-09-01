using Backlog.UI.Components.Selects;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Import dialog's repository matching rows.
/// <para>
/// A plan spanning repositories names them however its author wrote them, and
/// this is where somebody says which known repository each name means. Asserted
/// on the submission the dialog hands back rather than on the select markup,
/// because the submission is the whole of what the module acts on — a row that
/// looked right and submitted nothing would import the plan against names
/// nobody matched.
/// </para>
/// <para>
/// What the module then does with those names — resolve, register, memoize — is
/// <c>ImportPlanTests</c>' subject and deliberately not this one's. The dialog
/// stays dumb: it scans for names to ask about and reports the answers.
/// </para>
/// </summary>
public sealed class ImportPlanDialogRepoMatchingTests
{
    private const string TwoRepoPlan =
        "# First prompt\n`prompt` `#myplan` `repo:Fancy Widgets`\n\n"
        + "# Second prompt\n`prompt` `#myplan` `repo:gadgets`\n";

    [Fact]
    public void A_name_the_workspace_already_knows_asks_for_nothing()
    {
        using var dialog = Render();

        Paste(dialog.Component, "# Only prompt\n`prompt` `repo:widgets`\n");

        // AC5: the matched and single-repository cases stay exactly the two
        // fields they always were.
        Assert.Empty(dialog.Component.FindAll("[data-testid^='import-plan-repo-match-']"));
    }

    /// <summary>Case is not a difference. An alias is stored lower-cased and a
    /// plan writes what it likes, so a row for a name that only differs in case
    /// would be a decision nobody needs to make.</summary>
    [Fact]
    public void A_known_name_written_in_another_case_still_asks_for_nothing()
    {
        using var dialog = Render();

        Paste(dialog.Component, "# Only prompt\n`prompt` `repo:Widgets`\n");

        Assert.Empty(dialog.Component.FindAll("[data-testid^='import-plan-repo-match-']"));
    }

    [Fact]
    public void Each_unknown_name_in_the_plan_gets_one_row_to_answer()
    {
        using var dialog = Render();

        Paste(dialog.Component, TwoRepoPlan);

        var rows = dialog.Component.FindAll("[data-testid^='import-plan-repo-match-']");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Fancy Widgets", rows[0].TextContent);
        Assert.Contains("gadgets", rows[1].TextContent);
    }

    /// <summary>The same repository named by every entry in a plan is one
    /// decision, not one per entry.</summary>
    [Fact]
    public void A_name_repeated_across_entries_gets_one_row()
    {
        using var dialog = Render();

        Paste(
            dialog.Component,
            "# First prompt\n`prompt` `#myplan` `repo:newcomer`\n\n"
            + "# Second prompt\n`prompt` `#myplan` `repo:newcomer`\n");

        Assert.Single(dialog.Component.FindAll("[data-testid^='import-plan-repo-match-']"));
    }

    [Fact]
    public void The_matches_somebody_picked_travel_with_the_submission()
    {
        using var dialog = Render();
        Paste(dialog.Component, TwoRepoPlan);

        Choose(dialog.Component, "import-plan-repo-match-0", "widgets");
        dialog.Component.Find("[data-testid='import-plan-submit']").Click();

        var submission = Assert.Single(dialog.Submissions);
        Assert.Equal(TwoRepoPlan, submission.RawText);

        // Only the one that was answered. The other row was left on "Register as
        // new repository", which is an answer the module already knows how to act
        // on and not something for this dialog to spell out.
        var match = Assert.Single(submission.RepoMatches!);
        Assert.Equal("Fancy Widgets", match.Key);
        Assert.Equal("widgets", match.Value);
    }

    [Fact]
    public void Leaving_every_row_alone_submits_no_matches_at_all()
    {
        using var dialog = Render();
        Paste(dialog.Component, TwoRepoPlan);

        dialog.Component.Find("[data-testid='import-plan-submit']").Click();

        Assert.Null(Assert.Single(dialog.Submissions).RepoMatches);
    }

    /// <summary>Editing the plan after answering a row drops the answer with the
    /// name it was about, so a match for a repository the text no longer mentions
    /// cannot be submitted.</summary>
    [Fact]
    public void A_match_for_a_name_edited_out_of_the_plan_is_forgotten()
    {
        using var dialog = Render();
        Paste(dialog.Component, TwoRepoPlan);
        Choose(dialog.Component, "import-plan-repo-match-0", "widgets");

        Paste(dialog.Component, "# Second prompt\n`prompt` `#myplan` `repo:gadgets`\n");
        dialog.Component.Find("[data-testid='import-plan-submit']").Click();

        Assert.Null(Assert.Single(dialog.Submissions).RepoMatches);
    }

    private static void Paste(IRenderedComponent<ImportPlanDialog> component, string text) =>
        component.Find("[data-testid='import-plan-text'] textarea").Input(text);

    private static void Choose(IRenderedComponent<ImportPlanDialog> component, string testId, string alias) =>
        component.Find($"[data-testid='{testId}'] select").Change(alias);

    private static DialogRenderContext Render()
    {
        var context = new BunitContext();

        // The dialog's Modal pulls focus onto itself when it opens, which is a
        // JS call and nothing this test is about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var submissions = new List<ImportPlanSubmission>();

        var component = context.Render<ImportPlanDialog>(parameters => parameters
            .Add(dialog => dialog.Open, true)
            .Add(dialog => dialog.Options,
            [
                new SelectorOption("widgets", "widgets"),
                new SelectorOption("backlog", "backlog")
            ])
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
