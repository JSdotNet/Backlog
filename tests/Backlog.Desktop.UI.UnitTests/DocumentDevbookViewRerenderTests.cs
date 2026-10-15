using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The document-folder view (Design, AI) reads its open chapter off disk and
/// re-reads the chapter's <c>sync</c> state with it. The pane hands the view its
/// parameters again on every render of the shell — every keystroke typed into a
/// task beside it — so a read per parameter set was a file read and a parse per
/// keystroke. The chapter is read once per folder load and selection, and again
/// on whatever reloads the folder.
/// <para>
/// Observed through the folder source: every chapter read asks it where the
/// folder is for the sync reading, and a parameter set that changes nothing must
/// ask it nothing.
/// </para>
/// </summary>
public sealed class DocumentDevbookViewRerenderTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_parent_render_with_unchanged_parameters_reads_no_chapter()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("backlog", "color-scheme.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));
        var reads = harness.Folders.ChapterReads;

        for (var render = 0; render < 3; render++)
        {
            harness.Render(component, "backlog", "color-scheme.md");
        }

        await component.InvokeAsync(() => { });
        Assert.Equal(reads, harness.Folders.ChapterReads);
    }

    [Fact]
    public async Task Selecting_another_chapter_reads_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("backlog", "color-scheme.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));
        var reads = harness.Folders.ChapterReads;

        harness.Render(component, "backlog", "interaction-guidelines.md");

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Every control answers within a frame.", component.Markup, StringComparison.Ordinal);
            Assert.Equal(reads + 1, harness.Folders.ChapterReads);
        });
    }

    [Fact]
    public async Task A_folder_change_reads_the_open_chapter_again()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("backlog", "color-scheme.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));
        var reads = harness.Folders.ChapterReads;

        File.WriteAllText(Path.Combine(harness.Backlog, "color-scheme.md"), "# Colors\n\nThe palette, rewritten elsewhere.\n");
        await Task.Run(harness.Folders.NotifyContentChanged, TestContext.Current.CancellationToken);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("The palette, rewritten elsewhere.", component.Markup, StringComparison.Ordinal);
            Assert.True(harness.Folders.ChapterReads > reads);
        });
    }

    [Fact]
    public async Task Leaving_the_editor_reads_the_chapter_again()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("backlog", "color-scheme.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));
        await component.InvokeAsync(() => component.Find("[data-testid='design-chapter-file-edit']").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-done']")));
        var reads = harness.Folders.ChapterReads;

        await component.InvokeAsync(() => component.Find("[data-testid='design-chapter-file-done']").Click());

        component.WaitForAssertion(() =>
        {
            Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']"));
            Assert.True(harness.Folders.ChapterReads > reads);
        });
    }

    [Fact]
    public async Task Another_repository_reads_its_own_chapter()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("backlog", "color-scheme.md");
        component.WaitForAssertion(() => Assert.Contains("The palette and how it is applied.", component.Markup, StringComparison.Ordinal));
        var reads = harness.Folders.ChapterReads;

        harness.Render(component, "docs", "color-scheme.md");

        component.WaitForAssertion(() =>
        {
            Assert.Contains("The documentation site's palette.", component.Markup, StringComparison.Ordinal);
            Assert.Equal(reads + 1, harness.Folders.ChapterReads);
        });
    }

    /// <summary>
    /// The editor saves the chapter as it is typed, and every save reads the chapter
    /// again. That read is of the file the reader is typing into, and nothing they
    /// asked for is loading, so it never starts the wait that marks the chapter busy:
    /// a dimmed field on every autosave, announced as busy, is the flash
    /// interaction-guidelines.md#timing forbids.
    /// </summary>
    [Fact]
    public async Task An_autosave_of_the_chapter_being_edited_never_marks_it_busy()
    {
        var clock = new LoaderDelayClock();
        await using var harness = CreateHarness(clock);

        var component = harness.Render("backlog", "color-scheme.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));
        await component.InvokeAsync(() => component.Find("[data-testid='design-chapter-file-edit']").Click());
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("textarea")));
        var reads = harness.Folders.ChapterReads;
        var waits = clock.LoaderWaits;

        component.Find("textarea").Input("# Colors\n\nTyped, and saved on the way out of the field.\n");
        component.Find("textarea").Blur();

        component.WaitForAssertion(() => Assert.True(harness.Folders.ChapterReads > reads), TimeSpan.FromSeconds(5));
        Assert.Equal(waits, clock.LoaderWaits);
        Assert.Null(component.Find("article.folder-document").GetAttribute("aria-busy"));
        Assert.NotEmpty(component.FindAll("textarea"));
    }

    /// <summary>A chapter the reader picked is a load they asked for: it starts the
    /// wait, and a read that outlasts it marks the chapter busy.</summary>
    [Fact]
    public async Task Selecting_another_chapter_starts_the_wait_for_its_loader()
    {
        var clock = new LoaderDelayClock();
        await using var harness = CreateHarness(clock);

        var component = harness.Render("backlog", "color-scheme.md");
        component.WaitForAssertion(() => Assert.Contains("The palette and how it is applied.", component.Markup, StringComparison.Ordinal));
        var waits = clock.LoaderWaits;

        harness.Render(component, "backlog", "interaction-guidelines.md");

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Every control answers within a frame.", component.Markup, StringComparison.Ordinal);
            Assert.Equal(waits + 1, clock.LoaderWaits);
        });

        // The read landed well inside the wait, so the mark never went up.
        Assert.Null(component.Find("article.folder-document").GetAttribute("aria-busy"));
        Assert.Empty(component.FindAll("[data-testid='design-chapter-file-refreshing']"));
    }

    /// <summary>The system clock, counting the waits of exactly the length a region
    /// loader waits before it shows (interaction-guidelines.md#timing).</summary>
    private sealed class LoaderDelayClock : TimeProvider
    {
        private static readonly TimeSpan LoaderDelay = TimeSpan.FromMilliseconds(300);
        private int _waits;

        public int LoaderWaits => Volatile.Read(ref _waits);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == LoaderDelay) Interlocked.Increment(ref _waits);
            return System.CreateTimer(callback, state, dueTime, period);
        }
    }

    private Harness CreateHarness(TimeProvider? clock = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-document-view-rerender-tests", Guid.NewGuid().ToString("n"));
        _roots.Add(root);

        var backlog = WriteRepository(Path.Combine(root, "backlog"), "The palette and how it is applied.");
        var docs = WriteRepository(Path.Combine(root, "docs"), "The documentation site's palette.");

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog\ndocs = JSdotNet/Backlog-docs");
        Assert.Empty(errors);
        Assert.Null(gitHubSettings.SetRepositories(repositories));
        gitHubSettings.SetCloneDirectory("backlog", Path.GetDirectoryName(backlog)!);
        gitHubSettings.SetCloneDirectory("docs", Path.GetDirectoryName(docs)!);

        var folders = new ChapterReadCountingFolderSource(new DevbookFolderSource(gitHubSettings));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IDevbookFolderSource>(folders);
        context.Services.AddSingleton<DesignDevbookProvider>();
        context.Services.AddSingleton<DevbookChapterWriter>();
        if (clock is not null) context.Services.AddSingleton(clock);

        return new Harness(context, folders, backlog);
    }

    /// <summary>A repository with a <c>.design</c> folder of two documents; the
    /// colour scheme's summary says which repository it is.</summary>
    private static string WriteRepository(string repository, string colorsSummary)
    {
        var design = Path.Combine(repository, ".design");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "color-scheme.md"), $"# Colors\n\n{colorsSummary}\n\n## Palette\n\nThe tokens.\n");
        File.WriteAllText(Path.Combine(design, "interaction-guidelines.md"), "# Interaction guidelines\n\nEvery control answers within a frame.\n");
        return design;
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(BunitContext Context, ChapterReadCountingFolderSource Folders, string Backlog) : IAsyncDisposable
    {
        public IRenderedComponent<DesignDevbookView> Render(string repositoryAlias, string? selectedPath) =>
            Context.Render<DesignDevbookView>(parameters => parameters
                .Add(view => view.RepositoryAlias, repositoryAlias)
                .Add(view => view.SelectedPath, selectedPath));

        public void Render(IRenderedComponent<DesignDevbookView> component, string repositoryAlias, string? selectedPath) =>
            component.Render(parameters => parameters
                .Add(view => view.RepositoryAlias, repositoryAlias)
                .Add(view => view.SelectedPath, selectedPath));

        /// <summary>Awaited, because the editing surface writes its last pending
        /// save on the way out.</summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
