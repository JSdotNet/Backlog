using Backlog.Modules.Brain.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// The Brain notes whose review date has come (local ADR 0054 §6,
/// <c>.devbook/domain/brain/domain.md#knowledge-note</c>): the <c>review</c> date a
/// note's top <c>meta</c> block names, built into the Brain's own database and read
/// back as dates — a value that is no date a warning beside them, never a failure,
/// and nothing at all for a device with no Brain or a Brain whose index will not
/// answer.
/// </summary>
// Beside the other suites that build and open Brain databases: clearing the Sqlite
// pools on dispose is process-wide, so these do not run alongside them.
[Collection(CorpusRegistryCollection.Name)]
public sealed class BrainReviewDateReaderTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-brain-review-dates", Guid.NewGuid().ToString("N"));

    private string Brain => Path.Combine(_scratch, "Brain");

    private string Target => DatabaseFor(Brain)!;

    public BrainReviewDateReaderTests()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Areas | areas |\n| Projects | projects |\n");
        Write("areas/health.md", "# Health\n\n```meta\nreview: 2026-11-01\n```\n\nCheck-ups.\n");
        Write("areas/money.md", "# Money\n\n```meta\nreview: \"2026-11-11\"\n```\n\nBudget.\n");
        Write("areas/garden.md", "# Garden\n\n```meta\nreview: 2026-12-01\n```\n\nNot yet.\n");
        Write("areas/sloppy.md", "# Sloppy\n\n```meta\nreview: 2026-11-5\n```\n\nNo leading zero.\n");
        Write("areas/words.md", "# Words\n\n```meta\nreview: next week\n```\n\nNo date at all.\n");
        Write("projects/sync.md", """
            # Sync

            No review on the file.

            ## Later

            ```meta
            review: 2026-01-01
            ```

            A heading's review is not the note's.
            """);
    }

    [Fact]
    public async Task The_notes_whose_review_date_has_come_arrive_oldest_first()
    {
        Build();

        var answer = await Reader().DueAsync(Today, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new BrainReviewNote("areas/health.md", "Health", new DateOnly(2026, 11, 1)),
                new BrainReviewNote("areas/money.md", "Money", Today),
            ],
            answer.Due);
    }

    [Fact]
    public async Task An_unreadable_date_is_a_warning_and_the_rest_still_answer()
    {
        Build();

        var answer = await Reader().DueAsync(Today, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new BrainReviewWarning("areas/sloppy.md", "Sloppy", "2026-11-5"),
                new BrainReviewWarning("areas/words.md", "Words", "next week"),
            ],
            answer.Unreadable);
        Assert.Contains("areas/words.md", answer.Unreadable[1].Message, StringComparison.Ordinal);
        Assert.NotEmpty(answer.Due);
    }

    [Fact]
    public async Task A_later_day_brings_the_future_review_in_and_a_headings_review_never()
    {
        Build();

        var answer = await Reader().DueAsync(new DateOnly(2026, 12, 1), TestContext.Current.CancellationToken);

        Assert.Equal(["areas/health.md", "areas/money.md", "areas/garden.md"], answer.Due.Select(note => note.NotePath));
        Assert.DoesNotContain(answer.Due, note => note.NotePath == "projects/sync.md");
    }

    [Fact]
    public async Task No_brain_no_database_or_a_broken_one_reads_as_nothing_due()
    {
        var none = new BrainReviewDateReader(() => null);
        var missing = new BrainReviewDateReader(() => Brain, _ => Path.Combine(_scratch, "missing", "devbook.db"));

        Directory.CreateDirectory(_scratch);
        var brokenPath = Path.Combine(_scratch, "broken.db");
        File.WriteAllText(brokenPath, "this is not a database");
        var broken = new BrainReviewDateReader(() => Brain, _ => brokenPath);

        Assert.Same(BrainReviewsDue.None, await none.DueAsync(Today, TestContext.Current.CancellationToken));
        Assert.Same(BrainReviewsDue.None, await missing.DueAsync(Today, TestContext.Current.CancellationToken));
        Assert.Same(BrainReviewsDue.None, await broken.DueAsync(Today, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("2026-11-20", 2026, 11, 20)]
    [InlineData(" '2026-02-28' ", 2026, 2, 28)]
    public void A_date_reads_only_as_year_month_day(string value, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), BrainReviewDate.Parse(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-11-5")]
    [InlineData("20-11-2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-11-20,2026-12-01")]
    [InlineData("soon")]
    public void Anything_else_is_no_date(string? value) => Assert.Null(BrainReviewDate.Parse(value));

    private BrainReviewDateReader Reader() => new(() => Brain, DatabaseFor);

    private string? DatabaseFor(string root) =>
        DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), root);

    private void Build() =>
        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, BrainCorpusProfile.Instance, TestContext.Current.CancellationToken));

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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
