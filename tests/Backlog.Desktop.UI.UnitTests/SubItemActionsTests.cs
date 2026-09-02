using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a step offers, and — now the more important half — what it does not.
/// <para>
/// A step used to carry two hand-off buttons of its own: push this chapter to GitHub
/// as its own issue, and start Copilot CLI. They have been removed twice. The first
/// time was collateral — they lived inside the sub-item metadata row, that row was
/// deleted along with the selectors on it, and nothing failed because the state
/// methods behind them were still covered. So they were restored, and asserted here.
/// </para>
/// <para>
/// This time the removal is the decision, and it comes out of the model rather than
/// out of taste. <c>.domain/tasks/domain.md</c> says a Sub-Item "may project to
/// GitHub issue task-list checkboxes" — checkboxes inside the entry's issue — and
/// <c>ProjectionRef</c> is owned by <c>TaskItem</c> and never by <c>SubItem</c>.
/// A step that was its own issue had nowhere to record the link it got back, so it
/// could be filed again and again with nothing noticing. The Copilot button was worse
/// than wrong: it handed over the <em>parent</em> entry from a row that made it look
/// like it acted on the step.
/// </para>
/// <para>
/// So these tests are the other side of the same fact. The hand-offs are asserted
/// where they belong — on the entry, in a group that says so out loud — and asserted
/// absent from a step, so a third accidental restoration fails instead of shipping.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class SubItemActionsTests
{
    private const string EntryWithSubItem =
        "# Ship the sync spike\n" +
        "`task` `*high` `!ready` `repo:backlog`\n\n" +
        "Notes on the parent.\n\n" +
        "## Wire up the store\n" +
        "How the store gets wired.\n";

    [Fact]
    public async Task A_step_offers_no_hand_off_of_its_own()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(EntryWithSubItem);

        // Every gate the removed markup used to ask about is open, so an absent
        // button here is a decision rather than an unmet condition.
        Assert.True(host.Features.IsEnabled(TasksFeatures.GitHubIntegration));
        Assert.True(host.Features.IsEnabled(AppFeatureKeys.CopilotCli));
        Assert.True(host.State.GitHubConfigured);
        Assert.True(row.IsPersisted);
        Assert.NotNull(host.State.RepositoryFor(row));

        var pane = host.Render();

        // The step itself is still there, listed by the shared task list.
        Assert.Single(pane.FindAll("[data-testid='subitem-list-0']"));

        Assert.Empty(pane.FindAll("[data-testid='subitem-github-push-button']"));
        Assert.Empty(pane.FindAll("[data-testid='subitem-copilot-cli-button']"));
    }

    /// <summary>The hand-offs live on the entry, and say so. "Create an issue"
    /// without saying what is being filed is the one thing a person would want to
    /// check before pressing it — and it was ambiguous for exactly as long as an
    /// identical button sat on a step.</summary>
    [Fact]
    public async Task The_hand_offs_are_grouped_and_labelled_as_acting_on_the_whole_entry()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync(EntryWithSubItem);

        var pane = host.Render();

        var group = pane.Find("[data-testid='entry-detail'] .entry-doc__actions");
        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal(
            "Hand over the whole entry: Ship the sync spike",
            group.GetAttribute("aria-label"));

        Assert.Equal(
            "Create an issue for this whole entry in JSdotNet/Backlog",
            pane.Find("[data-testid='github-push-button']").GetAttribute("aria-label"));

        Assert.Equal(
            "Start GitHub Copilot CLI for this whole entry",
            pane.Find("[data-testid='copilot-cli-button']").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Pressing_the_entry_push_creates_the_issue_for_the_entry()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync(EntryWithSubItem);

        var pane = host.Render();
        await pane.Find("[data-testid='github-push-button']").ClickAsync(new());

        // The entry's own title, not the step's — which is what the removed button
        // made ambiguous.
        Assert.Equal("Ship the sync spike", host.Client.CreatedTitle);
        Assert.Equal("JSdotNet/Backlog", host.Client.CreatedRepository);
    }

    /// <summary>Turning GitHub off takes the push button with it and leaves the
    /// Copilot hand-off alone. Two gates, two features.</summary>
    [Fact]
    public async Task The_hand_offs_are_gated_one_at_a_time()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync(EntryWithSubItem);

        _ = host.Features.SetEnabled(TasksFeatures.GitHubIntegration, false);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='github-push-button']"));
        Assert.Single(pane.FindAll("[data-testid='copilot-cli-button']"));
    }

    /// <summary>An entry with nowhere to push has no push button. Without a
    /// configured repository there is no issue to create, and a control that
    /// silently did nothing would be worse than no control.</summary>
    [Fact]
    public async Task No_configured_repository_means_no_push_button()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(EntryWithSubItem);

        Assert.False(host.State.GitHubConfigured);

        var pane = host.Render();

        Assert.Single(pane.FindAll("[data-testid='subitem-list-0']"));
        Assert.Empty(pane.FindAll("[data-testid='github-push-button']"));
    }

    /// <summary>The sub-item metadata selectors are not coming back either. Their
    /// removal was the decision the hand-off buttons were once collateral to, so this
    /// is the other half of that fact written down.</summary>
    [Fact]
    public async Task A_sub_item_still_has_no_type_priority_status_or_tag_editor()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync(EntryWithSubItem);

        var pane = host.Render();

        foreach (var testId in new[]
                 {
                     "subitem-type-badge",
                     "subitem-priority-badge",
                     "subitem-area-badge",
                     "subitem-status-badge",
                     "subitem-tags-input"
                 })
        {
            Assert.Empty(pane.FindAll($"[data-testid='{testId}']"));
        }
    }
}
