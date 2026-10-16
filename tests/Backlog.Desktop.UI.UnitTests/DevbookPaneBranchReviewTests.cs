using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Reviewing the branch the pane reads, in the pane: a toggle on the strip only
/// for a branch, and in place of the chapters the picker, the changed chapters
/// and the comparison of the one chosen.
/// </summary>
public sealed class DevbookPaneBranchReviewTests
{
    private const string Toggle = "[data-testid='devbook-review-toggle']";

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_toggle_is_offered_for_a_branch_and_not_for_the_local_clone()
    {
        await using var clone = CreateHarness(local: true);
        var onClone = clone.Render();
        onClone.WaitForAssertion(() => Assert.NotEmpty(onClone.FindAll("[data-testid='devbook-source-select']")));
        Assert.Empty(onClone.FindAll(Toggle));

        await using var branch = CreateHarness();
        var onBranch = branch.Render();
        onBranch.WaitForAssertion(() => Assert.Equal("Review changes", onBranch.Find(Toggle).TextContent.Trim()));
    }

    /// <summary>Opening the review starts on the whole branch and its first
    /// chapter; the chapters and their menu make way for it.</summary>
    [Fact]
    public async Task Opening_the_review_shows_the_branch_its_chapters_and_the_first_comparison()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        OpenReview(component);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["intro.md", "review.md"],
                component.FindAll("[data-testid='devbook-review-files'] .changed-file__name").Select(name => name.TextContent.Trim()));
            Assert.Equal("intro.md", component.Find("[data-testid='devbook-review-compare'] .md-compare__name").TextContent.Trim());
            Assert.Equal("+1 other file not shown", component.Find("[data-testid='devbook-review-other-files']").TextContent.Trim());
        });

        Assert.Equal("Hide changes", component.Find(Toggle).TextContent.Trim());
        Assert.Empty(component.FindAll(".devbook-menu__item"));
        Assert.Equal("true", component.Find("[data-testid='devbook-review-picker-scope-branch']").GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task Choosing_a_commit_lists_the_chapters_that_commit_changed()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        OpenReview(component);
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='devbook-review-files'] .changed-file").Count));

        component.Find("[data-testid='devbook-review-picker-commit-c1aaaaaaaaaa']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["review.md"],
                component.FindAll("[data-testid='devbook-review-files'] .changed-file__name").Select(name => name.TextContent.Trim()));
            Assert.Equal("true", component.Find("[data-testid='devbook-review-picker-commit-c1aaaaaaaaaa']").GetAttribute("aria-selected"));
            Assert.Empty(component.FindAll("[data-testid='devbook-review-other-files']"));
        });
    }

    [Fact]
    public async Task Choosing_a_chapter_compares_it()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        OpenReview(component);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-review-files-added-review.md']")));

        component.Find("[data-testid='devbook-review-files-added-review.md']").Click();

        component.WaitForAssertion(() =>
        {
            var compare = component.Find("[data-testid='devbook-review-compare']");
            Assert.Equal("review.md", compare.QuerySelector(".md-compare__name")!.TextContent.Trim());
            Assert.Equal(".devbook/arc42/review.md", compare.QuerySelector(".md-compare__path")!.TextContent.Trim());
            Assert.StartsWith("main → feature", compare.QuerySelector(".md-compare__meta")!.TextContent.Trim(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task What_github_refused_is_said_in_place_of_the_review()
    {
        await using var harness = CreateHarness();
        harness.Comparison.Failure = "GitHub refused the comparison.";
        var component = harness.Render();

        OpenReview(component);

        component.WaitForAssertion(() => Assert.Equal(
            "GitHub refused the comparison.",
            component.Find("[data-testid='devbook-review-error']").TextContent.Trim()));
        Assert.Empty(component.FindAll("[data-testid='devbook-review-picker']"));
    }

    [Fact]
    public async Task Closing_the_review_goes_back_to_the_chapters()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        OpenReview(component);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-review-picker']")));

        component.Find(Toggle).Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-review']"));
            Assert.Equal("Review changes", component.Find(Toggle).TextContent.Trim());
        });
    }

    /// <summary>A review is about one repository's branch; read from somewhere
    /// else, the pane goes back to its chapters.</summary>
    [Fact]
    public async Task Switching_to_the_local_clone_closes_the_review()
    {
        await using var harness = CreateHarness(withClone: true);
        var component = harness.Render();

        OpenReview(component);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-review-picker']")));

        var input = component.Find("[data-testid='devbook-source-select'] input");
        input.Input(DevbookSourceSelection.LocalFolderLabel);
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-review']"));
            Assert.Empty(component.FindAll(Toggle));
        });
    }

    /// <summary>
    /// An answer that arrives after a newer one is dropped. The branch's head
    /// resolves only once its gate is set and ignores the cancellation, so the
    /// load for the branch that was left completes after the load for the one
    /// switched back to — and without the pane's sequence it would overwrite it.
    /// </summary>
    [Fact]
    public async Task An_answer_landing_after_a_newer_one_is_dropped()
    {
        await using var harness = CreateHarness();
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Catalog.Heads["other"] = new GitHubBranchHead("other", "othersha");
        harness.Catalog.Gates["other"] = late;
        var component = harness.Render();

        OpenReview(component);
        component.WaitForAssertion(() => Assert.StartsWith(
            "main → feature",
            component.Find("[data-testid='devbook-review-compare'] .md-compare__meta").TextContent.Trim(),
            StringComparison.Ordinal));

        Assert.Null(harness.Settings.SetDevbookSource(harness.RepositoryAlias, "other", false));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-review-loading']")));

        Assert.Null(harness.Settings.SetDevbookSource(harness.RepositoryAlias, "feature", false));
        component.WaitForAssertion(() => Assert.StartsWith(
            "main → feature",
            component.Find("[data-testid='devbook-review-compare'] .md-compare__meta").TextContent.Trim(),
            StringComparison.Ordinal));

        late.SetResult();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        component.WaitForAssertion(() => Assert.StartsWith(
            "main → feature",
            component.Find("[data-testid='devbook-review-compare'] .md-compare__meta").TextContent.Trim(),
            StringComparison.Ordinal));
    }

    /// <summary>Arrowing on before GitHub answers: the wait for the commit
    /// stepped past is cancelled, and the scope the reader moved to stays.</summary>
    [Fact]
    public async Task Stepping_on_before_github_answers_keeps_the_newer_scope()
    {
        await using var harness = CreateHarness();
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Comparison.Gates["c1aaaaaaaaaa"] = late;
        var component = harness.Render();

        OpenReview(component);
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='devbook-review-files'] .changed-file").Count));

        component.Find("[data-testid='devbook-review-picker-commit-c1aaaaaaaaaa']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-review-files-loading']")));

        component.Find("[data-testid='devbook-review-picker-scope-branch']").Click();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='devbook-review-files'] .changed-file").Count));

        late.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["intro.md", "review.md"],
                component.FindAll("[data-testid='devbook-review-files'] .changed-file__name").Select(name => name.TextContent.Trim()));
            Assert.Equal("true", component.Find("[data-testid='devbook-review-picker-scope-branch']").GetAttribute("aria-selected"));
        });
    }

    /// <summary>The source moved somewhere other than the pane — Settings, say —
    /// while the review was open: the review follows it rather than going on
    /// showing the branch that was left.</summary>
    [Fact]
    public async Task A_source_switched_outside_the_pane_reloads_the_review()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        OpenReview(component);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-review-picker']")));

        Assert.Null(harness.Settings.SetDevbookSource(harness.RepositoryAlias, "gone", false));

        component.WaitForAssertion(() => Assert.Equal(
            "The branch gone is not on GitHub any more.",
            component.Find("[data-testid='devbook-review-error']").TextContent.Trim()));
    }

    [Fact]
    public async Task Moving_to_another_repository_closes_the_review()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        OpenReview(component);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-review-picker']")));

        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, null));

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='devbook-review']")));
    }

    private static void OpenReview(IRenderedComponent<DevbookPane> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(Toggle)));
        component.Find(Toggle).Click();
    }

    /// <param name="local">Read the local clone rather than the branch.</param>
    /// <param name="withClone">Give the repository a clone while still reading
    /// the branch, so the source can be switched to it.</param>
    private static Harness CreateHarness(bool local = false, bool withClone = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-review-tests", Guid.NewGuid().ToString("n"));
        var clone = Path.Combine(root, "clone");
        Write(clone, ".arc42/01-introduction-and-goals.md", "# Introduction\n");

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = featureSettings.SetEnabled(DevbookFeatures.RepositoryDevbook, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = local || withClone ? clone : null,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([repository]));
        Assert.Null(gitHubSettings.SetDevbookSource(repository.Alias, "feature", local));

        var catalog = new StubBranchCatalog
        {
            Heads =
            {
                [string.Empty] = new GitHubBranchHead("main", "mainsha"),
                ["feature"] = new GitHubBranchHead("feature", "headsha")
            }
        };

        var comparison = new StubBranchComparison
        {
            Comparison = new GitHubComparison(
                "base0",
                AheadBy: 2,
                BehindBy: 0,
                TotalCommits: 2,
                Commits:
                [
                    new GitHubComparedCommit("c1aaaaaaaaaa", "base0", "Add the review chapter", "Job", Now.AddHours(-2)),
                    new GitHubComparedCommit("c2bbbbbbbbbb", "c1aaaaaaaaaa", "Edit the intro", "Ada", Now.AddMinutes(-30))
                ],
                Files:
                [
                    new GitHubChangedFile(".devbook/arc42/intro.md", null, GitHubFileStatus.Modified, "intro-after"),
                    new GitHubChangedFile(".devbook/arc42/review.md", null, GitHubFileStatus.Added, "review-after"),
                    new GitHubChangedFile("src/Program.cs", null, GitHubFileStatus.Modified, "cs")
                ])
        };
        comparison.Commits["c1aaaaaaaaaa"] = new GitHubCommitChange(
            comparison.Comparison.Commits[0],
            [new GitHubChangedFile(".devbook/arc42/review.md", null, GitHubFileStatus.Added, "review-after")]);

        var trees = new StubTreeClient();
        trees.Trees["base0"] = new(StringComparer.Ordinal) { [".devbook/arc42/intro.md"] = "intro-before" };
        trees.Blobs["intro-before"] = "# Intro\n\nOld words.\n";
        trees.Blobs["intro-after"] = "# Intro\n\nNew words.\n";
        trees.Blobs["review-after"] = "# Review\n\nA new chapter.\n";

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHubSettings, store));
        context.Services.AddSingleton<ILocalGitRepositoryService>(new ScriptedGitRepositoryService());
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(catalog);
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton(new DevbookBranchReview(gitHubSettings, catalog, comparison, trees, new FakeTimeProvider(Now)));
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<DevbookChapterWriter>();

        return new Harness(root, context, repository.Alias, comparison, gitHubSettings, catalog);
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed record Harness(
        string Root,
        BunitContext Context,
        string RepositoryAlias,
        StubBranchComparison Comparison,
        GitHubSettingsStore Settings,
        StubBranchCatalog Catalog) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
