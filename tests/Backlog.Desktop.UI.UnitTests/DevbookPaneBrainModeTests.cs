using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook pane on the Brain (local ADR 0027 §4,
/// <c>.devbook/domain/devbook/features.md#brain-mode-of-the-devbook-pane</c>): with
/// nothing pressed, or the Brain as the anchor — both of which reach the pane as the
/// empty alias — it shows the person's Brain folder instead of a repository devbook.
/// A badge says Brain, the folder's path stands where the branch picker would, one tab
/// per declared area, and none of a repository's chrome.
/// </summary>
public sealed class DevbookPaneBrainModeTests
{
    private const string TwoAreas = "## Areas\n\n- Work — `work`\n- Home life — `home`\n";

    [Fact]
    public async Task Nothing_pressed_shows_the_brain_with_its_badge_path_and_declared_areas()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, TwoAreas);
        harness.WriteNote("work/standup.md", "# Standup\n\nWhat happened today.\n");
        harness.WriteNote("home/garden.md", "# Garden\n\nTomatoes.\n");

        var component = harness.RenderBrain();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(["Work", "Home life"], DevbookPaneBrainHarness.Tabs(component));
            Assert.Contains("What happened today.", component.Markup, StringComparison.Ordinal);
        });

        var badge = component.Find("[data-testid='devbook-brain-badge']");
        Assert.Equal("Brain", badge.TextContent.Trim());
        Assert.Contains("badge--brain", badge.ClassList);
        Assert.Equal("Devbook", component.Find("[data-testid='devbook-pane-title']").TextContent.Trim());
        Assert.Equal(harness.BrainFolder, component.Find("[data-testid='devbook-brain-path']").TextContent.Trim());
        Assert.Equal("Brain devbook", component.Find("[data-testid='devbook-stack']").GetAttribute("aria-label"));

        // The titled strip, which lets the tab list wrap onto a line of its own
        // rather than squeezing to one tab per line beside the title row.
        Assert.Single(component.FindAll(".devbook-stack__strip.devbook-stack__strip--titled"));

        // The path stands where the branch picker would, and the picker is gone.
        Assert.Empty(component.FindAll("[data-testid='devbook-source-select']"));
    }

    [Fact]
    public async Task With_no_areas_declared_the_seven_para_areas_are_the_tabs()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote("projects/plan.md", "# Plan\n");

        var component = harness.RenderBrain();

        component.WaitForAssertion(() => Assert.Equal(
            ["Inbox", "Projects", "Areas", "Resources", "People", "Journal", "Archive"],
            DevbookPaneBrainHarness.Tabs(component)));
    }

    /// <summary>The menu under a tab lists that area folder's notes, and selecting
    /// one renders it — through the read view the repository chapters use.</summary>
    [Fact]
    public async Task Selecting_a_note_in_the_menu_renders_it()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/alpha.md", "# Alpha\n\nFirst note.\n");
        harness.WriteNote("work/beta.md", "# Beta\n\nSecond note.\n");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains("First note.", component.Markup, StringComparison.Ordinal));

        var beta = component.FindAll(".devbook-menu__item").Single(item => item.TextContent.Contains("Beta", StringComparison.Ordinal));
        beta.Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Second note.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal);
            Assert.Equal("work/beta.md", component.Find("[data-testid='brain-note-file'] .file-view__path").TextContent.Trim());
        });
        Assert.NotEmpty(component.FindComponents<MarkdownView>());
    }

    /// <summary>"Journal sorts newest first" (the Devbook vs Brain board): the
    /// journal tab lists today's entry on top and opens on it, while every other
    /// area keeps its order by name.</summary>
    [Fact]
    public async Task The_journal_tab_lists_and_opens_the_newest_entry_first()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Journal — `journal`\n");
        harness.WriteNote("journal/2026-10-01.md", "# October first\n\nOlder entry.\n");
        harness.WriteNote("journal/2026-10-13.md", "# October thirteenth\n\nNewest entry.\n");
        harness.WriteNote("journal/2026-09-30.md", "# September thirtieth\n\nOldest entry.\n");

        var component = harness.RenderBrain();

        component.WaitForAssertion(() =>
            Assert.Contains("Newest entry.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal));
        Assert.Equal(
            ["2026 10 13", "2026 10 01", "2026 09 30"],
            component.FindAll(".devbook-menu__item--file").Select(item => item.TextContent.Trim()));
    }

    /// <summary>None of what a repository devbook carries — a version to check, a
    /// sync verdict, a scenario mark or its legend, the editor launch — is drawn, and the
    /// repository source behind them is not even asked. Search is drawn: it asks
    /// the Brain and every repository at once, so it is the same search on either
    /// side. The atlas is offered, as the Brain's own map. Drawing either toggle
    /// asks the repositories nothing.</summary>
    [Fact]
    public async Task The_repository_only_chrome_is_absent_and_its_services_are_not_asked()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, TwoAreas);
        harness.WriteNote("work/standup.md", "# Standup\n\nToday.\n");
        harness.RepositoryFolders.Reset();

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains("Today.", component.Markup, StringComparison.Ordinal));

        Assert.Empty(component.FindAll("[data-testid='devbook-update-action']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-update-behind']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-update-status']"));
        Assert.NotEmpty(component.FindAll("[data-testid='devbook-search-toggle']"));
        Assert.Single(component.FindAll("[data-testid='devbook-atlas-toggle']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-atlas-all']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-scenario-mark']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-scenario-legend']"));
        Assert.Empty(component.FindComponents<DevbookSyncVerdictBadges>());
        Assert.Empty(component.FindAll("[data-testid='devbook-open-vscode-button']"));
        Assert.Equal(0, harness.RepositoryFolders.Calls);
    }

    /// <summary>A note written while the pane is open shows once the Brain says it
    /// changed — the watch raises that after its rebuild — without a remount.</summary>
    [Fact]
    public async Task A_brain_change_reloads_the_open_pane()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/standup.md", "# Standup\n");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".devbook-menu__item--file")));

        harness.WriteNote("work/retro.md", "# Retro\n");
        await Task.Run(harness.Brain.NotifyContentChanged, TestContext.Current.CancellationToken);

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".devbook-menu__item--file").Count));
    }

    /// <summary>A repository anchor is the pane as it always was, a Brain composed
    /// beside it or not: no badge, no title, the branch picker in its place.</summary>
    [Fact]
    public async Task A_repository_anchor_shows_that_repository_unchanged()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote("projects/plan.md", "# Plan\n");

        var component = harness.Render(harness.RepositoryAlias);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-badge']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-pane-title']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-path']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-conflict']"));
        Assert.NotEmpty(component.FindAll("[data-testid='devbook-source-select']"));
        Assert.Equal("Repository devbook", component.Find("[data-testid='devbook-stack']").GetAttribute("aria-label"));

        // The strip keeps its repository-only layout: no titled modifier.
        Assert.Empty(component.FindAll(".devbook-stack__strip--titled"));
        Assert.DoesNotContain("Projects", DevbookPaneBrainHarness.Tabs(component));
    }

    /// <summary>A Brain area folder may share a repository area's name. Its key is
    /// namespaced, so none of the repository's special cases — the domain context
    /// map first, the design overview row — reaches it.</summary>
    [Fact]
    public async Task A_brain_area_named_like_a_repository_area_gets_none_of_its_special_cases()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Design — `design`\n");
        harness.WriteNote("design/alpha.md", "# Alpha\n\nAlpha body.\n");
        harness.WriteNote("design/context-map.md", "# Map\n");

        var component = harness.RenderBrain();

        component.WaitForAssertion(() => Assert.Contains("Alpha body.", component.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(component.FindAll(".devbook-menu__item"), item => item.TextContent.Contains("All documents", StringComparison.Ordinal));
        Assert.Empty(component.FindComponents<DesignDevbookView>());
    }
}
