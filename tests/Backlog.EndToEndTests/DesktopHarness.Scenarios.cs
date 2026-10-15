using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// A repository registered through Settings › Repositories, its scenario page in
/// the Devbook pane, a task's acceptance checklist, and a delivery run in the
/// Sessions pane — what <see cref="ScenarioEvidenceTests"/> drives.
///
/// <para>The repository registry is the shared one every worktree's harness reads,
/// so a repository is only ever added and removed the way a person does it, through
/// the Repositories page, and never by writing the registry.</para>
/// </summary>
internal sealed partial class DesktopHarness
{
    private ILocator RepositoriesInput => Page.GetByTestId("github-repos-input");

    /// <summary>A repository's row in the Repositories table, by its alias.</summary>
    public ILocator RepositoryRow(string alias) =>
        Page.GetByTestId("repo-table").Locator($"[data-repository='{alias}']");

    /// <summary>
    /// Adds <paramref name="line"/> — <c>alias = owner/name</c> — to the repositories
    /// box and leaves the field, which is what saves it, then opens the new row and
    /// types its clone directory, committed with Enter. Done when the row lists the
    /// folder.
    /// </summary>
    public async Task AddRepositoryAsync(string alias, string fullName, string cloneDirectory)
    {
        await OpenSettingsAsync("repositories");

        await Interactive.RepeatAsync(
            async () =>
            {
                if (await RepositoryRow(alias).CountAsync() > 0) return;
                var current = (await RepositoriesInput.InputValueAsync()).TrimEnd();
                await RepositoriesInput.FillAsync(current.Length == 0 ? $"{alias} = {fullName}" : $"{current}\n{alias} = {fullName}");
                await RepositoriesInput.BlurAsync();
            },
            async () => await RepositoryRow(alias).CountAsync() > 0,
            $"adding {fullName} as {alias}");

        await OpenRepositoryCardAsync(alias);
        var clone = Page.GetByTestId("github-repo-card").GetByTestId("repo-clone-directory-input");
        await Interactive.RepeatAsync(
            async () =>
            {
                await clone.FillAsync(cloneDirectory);
                await clone.PressAsync("Enter");
            },
            async () => (await RepositoryRow(alias).GetByTestId("repo-table-folder").InnerTextAsync()).Trim() == cloneDirectory,
            $"setting {alias}'s clone directory");
    }

    /// <summary>Removes a repository with its card's Remove repository, when it is
    /// registered at all. Done when its row is gone.</summary>
    public async Task RemoveRepositoryAsync(string alias)
    {
        await OpenSettingsAsync("repositories");
        if (await RepositoryRow(alias).CountAsync() == 0) return;

        await OpenRepositoryCardAsync(alias);
        await Interactive.RepeatAsync(
            async () =>
            {
                var remove = Page.GetByTestId("github-repo-card").GetByTestId("remove-repository-button");
                if (await remove.CountAsync() > 0) await remove.ClickAsync();
            },
            async () => await RepositoryRow(alias).CountAsync() == 0,
            $"removing {alias}");
    }

    private Task OpenRepositoryCardAsync(string alias) =>
        Interactive.RepeatAsync(
            async () =>
            {
                var edit = RepositoryRow(alias).GetByTestId("repo-edit");
                if (await edit.GetAttributeAsync("aria-expanded") != "true") await edit.ClickAsync();
            },
            async () => await RepositoryRow(alias).GetByTestId("repo-edit").GetAttributeAsync("aria-expanded") == "true"
                        && await Page.GetByTestId("github-repo-card").CountAsync() > 0,
            $"opening {alias}'s card");

    /// <summary>Turns on one feature in Settings › Features by the name it is listed
    /// under. Kept per worktree, so this changes nothing elsewhere.</summary>
    public async Task EnableFeatureAsync(Regex feature)
    {
        await OpenSettingsAsync("features");
        var toggle = Page.GetByRole(AriaRole.Switch, new PageGetByRoleOptions { NameRegex = feature });
        await Interactive.RepeatAsync(
            async () =>
            {
                if (!await toggle.IsCheckedAsync()) await toggle.ClickAsync();
            },
            () => toggle.IsCheckedAsync(),
            $"turning on {feature}");
    }

    // ---- The Devbook pane's scenario dots ----

    /// <summary>A file row of the pane's menu, by the title it lists.</summary>
    public ILocator DevbookMenuFile(string title) =>
        DevbookPane.Locator(".devbook-menu__item--file").Filter(new LocatorFilterOptions { HasTextString = title });

