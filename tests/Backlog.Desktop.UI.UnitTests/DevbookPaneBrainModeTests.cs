using Backlog.Infrastructure.Copilot;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

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
        await using var harness = DevbookPaneBrainHarness.Create(branchOverview: true);
        GiveTheRepositoryItsChrome(harness);
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

        // No branch overview either: a Brain has no branches to list. A load once
        // started reads as loading at once, so its absence is not a race.
        var overview = harness.Context.Services.GetRequiredService<DevbookBranchOverview>();
        Assert.False(overview.IsLoading(harness.RepositoryAlias));
        Assert.Null(overview.Current(harness.RepositoryAlias));
        Assert.Equal(0, harness.Catalog.HeadListings);
    }

    /// <summary>The control for the test above: the same harness, the same
    /// repository content, on the repository instead — and the chrome the Brain
    /// leaves out is drawn. Without it, the absence above could be a harness that
    /// never produces that chrome at all.</summary>
    [Fact]
    public async Task On_the_repository_the_same_harness_draws_the_repository_only_chrome()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        GiveTheRepositoryItsChrome(harness);
        harness.WriteNote(BrainConventions.FileName, TwoAreas);

        var component = harness.Render(harness.RepositoryAlias);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));
        Assert.Single(component.FindAll("[data-testid='devbook-update-action']"));
        Assert.NotEmpty(component.FindAll("[data-testid='devbook-source-select']"));
        Assert.NotEmpty(component.FindAll("[data-testid='devbook-open-vscode-button']"));

        // The pane opens on Instructions, which carries no chapter verdicts; the
        // architecture tab opens its first chapter, and that one has a verdict.
        component.Find(".devbook-stack__nav-item[aria-controls='tabpanel-arc42']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindComponents<DevbookSyncVerdictBadges>()));

        component.Find(".devbook-stack__nav-item[aria-controls='tabpanel-domain']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-scenario-mark']")));
    }

    /// <summary>What a repository needs before its chrome has anything to draw: a
    /// scenario page in its domain folder, and a sync verdict on every chapter.</summary>
    private static void GiveTheRepositoryItsChrome(DevbookPaneBrainHarness harness)
    {
        var page = Path.Combine(harness.Root, "clone", ".domain", "intake", "specs", "spec.md");
        Directory.CreateDirectory(Path.GetDirectoryName(page)!);
        File.WriteAllText(page, "# Specs\n\n```meta\ntype: scenario\n```\n\n## Done\n- **Then** it is done\n");

        harness.Context.Services.AddSingleton<IDevbookSyncVerdicts>(new EveryChapterVerdict());

        // What the domain panel the scenario page sits under asks for; the harness
        // composes no domain panel of its own.
        harness.Context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        harness.Context.Services.AddSingleton(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
    }

    /// <summary>A sweep that spoke about every file of every repository, at the
    /// file's own level, so whatever chapter the pane opens has a badge to draw.</summary>
    private sealed class EveryChapterVerdict : IDevbookSyncVerdicts
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DevbookSyncVerdict> For(string repository, string chapterPath, IReadOnlyList<DevbookFolderSetting>? folders = null) =>
            [new(repository, chapterPath, string.Empty, "aligned", "Matches the code.", chapterPath, "chapter", "aligned", "none", null, DateTimeOffset.UnixEpoch, "run-1")];

        public IReadOnlyList<DevbookSyncVerdict> ForRepository(string repository) => [];

        public Task RecordAsync(IReadOnlyList<DevbookSyncVerdict> verdicts, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
