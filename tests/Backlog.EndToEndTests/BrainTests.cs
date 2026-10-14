using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Backlog.EndToEndTests;

/// <summary>
/// The Brain's core path on the desktop harness, end to end: first use and Create
/// conventions, Brain scope narrowing, the pane's areas, Capture, To Brain, a
/// <c>repo:</c> link and back, and the scope surviving a restart. Each test proves
/// the requirements under <c>.devbook/domain/devbook/requirements.md</c> and
/// <c>.devbook/domain/inbox/requirements.md#route-to-brain</c> that list it.
///
/// <para>Every test brings its own Brain folder under the temp folder, outside every
/// repository, and puts the harness's setting back afterwards
/// (<see cref="BrainScenario"/>). They share one harness and its remembered scope, so
/// they run one at a time and apart from the other end-to-end tests.</para>
/// </summary>
[Collection(BrainCollection.Name)]
public sealed class BrainTests
{
    private const string ScopeAdr = ".devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md";

    /// <summary>A sentence of ADR 0027, so the pane is known to show that chapter.</summary>
    private const string ScopeAdrSentence = "Links run from the Brain into a repository, never back";

    private static readonly string[] ParaFolders = ["_inbox", "projects", "areas", "resources", "people", "journal", "archive"];

    /// <summary>
    /// First use: with no folder set the chip reads <i>Set up your Brain</i>, still
    /// scopes, and the pane asks for a folder. A folder typed into the Brain card
    /// is kept in the app's settings and stays empty; Create conventions then
    /// scaffolds it, and the chip reads <i>Brain</i>.
    /// </summary>
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task First_use_asks_for_a_folder_and_create_conventions_scaffolds_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await BrainScenario.OpenAsync("first-use", cancellationToken);
        var desktop = scenario.Desktop;

        await desktop.OpenBrainCardAsync();
        await desktop.SetBrainFolderAsync("");