    /// <summary>The key to the scenario dots under the pane's tree.</summary>
    public ILocator ScenarioLegend => DevbookPane.GetByTestId("devbook-scenario-legend");

    /// <summary>Closes the Devbook side pane when it is open. Which panes are open is
    /// the shared workspace's, so a test that opens an entry's detail sets it rather
    /// than trusting what the last test left.</summary>
    public Task CloseDevbookAsync()
    {
        var option = Page.GetByTestId("devbook-pane-option");
        return Interactive.RepeatAsync(
            async () =>
            {
                if (await option.CountAsync() > 0 && await option.GetAttributeAsync("aria-pressed") == "true") await option.ClickAsync();
            },
            async () => await option.CountAsync() == 0 || await option.GetAttributeAsync("aria-pressed") != "true",
            "closing the Devbook pane");
    }

    // ---- A task's acceptance checklist ----

    public ILocator AcceptanceChecklist => Page.GetByTestId("entry-acceptance");

    // ---- The Sessions pane ----

    /// <summary>Shows the Sessions surface from the header, and asks it to read the
    /// run files again.</summary>
    public async Task ShowSessionsAsync()
    {
        await Page.GotoAsync(_baseUrl.ToString());
        var toggle = Page.GetByTestId("sessions-toggle-button");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await toggle.GetAttributeAsync("aria-pressed") != "true") await toggle.ClickAsync();
            },
            () => Page.GetByTestId("sessions-panel").IsVisibleAsync(),
            "showing Sessions");

        var refresh = Page.GetByTestId("sessions-refresh");
        if (await refresh.CountAsync() > 0) await refresh.ClickAsync();
    }

    /// <summary>
    /// Presses one option of the Sessions pane's Show strip — <c>live</c> or
    /// <c>all</c> — and answers the one that was pressed before, so a test can put
    /// it back. A finished run with no session is a row only under All.
    /// </summary>
    public async Task<string> ChooseSessionsViewAsync(string view)
    {
        var before = await Page.GetByTestId("sessions-view-all").GetAttributeAsync("aria-pressed") == "true" ? "all" : "live";
        var option = Page.GetByTestId($"sessions-view-{view}");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await option.GetAttributeAsync("aria-pressed") != "true") await option.ClickAsync();
            },
            async () => await option.GetAttributeAsync("aria-pressed") == "true",
            $"showing {view} sessions");
        return before;
    }

    /// <summary>A row of the Sessions list by the title it shows. A run with no
    /// session is titled by its worktree folder's name.</summary>
    public ILocator SessionsRow(string title) =>
        Page.GetByTestId("sessions-row").Filter(new LocatorFilterOptions { HasTextString = title });

    /// <summary>The delivery run in the detail panel, by its id.</summary>
    public ILocator DeliveryRun(string runId) => Page.Locator($"[data-testid='sessions-run'][data-run-id='{runId}']");

    /// <summary>Selects a row and unfolds the run in its detail, then opens one of
    /// the run's stages, by name. Each press is repeated only while it has not
    /// taken: the first can land before the circuit attaches. The run is confirmed
    /// by the Title its detail shows, so a row of the same name cannot stand in.</summary>
    public async Task<ILocator> OpenRunStageAsync(string rowTitle, string runId, string runTitle, string stage)
    {
        var row = SessionsRow(rowTitle).First;
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await row.GetAttributeAsync("aria-pressed") != "true") await row.ClickAsync();
            },
            async () => await DeliveryRun(runId).CountAsync() > 0,
            $"selecting {rowTitle}");

        await Assertions.Expect(DeliveryRun(runId).GetByTestId("sessions-run-title")).ToHaveTextAsync(runTitle);

        var fold = DeliveryRun(runId).GetByTestId("sessions-run-fold");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await fold.GetAttributeAsync("aria-expanded") != "true") await fold.ClickAsync();
            },
            async () => await fold.GetAttributeAsync("aria-expanded") == "true",
            "unfolding the run");

        var stageRow = DeliveryRun(runId).Locator($"[data-testid='sessions-stage'][data-stage='{stage}']");
        var toggle = stageRow.GetByTestId("sessions-stage-toggle");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await toggle.GetAttributeAsync("aria-expanded") != "true") await toggle.ClickAsync();
            },
            async () => await toggle.GetAttributeAsync("aria-expanded") == "true",
            $"opening the {stage} stage");

        return stageRow;
    }
}
