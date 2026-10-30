using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// A Brain note's <c>repo:</c> links read as links, and read in reverse as the
/// backlinks a repository chapter shows its owner (local ADR 0027 §6,
/// <c>.devbook/domain/brain/requirements.md#repo-links-and-backlinks</c>): from
/// the Brain's own database, never from the repository.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public class BrainBacklinkTests : IDisposable
{
    private const string BuildingBlocks = ".devbook/arc42/05-building-block-view.md";

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-brain-backlinks", Guid.NewGuid().ToString("N"));

    private string Brain => Path.Combine(_scratch, "Brain");

    private string Target => DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), Brain)!;

    public BrainBacklinkTests()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n| Work | work |\n");
        Write("projects/plan.md", $$"""
            # Plan

            ```meta
            related: [repo:backlog/{{BuildingBlocks}}#devbook]
            ```

            See the [building blocks](repo:backlog/{{BuildingBlocks}}).
            """);
        Write("work/standup.md", $$"""
            # Standup

            Nothing here.

            ## Yesterday

            ```meta
            status: draft
            ```

            Read [the view](repo:Backlog/.arc42/05-building-block-view.md#runtime) again.
            """);
        Write("projects/two.md", $$"""
            # Two chapters

            ## First

            ```meta
            status: draft
            ```

            Under [the first](repo:backlog/{{BuildingBlocks}}).

            ## Second

            ```meta
            status: draft
            ```

            And [the second](repo:backlog/{{BuildingBlocks}}#devbook).
            """);
        Write("work/elsewhere.md", """
            # Elsewhere

            A [spec](repo:specmanager/.devbook/domain/specs/domain.md) and [another chapter](repo:backlog/.devbook/domain/tasks/domain.md).
            """);
    }

    [Theory]
    [InlineData("repo:backlog/.devbook/arc42/05-building-block-view.md#devbook", "backlog", ".devbook/arc42/05-building-block-view.md", "devbook")]
    [InlineData("repo:backlog/.devbook/arc42/05-building-block-view.md", "backlog", ".devbook/arc42/05-building-block-view.md", null)]
    [InlineData("\"repo:other/./.domain/x.md#\"", "other", ".domain/x.md", null)]
    public void A_repo_link_reads_as_alias_path_and_slug(string text, string alias, string path, string? slug)
    {
        var link = BrainRepoLink.Parse(text);

        Assert.NotNull(link);
        Assert.Equal(alias, link.Alias);
        Assert.Equal(path, link.Path);
        Assert.Equal(slug, link.Slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".devbook/arc42/05-building-block-view.md")]
    [InlineData("repo:")]
    [InlineData("repo:backlog")]
    [InlineData("repo:/.devbook/arc42/x.md")]
    [InlineData("repo:backlog/")]
    public void Anything_else_is_no_repo_link(string? text) => Assert.Null(BrainRepoLink.Parse(text));

    [Fact]
    public void A_link_names_its_chapter_in_either_layout_and_any_alias_casing()
    {
        var link = BrainRepoLink.Parse($"repo:Backlog/{BuildingBlocks}#runtime")!;

        Assert.True(link.Names("backlog", BuildingBlocks));
        Assert.True(link.Names("backlog", ".arc42/05-building-block-view.md"));
        Assert.False(link.Names("other", BuildingBlocks));
        Assert.False(link.Names("backlog", ".devbook/arc42/06-runtime-view.md"));
    }

    [Fact]
    public void A_repository_chapter_lists_every_brain_note_linking_to_it_once()
    {
        using var database = Build();

        var links = BrainBacklinkReader.LinksTo(database, "backlog", BuildingBlocks);

        Assert.Equal(
            [
                new BrainBacklink("projects/plan.md", "Plan", null),
                new BrainBacklink("work/standup.md", "Standup", "Yesterday"),
                new BrainBacklink("projects/two.md", "Two chapters", "First")
            ],
            links);
    }

    [Fact]
    public void A_repository_s_links_read_per_heading_once_per_note_and_heading()
    {
        using var database = Build();

        var links = BrainBacklinkReader.ChapterLinks(database, "backlog");

        Assert.Equal(
            [
                new BrainChapterBacklink(BuildingBlocks, null, new BrainBacklink("projects/plan.md", "Plan", null)),
                new BrainChapterBacklink(BuildingBlocks, "devbook", new BrainBacklink("projects/plan.md", "Plan", null)),
                new BrainChapterBacklink(BuildingBlocks, null, new BrainBacklink("projects/two.md", "Two chapters", "First")),
                new BrainChapterBacklink(BuildingBlocks, "devbook", new BrainBacklink("projects/two.md", "Two chapters", "Second")),
                new BrainChapterBacklink(".devbook/domain/tasks/domain.md", null, new BrainBacklink("work/elsewhere.md", "Elsewhere", null)),
                new BrainChapterBacklink(".arc42/05-building-block-view.md", "runtime", new BrainBacklink("work/standup.md", "Standup", "Yesterday"))
            ],
            links);
        Assert.Empty(BrainBacklinkReader.ChapterLinks(database, "elsewhere"));
    }

    [Fact]
    public async Task A_double_that_knows_no_headings_answers_none()
    {
        IBrainBacklinks backlinks = new NoHeadings();

        Assert.Empty(await backlinks.ChapterLinksAsync("backlog", TestContext.Current.CancellationToken));
    }

    private sealed class NoHeadings : IBrainBacklinks
    {
        public Task<IReadOnlyList<BrainBacklink>> LinksToAsync(string repositoryAlias, string chapterPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BrainBacklink>>([]);
    }

    [Fact]
    public void A_chapter_nothing_in_the_brain_links_to_has_no_backlinks()
    {
        using var database = Build();

        Assert.Empty(BrainBacklinkReader.LinksTo(database, "backlog", ".devbook/arc42/06-runtime-view.md"));
        Assert.Empty(BrainBacklinkReader.LinksTo(database, "elsewhere", BuildingBlocks));
        Assert.Equal(["work/elsewhere.md"], BrainBacklinkReader.LinksTo(database, "specmanager", ".devbook/domain/specs/domain.md").Select(link => link.NotePath));
    }

    [Fact]
    public async Task The_reader_asks_the_brain_s_own_database()
    {
        using (Build())
        {
        }

        var reader = new BrainBacklinkReader(() => Brain, root => DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), root));

        var links = await reader.LinksToAsync("backlog", ".arc42/05-building-block-view.md", TestContext.Current.CancellationToken);

        Assert.Equal(["projects/plan.md", "work/standup.md", "projects/two.md"], links.Select(link => link.NotePath));
    }

    [Fact]
    public async Task A_brain_with_no_database_yet_has_no_backlinks()
    {
        var reader = new BrainBacklinkReader(() => Brain, _ => Path.Combine(_scratch, "missing", "devbook.db"));

        Assert.Empty(await reader.LinksToAsync("backlog", BuildingBlocks, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task With_no_brain_set_there_is_nothing_to_read()
    {
        var reader = new BrainBacklinkReader(() => null);

        Assert.Empty(await reader.LinksToAsync("backlog", BuildingBlocks, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Reading_backlinks_writes_nothing_into_the_brain()
    {
        var before = Directory.GetFiles(Brain, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        using (var database = Build()) _ = BrainBacklinkReader.LinksTo(database, "backlog", BuildingBlocks);

        Assert.Equal(before, Directory.GetFiles(Brain, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }

    private DevbookDatabase Build()
    {
        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken));
        var database = DevbookDatabase.TryOpen(Target);
        Assert.NotNull(database);
        return database;
    }

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(Brain, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
