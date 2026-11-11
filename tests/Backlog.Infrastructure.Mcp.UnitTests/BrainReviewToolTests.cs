using Backlog.Infrastructure.Brain;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>list_brain_reviews</c> (local ADR 0054 §6,
/// <c>.devbook/domain/brain/features.md#mcp-brain-scope</c>): the Brain notes whose
/// review date has come, with the unreadable ones as warnings — the port's answer,
/// asked for today on the host's clock unless the call names a day.
/// </summary>
public sealed class BrainReviewToolTests : IDisposable
{
    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("backlog-mcp-brain-reviews");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public async Task It_answers_the_due_notes_and_the_warnings_for_today()
    {
        var reviews = new FakeReviews(new BrainReviewsDue(
            [new BrainReviewNote("areas/health.md", "Health", new DateOnly(2026, 11, 1))],
            [new BrainReviewWarning("areas/words.md", "Words", "next week")]));
        var tools = Tools(reviews, _root.FullName);

        var answer = await tools.ListBrainReviewsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 11, 11), reviews.AskedFor);
        Assert.Equal("2026-11-11", answer.On);
        Assert.Equal([new BrainReviewPayload("areas/health.md", "Health", "2026-11-01")], answer.Due);
        var warning = Assert.Single(answer.Warnings);
        Assert.Equal("areas/words.md", warning.Path);
        Assert.Equal("next week", warning.Value);
        Assert.Contains("not a YYYY-MM-DD date", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_day_the_call_names_is_the_one_asked()
    {
        var reviews = new FakeReviews(BrainReviewsDue.None);
        var tools = Tools(reviews, _root.FullName);

        var answer = await tools.ListBrainReviewsAsync("2026-12-01", TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 12, 1), reviews.AskedFor);
        Assert.Equal("2026-12-01", answer.On);
        Assert.Empty(answer.Due);
        Assert.Empty(answer.Warnings);
    }

    [Fact]
    public async Task A_day_that_is_no_date_is_refused_in_words()
    {
        var reviews = new FakeReviews(BrainReviewsDue.None);
        var tools = Tools(reviews, _root.FullName);

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.ListBrainReviewsAsync("tomorrow", TestContext.Current.CancellationToken));

        Assert.Contains("brain.review_day_invalid", failure.Message, StringComparison.Ordinal);
        Assert.Null(reviews.AskedFor);
    }

    [Fact]
    public async Task No_brain_set_or_none_composed_is_said_in_words()
    {
        var unset = Tools(new FakeReviews(BrainReviewsDue.None), brainFolder: null);
        var none = new DevbookTools(
            new FakeDevbookFolderSource(_root.FullName, new DevbookFolderSetting(".arc42", "Architecture", ".arc42")),
            new FakeDevbookAnnotationStore(),
            new FakeRepositoryDirectory([Backlog]));

        var notSet = await Assert.ThrowsAsync<McpException>(() => unset.ListBrainReviewsAsync(cancellationToken: TestContext.Current.CancellationToken));
        var unavailable = await Assert.ThrowsAsync<McpException>(() => none.ListBrainReviewsAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("brain.not_set", notSet.Message, StringComparison.Ordinal);
        Assert.Contains("brain.unavailable", unavailable.Message, StringComparison.Ordinal);
    }

    private DevbookTools Tools(IBrainReviewDates reviews, string? brainFolder)
    {
        var brain = new BrainFolderSource(() => brainFolder);

        return new DevbookTools(
            new FakeDevbookFolderSource(_root.FullName, new DevbookFolderSetting(".arc42", "Architecture", ".arc42")),
            new FakeDevbookAnnotationStore(),
            new FakeRepositoryDirectory([Backlog]),
            brain,
            new BrainNoteWriter(brain),
            reviews,
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 11, 9, 30, 0, TimeSpan.Zero)));
    }

    private sealed class FakeReviews(BrainReviewsDue answer) : IBrainReviewDates
    {
        public DateOnly? AskedFor { get; private set; }

        public Task<BrainReviewsDue> DueAsync(DateOnly today, CancellationToken cancellationToken = default)
        {
            AskedFor = today;
            return Task.FromResult(answer);
        }
    }
}
