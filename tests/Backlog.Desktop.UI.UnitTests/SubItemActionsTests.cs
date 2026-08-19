using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The two hand-offs a step offers, asserted on the rendered markup rather than on
/// the state behind it.
/// <para>
/// These exist because both buttons were once lost as collateral: they lived
/// inside the sub-item metadata row, that row was removed along with the
/// type/priority/status/tag selectors on it — which was the decision — and the two
/// actions went with it, which was not. Nothing failed. The state methods behind
/// them still existed and were still covered, so the only thing that had changed
/// was that no one could reach them.
/// </para>
/// <para>
/// Which is exactly why they are asserted again here after the cards became the
/// shared task list. A list of rows has no slot for a host's own controls unless
/// somebody asks for one, and the cheapest way through that would have been to drop
/// these two again. They arrive through <c>TaskListView.RowActions</c> instead.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class SubItemActionsTests
{
    private const string EntryWithSubItem =
        "# Ship the sync spike\n" +
        "`task` `*high` `!ready` `@backlog`\n\n" +
        "Notes on the parent.\n\n" +
        "## Wire up the store\n" +
        "How the store gets wired.\n";

    [Fact]
    public async Task A_sub_item_offers_both_hand_offs_when_their_gates_are_open()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(EntryWithSubItem);

        // Every gate the markup asks about: the feature, a configured GitHub, a
        // persisted row, and an area that resolves to a repository.
        Assert.True(host.Features.IsEnabled(BacklogFeatures.GitHubIntegration));
        Assert.True(host.Features.IsEnabled(AppFeatureKeys.CopilotCli));
        Assert.True(host.State.GitHubConfigured);
        Assert.True(row.IsPersisted);
        Assert.NotNull(host.State.RepositoryFor(row));

        var pane = host.Render();

        // The steps are the shared task list now, so a row is named by the list plus
        // its index rather than by a testid the pane wrote itself.
        Assert.Single(pane.FindAll("[data-testid='subitem-list-0']"));
        Assert.Single(pane.FindAll("[data-testid='subitem-github-push-button']"));
        Assert.Single(pane.FindAll("[data-testid='subitem-copilot-cli-button']"));
    }

    /// <summary>The push button names the repository it would create the issue in,
    /// because "create an issue" without saying where is the one thing a person
    /// would want to check before pressing it.</summary>
    [Fact]
    public async Task The_push_button_names_the_repository_it_would_write_to()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync(EntryWithSubItem);

        var push = host.Render().Find("[data-testid='subitem-github-push-button']");

        Assert.Equal("Create an issue in JSdotNet/Backlog", push.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Pressing_the_push_button_creates_the_issue_for_that_sub_item()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync(EntryWithSubItem);

        var pane = host.Render();
        await pane.Find("[data-testid='subitem-github-push-button']").ClickAsync(new());

        // The sub-item's own title, in the parent's repository — the sub-item has
        // no repository of its own to disagree about.
        Assert.Equal("Wire up the store", host.Client.CreatedTitle);
        Assert.Equal("JSdotNet/Backlog", host.Client.CreatedRepository);
    }

    /// <summary>Turning GitHub off takes the push button with it and leaves the
    /// Copilot hand-off alone. Two gates, two features.</summary>
    [Fact]
    public async Task The_hand_offs_are_gated_one_at_a_time()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync(EntryWithSubItem);

        _ = host.Features.SetEnabled(BacklogFeatures.GitHubIntegration, false);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='subitem-github-push-button']"));
        Assert.Single(pane.FindAll("[data-testid='subitem-copilot-cli-button']"));
    }

    /// <summary>An entry with nowhere to push has no push button. Without a
    /// configured repository there is no issue to create, and a control that
    /// silently did nothing would be worse than no control.</summary>
    [Fact]
    public async Task No_configured_repository_means_no_push_button()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(EntryWithSubItem);

        Assert.False(host.State.GitHubConfigured);

        var pane = host.Render();

        // The steps are the shared task list now, so a row is named by the list plus
        // its index rather than by a testid the pane wrote itself.
        Assert.Single(pane.FindAll("[data-testid='subitem-list-0']"));
        Assert.Empty(pane.FindAll("[data-testid='subitem-github-push-button']"));
    }

    /// <summary>The sub-item metadata selectors are not coming back. Their removal
    /// was the decision the restored buttons above were collateral to, so this is
    /// the other half of that fact written down.</summary>
    [Fact]
    public async Task A_sub_item_still_has_no_type_priority_status_or_tag_editor()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
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