        await desktop.ShowAllTasksAsync();
        await desktop.ClearScopeAsync();
        await Expect(desktop.BrainChip).ToHaveTextAsync("Set up your Brain");
        await desktop.PressScopeChipAsync(desktop.BrainChip);
        await desktop.ShowDevbookAsync();
        await Expect(desktop.Page.GetByTestId("devbook-brain-first-use")).ToBeVisibleAsync();
        await Expect(desktop.Page.GetByTestId("devbook-brain-set-up")).ToHaveAttributeAsync("href", "settings?page=storage#brain-folder");
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "first-use-pane-asks-for-a-folder");

        await scenario.UseFolderAsync();
        Assert.Empty(Directory.EnumerateFileSystemEntries(scenario.Folder));
        var rows = await desktop.BrainCardRowsAsync();
        Assert.Contains("The search database is built in this app's storage and never synced.", rows, StringComparison.Ordinal);
        Assert.Contains("CONVENTIONS.md missing", rows, StringComparison.Ordinal);
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "card-folder-picked-and-still-empty");

        var written = await desktop.CreateConventionsAsync();
        Assert.Contains("README.md", written, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(scenario.Folder, "README.md")));
        Assert.True(File.Exists(Path.Combine(scenario.Folder, "CONVENTIONS.md")));
        Assert.Contains("index: root", await File.ReadAllTextAsync(Path.Combine(scenario.Folder, "README.md"), cancellationToken), StringComparison.Ordinal);
        Assert.All(ParaFolders, folder => Assert.True(Directory.Exists(Path.Combine(scenario.Folder, folder)), $"{folder} was not scaffolded"));
        Assert.Empty(Directory.EnumerateFiles(scenario.Folder, "*.db*", SearchOption.AllDirectories));
        await Expect(desktop.CreateConventionsButton).ToHaveCountAsync(0);
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "card-after-create-conventions");

        await desktop.ShowAllTasksAsync();
        await Expect(desktop.BrainChip).ToHaveTextAsync("Brain");
    }

    /// <summary>
    /// The Brain chip leads the scope behind a divider. Pressed alone it lists only
    /// rows no listed repository claims; Ctrl-pressed beside <c>backlog</c> it keeps
    /// both, with <c>backlog</c> the anchor; pressed without a modifier it is alone
    /// again.
    /// </summary>
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task The_brain_chip_narrows_the_tasks_to_those_filed_against_no_repository()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await BrainScenario.OpenAsync("scope", cancellationToken);
        var desktop = scenario.Desktop;
        await scenario.UseFolderAsync();
        await desktop.CreateConventionsAsync();

        await desktop.ShowAllTasksAsync();
        await desktop.ClearScopeAsync();

        var order = await desktop.Page.Locator(".app-header__repository-scope > *").EvaluateAllAsync<string[]>(
            "nodes => nodes.map(n => n.dataset.testid ?? n.className)");
        Assert.Equal("brain-scope-option", order[0]);
        Assert.Equal("brain-scope-divider", order[1]);
        Assert.All(order.Skip(2), testId => Assert.Equal("repository-filter-option", testId));

        var everything = await desktop.TaskIdsAsync();

        await desktop.PressScopeChipAsync(desktop.BrainChip);
        var brain = await desktop.TaskIdsAsync();
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "brain-alone");

        await desktop.PressScopeChipAsync(desktop.RepositoryChip("backlog"));
        await Expect(desktop.BrainChip).ToHaveAttributeAsync("aria-pressed", "false");
        var backlog = await desktop.TaskIdsAsync();

        await desktop.PressScopeChipAsync(desktop.BrainChip, ctrl: true);
        await Expect(desktop.RepositoryChip("backlog")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(desktop.RepositoryChip("backlog")).ToHaveAttributeAsync("aria-current", "true");
        var both = await desktop.TaskIdsAsync();
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "backlog-and-brain");

        Assert.NotEmpty(brain);
        Assert.NotEmpty(backlog);
        Assert.True(brain.Count < everything.Count, $"Brain alone listed {brain.Count} of {everything.Count} rows.");
        Assert.Empty(brain.Intersect(backlog));
        Assert.True(brain.IsSubsetOf(both) && backlog.IsSubsetOf(both), "Brain beside backlog did not keep the rows of both.");

        // A plain press on a pressed-beside chip makes it the only one.
        await desktop.BrainChip.ClickAsync();
        await Interactive.EventuallyAsync(
            async () => await desktop.RepositoryChip("backlog").GetAttributeAsync("aria-pressed") == "false"
                        && await desktop.BrainChip.GetAttributeAsync("aria-pressed") == "true",
            TimeSpan.FromSeconds(15),
            "a plain press to leave Brain alone");
    }

    /// <summary>
    /// The pane on the Brain: with nothing pressed it shows the Brain with its badge
    /// and path, the tabs are the areas <c>CONVENTIONS.md</c> declares in their
    /// order, the journal lists and opens its newest entry first, and a repository
    /// anchor puts the pane on that repository's devbook.
    /// </summary>
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task The_pane_shows_the_declared_areas_and_the_journal_newest_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await BrainScenario.OpenAsync("areas", cancellationToken);
        var desktop = scenario.Desktop;
        scenario.Write("CONVENTIONS.md", """
            # Conventions

            ## Areas

            | Area | Folder | Holds |
            |---|---|---|
            | Inbox | `_inbox` | Notes not yet filed. |
            | Work | `work` | The work. |
            | Home life | `home` | Everything else. |
            | Journal | `journal` | Dated entries. |
            """);
        scenario.Write("work/plan.md", "# Plan\n\nThe plan.\n");
        foreach (var day in new[] { "2026-09-30", "2026-10-01", "2026-10-13" })
        {
            scenario.Write($"journal/{day}.md", $"# {day}\n\nEntry for {day}.\n");
        }

        await scenario.UseFolderAsync();
        await desktop.ShowAllTasksAsync();
        await desktop.ClearScopeAsync();
        await desktop.ShowDevbookAsync();

        await Expect(desktop.BrainBadge).ToHaveTextAsync("Brain");
        await Expect(desktop.Page.GetByTestId("devbook-brain-path")).ToHaveTextAsync(scenario.Folder);
        Assert.Equal(["Inbox", "Work", "Home life", "Journal"], await desktop.BrainAreasAsync());
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "declared-areas");

        await desktop.OpenAreaAsync("Journal");
        await Interactive.EventuallyAsync(
            async () => (await desktop.AreaFilesAsync()).Count == 3,
            TimeSpan.FromSeconds(15),
            "the journal's three entries");
        Assert.Equal(["2026 10 13", "2026 10 01", "2026 09 30"], await desktop.AreaFilesAsync());
        await Expect(desktop.BrainNote("2026-10-13")).ToBeVisibleAsync();
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "journal-newest-first");

        await desktop.PressScopeChipAsync(desktop.RepositoryChip("backlog"));
        await Expect(desktop.BrainBadge).ToHaveCountAsync(0);
        await Expect(desktop.DevbookPane.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Architecture", Exact = true })).ToBeVisibleAsync();
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "backlog-anchor-shows-its-devbook");
    }

    /// <summary>
    /// Capture from the pane writes a note into the Brain's inbox area with its
    /// title, its body and one <c>meta</c> block, opens it, and a fence typed into
    /// the body stays text.
    /// </summary>
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task Capture_writes_a_note_into_the_inbox_area_and_typed_structure_stays_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await BrainScenario.OpenAsync("capture", cancellationToken);
        var desktop = scenario.Desktop;
        await scenario.UseFolderAsync();
        await desktop.CreateConventionsAsync();

        await desktop.ShowAllTasksAsync();
        await desktop.ClearScopeAsync();
        await desktop.ShowDevbookAsync();
        await Expect(desktop.BrainBadge).ToBeVisibleAsync();

        var title = $"Ideas for the sync {scenario.Token}";
        await desktop.CaptureIntoBrainAsync(title, "Batch the pulls.\n```meta\ntype: not-a-note\n```");
        await Expect(desktop.Page.GetByTestId("brain-note-file")).ToContainTextAsync("_inbox/");
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "captured-note-open");

        var note = Assert.Single(scenario.NotesIn("_inbox"));
        var text = await File.ReadAllTextAsync(note, cancellationToken);
        Assert.StartsWith($"# {title}", text, StringComparison.Ordinal);
        Assert.Contains("Batch the pulls.", text, StringComparison.Ordinal);
        var fences = text.Split('\n').Count(line => line.TrimEnd('\r') == "```meta");
        Assert.Equal(1, fences);
        Assert.Contains("type: not-a-note", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An item captured in the desktop Inbox and routed To Brain becomes a note in
    /// the Brain's inbox area with its title, and the Inbox says where it went.
    /// </summary>
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task To_brain_writes_an_inbox_item_as_a_note_in_the_inbox_area()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await BrainScenario.OpenAsync("to-brain", cancellationToken);
        var desktop = scenario.Desktop;
        await scenario.UseFolderAsync();
        await desktop.CreateConventionsAsync();
        await desktop.EnableInboxAsync();

        var title = $"Standup {scenario.Token}";
        await desktop.CaptureIntoInboxAsync(title);
        await desktop.OpenInboxItemAsync(title);
        var said = await desktop.RouteOpenItemToBrainAsync();
        Assert.Contains(title, said, StringComparison.Ordinal);
        Assert.Contains("_inbox/", said, StringComparison.Ordinal);
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "routed-to-brain");

        var note = Assert.Single(scenario.NotesIn("_inbox"));
        Assert.StartsWith($"# {title}", await File.ReadAllTextAsync(note, cancellationToken), StringComparison.Ordinal);
    }

    /// <summary>
    /// A note's gold <c>repo:backlog/…</c> chip opens that chapter of
    /// <c>backlog</c>'s devbook and leaves the scope on the Brain; pressing the
    /// Brain chip then brings the Brain back to the pane.
    /// </summary>
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task A_repo_link_opens_the_repository_chapter_and_the_brain_chip_brings_the_brain_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await BrainScenario.OpenAsync("repo-link", cancellationToken);
        var desktop = scenario.Desktop;
        scenario.Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder | Holds |\n|---|---|---|\n| Work | `work` | The work. |\n");
        scenario.Write("work/plan.md", $"# Plan\n\nSee [the scope decision](repo:backlog/{ScopeAdr}).\n");

        await scenario.UseFolderAsync();
        await desktop.ShowAllTasksAsync();
        await desktop.ClearScopeAsync();
        await desktop.PressScopeChipAsync(desktop.BrainChip);
        await desktop.ShowDevbookAsync();
        await desktop.OpenAreaAsync("Work");
        await Expect(desktop.BrainNote("Plan")).ToBeVisibleAsync();
        await Expect(desktop.Page.GetByTestId("brain-repo-link")).ToHaveAttributeAsync("data-resolved", "true");
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "note-with-gold-chip");

        await desktop.FollowRepoLinkAsync();
        await Expect(desktop.DevbookPane).ToContainTextAsync(ScopeAdrSentence);
        await Expect(desktop.BrainBadge).ToHaveCountAsync(0);
        await Expect(desktop.BrainChip).ToHaveAttributeAsync("aria-pressed", "true");
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "repository-chapter-scope-kept");

        await desktop.BrainChip.ClickAsync();
        await Expect(desktop.BrainBadge).ToHaveTextAsync("Brain");
        await Expect(desktop.DevbookPane).Not.ToContainTextAsync(ScopeAdrSentence);
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "brain-back");
    }

    /// <summary>
    /// The scope comes back after the harness restarts: Brain pressed first and
    /// <c>backlog</c> Ctrl-pressed beside it reopen as both pressed with the Brain
    /// the anchor.
    /// </summary>
    [Fact(Timeout = 15 * 60 * 1000)]
    public async Task The_scope_survives_a_restart_with_the_brain_as_its_anchor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await BrainScenario.OpenAsync("restart", cancellationToken);
        var desktop = scenario.Desktop;
        await scenario.UseFolderAsync();
        await desktop.CreateConventionsAsync();

        await desktop.ShowAllTasksAsync();
        await desktop.ClearScopeAsync();
        await desktop.PressScopeChipAsync(desktop.BrainChip);
        await desktop.PressScopeChipAsync(desktop.RepositoryChip("backlog"), ctrl: true);
        await Expect(desktop.BrainChip).ToHaveAttributeAsync("aria-current", "true");
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "before-restart");

        await scenario.AppHost.StopAsync(DesktopHarness.Resource, cancellationToken);
        await scenario.AppHost.StartAsync(DesktopHarness.Resource, cancellationToken);
        await scenario.ReopenAsync(cancellationToken);
        desktop = scenario.Desktop;

        await desktop.ShowTasksAsync();
        await Expect(desktop.BrainChip).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 30_000 });
        await Expect(desktop.BrainChip).ToHaveAttributeAsync("aria-current", "true");
        await Expect(desktop.RepositoryChip("backlog")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(desktop.RepositoryChip("backlog")).Not.ToHaveAttributeAsync("aria-current", "true");
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "after-restart");
    }
}

/// <summary>The Brain tests share the harness's Brain setting and its remembered
/// scope, so they run one at a time and never beside another test class.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BrainCollection
{
    public const string Name = "Brain on the desktop harness";
}
