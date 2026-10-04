using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// <see cref="DevbookReferenceResolver"/>: what a task's Devbook reference points
/// at, one test per state, from the database and — where there is none — from
/// the Markdown.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class DevbookReferenceResolverTests : IDisposable
{
    private const string Alias = "backlog";

    private const string TasksPage = ".devbook/domain/tasks/domain.md";

    private const string TasksMarkdown = """
        # Tasks

        ```meta
        status: active
        ```

        What a task is.

        ## Task item

        ```meta
        status: draft
        ```

        The aggregate.

        ### Sub-items

        The steps.
        """;

    private const string BuildingBlocksPage = ".devbook/arc42/05-building-block-view.md";

    private const string BuildingBlocksMarkdown = """
        # Building block view

        ## Level 1

        The whole.
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-devbook-references", Guid.NewGuid().ToString("N"));

    public DevbookReferenceResolverTests()
    {
        Write(TasksPage, TasksMarkdown);
        Write(BuildingBlocksPage, BuildingBlocksMarkdown);
    }

    // --- from the database ----------------------------------------------------

    [Fact]
    public async Task A_chapter_answers_with_its_heading_and_status()
    {
        BuildDatabase();

        var answer = await ResolveOne($"{TasksPage}#task-item");

        Assert.Equal(DevbookReferenceState.Chapter, answer.State);
        Assert.Equal("Task item", answer.Title);
        Assert.Equal("draft", answer.Status);
        Assert.Equal(TasksPage, answer.Path);
        Assert.Equal("task-item", answer.Anchor);
        Assert.Equal("domain", answer.Folder);
        Assert.False(answer.IsBroken);
    }

    [Fact]
    public async Task A_page_answers_with_its_title_and_no_status()
    {
        BuildDatabase();

        var answer = await ResolveOne(TasksPage);

        Assert.Equal(DevbookReferenceState.Page, answer.State);
        Assert.Equal("Tasks", answer.Title);
        Assert.Null(answer.Status);
        Assert.Null(answer.Anchor);
        Assert.Equal("domain", answer.Folder);
    }

    /// <summary>The database is asked first: a file deleted since the build still
    /// answers from its rows, which is what makes the answer cheap.</summary>
    [Fact]
    public async Task The_database_answers_before_the_file_is_read()
    {
        BuildDatabase();
        File.Delete(FullPath(TasksPage));

        var answer = await ResolveOne($"{TasksPage}#sub-items");

        Assert.Equal(DevbookReferenceState.Chapter, answer.State);
        Assert.Equal("Sub-items", answer.Title);
        Assert.Null(answer.Status);
    }

    [Fact]
    public async Task A_slug_no_heading_has_is_an_unknown_heading_titled_by_its_page()
    {
        BuildDatabase();

        var answer = await ResolveOne($"{TasksPage}#renamed-away");

        Assert.Equal(DevbookReferenceState.UnknownHeading, answer.State);
        Assert.Equal("Tasks", answer.Title);
        Assert.Null(answer.Status);
        Assert.True(answer.IsBroken);
    }

    [Fact]
    public async Task A_page_nobody_wrote_is_an_unknown_page()
    {
        BuildDatabase();

        var answer = await ResolveOne(".devbook/domain/tasks/gone.md#anything");

        Assert.Equal(DevbookReferenceState.UnknownPage, answer.State);
        Assert.Equal(".devbook/domain/tasks/gone.md#anything", answer.Title);
        Assert.Equal("domain", answer.Folder);
        Assert.True(answer.IsBroken);
    }

    /// <summary>An anchor typed the way the heading reads, rather than as its
    /// slug, still finds it — a person writes what they see.</summary>
    [Fact]
    public async Task An_anchor_matches_without_regard_to_case_or_slugging()
    {
        BuildDatabase();

        var answers = await Resolver().ResolveAsync(
            Alias,
            [$"{TasksPage}#Task-Item", $"{TasksPage}#Task item"],
            TestContext.Current.CancellationToken);

        Assert.All(answers, answer =>
        {
            Assert.Equal(DevbookReferenceState.Chapter, answer.State);
            Assert.Equal("Task item", answer.Title);
        });
    }

    /// <summary>The root layout's spelling of a page and the devbook layout's are
    /// one page.</summary>
    [Fact]
    public async Task The_root_layout_spelling_of_a_path_finds_the_same_page()
    {
        BuildDatabase();

        var answer = await ResolveOne(".domain/tasks/domain.md#task-item");

        Assert.Equal(DevbookReferenceState.Chapter, answer.State);
        Assert.Equal("draft", answer.Status);
        Assert.Equal("domain", answer.Folder);
    }

    [Fact]
    public async Task The_written_forms_a_task_accepts_are_read_the_same_way()
    {
        BuildDatabase();

        var answer = await ResolveOne(@"[./.devbook\domain\tasks\domain.md#task-item]");

        Assert.Equal(DevbookReferenceState.Chapter, answer.State);
        Assert.Equal(TasksPage, answer.Path);
        Assert.Equal(@"[./.devbook\domain\tasks\domain.md#task-item]", answer.Reference);
    }

    // --- outside the devbook, and nobody to ask -------------------------------

    [Fact]
    public async Task A_path_under_no_devbook_folder_is_outside_the_devbook()
    {
        BuildDatabase();
        Write("src/README.md", "# Source\n");

        var answer = await ResolveOne("src/README.md");

        Assert.Equal(DevbookReferenceState.OutsideDevbook, answer.State);
        Assert.Equal("src/README.md", answer.Title);
        Assert.Null(answer.Folder);
        Assert.True(answer.IsBroken);
    }

    /// <summary>A switched-off folder is not one the repository adopted.</summary>
    [Fact]
    public async Task A_path_under_a_switched_off_folder_is_outside_the_devbook()
    {
        BuildDatabase();

        var resolver = new DevbookReferenceResolver(new FolderSource(_root, disabled: ".arc42"));
        var answers = await resolver.ResolveAsync(Alias, [BuildingBlocksPage], TestContext.Current.CancellationToken);

        Assert.Equal(DevbookReferenceState.OutsideDevbook, Assert.Single(answers).State);
    }

    [Fact]
    public async Task Without_a_repository_every_reference_is_unverified()
    {
        BuildDatabase();

        var answers = await Resolver().ResolveAsync(
            null,
            [TasksPage, $"{TasksPage}#task-item"],
            TestContext.Current.CancellationToken);

        Assert.All(answers, answer =>
        {
            Assert.Equal(DevbookReferenceState.Unverified, answer.State);
            Assert.False(answer.IsBroken);
        });
        Assert.Equal("task-item", answers[1].Anchor);
    }

    [Fact]
    public async Task A_value_that_is_no_reference_is_an_unknown_page_rather_than_a_failure()
    {
        var answers = await Resolver().ResolveAsync(Alias, ["readme", "", "#only-an-anchor"], TestContext.Current.CancellationToken);

        Assert.Equal(3, answers.Count);
        Assert.All(answers, answer => Assert.Equal(DevbookReferenceState.UnknownPage, answer.State));
        Assert.Equal("readme", answers[0].Title);
    }

    [Fact]
    public async Task Answers_keep_the_order_they_were_asked_in()
    {
        BuildDatabase();

        string[] asked = [BuildingBlocksPage, "src/x.md", $"{TasksPage}#task-item"];
        var answers = await Resolver().ResolveAsync(Alias, asked, TestContext.Current.CancellationToken);

        Assert.Equal(asked, answers.Select(answer => answer.Reference));
    }

    // --- with no database -----------------------------------------------------

    [Fact]
    public async Task Without_a_database_a_chapter_is_read_from_its_file_with_its_status()
    {
        var answer = await ResolveOne($"{TasksPage}#task-item");

        Assert.Equal(DevbookReferenceState.Chapter, answer.State);
        Assert.Equal("Task item", answer.Title);
        Assert.Equal("draft", answer.Status);
        Assert.Equal("domain", answer.Folder);
    }

    [Fact]
    public async Task Without_a_database_a_page_and_its_unknown_heading_are_read_from_the_file()
    {
        var answers = await Resolver().ResolveAsync(
            Alias,
            [BuildingBlocksPage, $"{BuildingBlocksPage}#level-2", ".devbook/arc42/missing.md"],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                (DevbookReferenceState.Page, "Building block view"),
                (DevbookReferenceState.UnknownHeading, "Building block view"),
                (DevbookReferenceState.UnknownPage, ".devbook/arc42/missing.md")
            ],
            answers.Select(answer => (answer.State, answer.Title)));
        Assert.All(answers, answer => Assert.Equal("arc42", answer.Folder));
    }

    /// <summary>A folder that cannot be read right now — a branch not fetched yet —
    /// says nothing about whether the page is there.</summary>
    [Fact]
    public async Task Without_a_database_or_a_readable_folder_a_reference_is_unverified()
    {
        var resolver = new DevbookReferenceResolver(new FolderSource(_root, available: false));

        var answers = await resolver.ResolveAsync(Alias, [$"{TasksPage}#task-item"], TestContext.Current.CancellationToken);

        var answer = Assert.Single(answers);
        Assert.Equal(DevbookReferenceState.Unverified, answer.State);
        Assert.Equal("domain", answer.Folder);
        Assert.False(answer.IsBroken);
    }

    // --- the picker -----------------------------------------------------------

    [Fact]
    public async Task The_targets_are_each_page_then_its_chapters_in_reading_order()
    {
        BuildDatabase();

        var targets = await Resolver().ListTargetsAsync(Alias, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new DevbookReferenceTarget(TasksPage, "Tasks", "domain", 0, null),
                new DevbookReferenceTarget($"{TasksPage}#task-item", "Task item", "domain", 2, "draft"),
                new DevbookReferenceTarget($"{TasksPage}#sub-items", "Sub-items", "domain", 3, null),
                new DevbookReferenceTarget(BuildingBlocksPage, "Building block view", "arc42", 0, null),
                new DevbookReferenceTarget($"{BuildingBlocksPage}#level-1", "Level 1", "arc42", 2, null)
            ],
            Order(targets));
    }

    [Fact]
    public async Task There_are_no_targets_without_a_database_or_a_repository()
    {
        Assert.Empty(await Resolver().ListTargetsAsync(Alias, TestContext.Current.CancellationToken));

        BuildDatabase();

        Assert.Empty(await Resolver().ListTargetsAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_switched_off_folder_offers_no_targets()
    {
        BuildDatabase();

        var resolver = new DevbookReferenceResolver(new FolderSource(_root, disabled: ".arc42"));
        var targets = await resolver.ListTargetsAsync(Alias, TestContext.Current.CancellationToken);

        Assert.NotEmpty(targets);
        Assert.All(targets, target => Assert.Equal("domain", target.Folder));
    }

    /// <summary>The folders come in the generator's own order (arc42 before
    /// domain); the test asserts each folder's run rather than which folder
    /// leads, so it says what the picker relies on.</summary>
    private static IEnumerable<DevbookReferenceTarget> Order(IReadOnlyList<DevbookReferenceTarget> targets) =>
        targets.GroupBy(target => target.Folder).OrderByDescending(group => group.Key).SelectMany(group => group);

    // --- off the caller's thread ----------------------------------------------

    /// <summary>
    /// The picker asks from the render, on the UI's dispatcher, and the database
    /// work behind an answer — opening the file, the outline, every page's
    /// headings — used to run right there, before the first await. It runs on the
    /// thread pool now, so nothing the adapter does touches the folder port on the
    /// caller's synchronization context.
    /// </summary>
    [Fact]
    public async Task Listing_targets_does_its_work_off_the_caller_s_context()
    {
        BuildDatabase();
        var source = new FolderSource(_root);

        var targets = await OnMarkedContext(() => new DevbookReferenceResolver(source).ListTargetsAsync(Alias, TestContext.Current.CancellationToken));

        Assert.NotEmpty(targets);
        Assert.NotEmpty(source.Contexts);
        Assert.DoesNotContain(source.Contexts, context => context is MarkedContext);
    }

    [Fact]
    public async Task Resolving_does_its_work_off_the_caller_s_context()
    {
        BuildDatabase();
        var source = new FolderSource(_root);

        var answers = await OnMarkedContext(() => new DevbookReferenceResolver(source).ResolveAsync(Alias, [$"{TasksPage}#task-item"], TestContext.Current.CancellationToken));

        Assert.Equal(DevbookReferenceState.Chapter, Assert.Single(answers).State);
        Assert.NotEmpty(source.Contexts);
        Assert.DoesNotContain(source.Contexts, context => context is MarkedContext);
    }

    [Fact]
    public async Task A_cancelled_question_is_not_answered()
    {
        BuildDatabase();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Resolver().ListTargetsAsync(Alias, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Resolver().ResolveAsync(Alias, [TasksPage], cancelled.Token));
    }

    /// <summary>Every page's headings in one query: the same rows, in the same
    /// order, as asking page by page.</summary>
    [Fact]
    public void The_database_answers_every_page_s_headings_at_once()
    {
        BuildDatabase();
        var location = DevbookDatabaseLocation.ForRepositoryRoot(_root);
        Assert.NotNull(location);

        using var database = DevbookDatabase.TryOpen(location);
        Assert.NotNull(database);

        var all = database.ChapterHeadings();

        Assert.Equal([BuildingBlocksPage, TasksPage], all.Select(group => group.Key).Order(StringComparer.Ordinal));
        foreach (var page in all)
        {
            Assert.Equal(
                database.Chapters(page.Key).Select(row => (row.Slug, row.Level, row.Title, row.Status)),
                page.Select(row => (row.Slug, row.Level, row.Title, row.Status)));
        }
    }

    private static async Task<T> OnMarkedContext<T>(Func<Task<T>> ask)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new MarkedContext());
        Task<T> pending;
        try
        {
            pending = ask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        return await pending;
    }

    /// <summary>The caller's context, recognisable when the folder port is asked
    /// on it.</summary>
    private sealed class MarkedContext : SynchronizationContext;

    // --- plumbing -------------------------------------------------------------

    private DevbookReferenceResolver Resolver() => new(new FolderSource(_root));

    private async Task<ResolvedDevbookReference> ResolveOne(string reference) =>
        Assert.Single(await Resolver().ResolveAsync(Alias, [reference], TestContext.Current.CancellationToken));

    private void BuildDatabase()
    {
        var target = DevbookDatabaseLocation.ForRepositoryRoot(_root);
        Assert.NotNull(target);
        Assert.True(DevbookDatabaseBuilder.Build(_root, target, TestContext.Current.CancellationToken));
    }

    private string FullPath(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative, string text)
    {
        var file = FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }

    /// <summary>One repository's folders on disk, resolved the way the product
    /// resolves an unconfigured folder: the devbook default, then the legacy root
    /// folder.</summary>
    private sealed class FolderSource(string root, bool available = true, string? disabled = null) : IDevbookFolderSource
    {
        /// <summary>The synchronization context each call into the port ran on.</summary>
        public List<SynchronizationContext?> Contexts { get; } = [];

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public void NotifyContentChanged()
        {
        }

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias)
        {
            lock (Contexts) Contexts.Add(SynchronizationContext.Current);

            return repositoryAlias is null
                ? []
                : [.. DevbookFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key != disabled })];
        }

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
        {
            var setting = Folders(repositoryAlias).FirstOrDefault(folder => folder.Key == key);
            if (setting is null) return DevbookFolderLocation.Unavailable(key, "Not configured.");
            if (!available) return DevbookFolderLocation.Unavailable(key, "Not fetched yet.", folder: setting, pending: true);

            foreach (var candidate in setting.CandidatePaths)
            {
                var path = Path.Combine(root, candidate);
                if (Directory.Exists(path))
                {
                    return new DevbookFolderLocation(key, true, null, "JSdotNet/Backlog", setting, path, root, RepositoryAlias: repositoryAlias);
                }
            }

            return DevbookFolderLocation.Unavailable(key, "No such folder.", folder: setting);
        }
    }
}
