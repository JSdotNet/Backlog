using Backlog.Infrastructure.Devbook.Building;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// A chapter's <c>tests</c> field as <c>node_attribute</c> rows named
/// <c>tests</c>, one per identifier and in the order written, read back through
/// <see cref="DevbookDatabase.Attributes(string)"/>. The Domain view tells a
/// proved requirement from one no test is linked to by these rows (local
/// ADR 0040); <see cref="DevbookBuilderParityTests"/> holds the Node writer to
/// the same rows.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class DevbookChapterTestsAttributeTests : IDisposable
{
    private const string ChapterPath = ".devbook/domain/inbox/requirements.md";

    private const string Markdown = """
        # Inbox Requirements

        ## Requirement: Capture keeps the text

        ```meta
        status: active
        type: requirement
        tests: [unit:dotnet:Inbox.Tests.CaptureTests.Keeps_the_text, e2e:playwright:inbox.spec.ts#capture]
        ```

        ## Requirement: Capture names its source

        ```meta
        status: active
        type: requirement
        tests: unit:dotnet:Inbox.Tests.CaptureTests.Names_its_source
        ```

        ## Requirement: Triage is undone

        ```meta
        status: active
        type: requirement
        ```

        Enforced at: open
        """;

    private DevbookDatabase? _opened;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-chapter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Each_tests_entry_is_a_tests_attribute_of_its_chapter()
    {
        var tests = Build().Attributes("tests");

        Assert.Equal(
            ["unit:dotnet:Inbox.Tests.CaptureTests.Keeps_the_text", "e2e:playwright:inbox.spec.ts#capture"],
            tests[$"{ChapterPath}#requirement-capture-keeps-the-text"]);
        Assert.Equal(
            ["unit:dotnet:Inbox.Tests.CaptureTests.Names_its_source"],
            tests[$"{ChapterPath}#requirement-capture-names-its-source"]);
    }

    [Fact]
    public void A_chapter_without_tests_has_no_tests_attribute()
    {
        var tests = Build().Attributes("tests");

        Assert.False(tests.Contains($"{ChapterPath}#requirement-triage-is-undone"));
        Assert.Equal(2, tests.Count);
    }

    private DevbookDatabase Build()
    {
        var file = Path.Combine(_root, ChapterPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, Markdown);
        var target = Path.Combine(_root, "out", "devbook.db");

        Assert.True(DevbookDatabaseBuilder.Build(_root, target, TestContext.Current.CancellationToken));

        var database = DevbookDatabase.TryOpen(target);
        Assert.NotNull(database);
        _opened = database;
        return database;
    }

    public void Dispose()
    {
        _opened?.Dispose();
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
}
