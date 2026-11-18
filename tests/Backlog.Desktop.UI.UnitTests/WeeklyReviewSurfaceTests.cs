using Backlog.Modules.Progress.UI.WeeklyReview;
using Backlog.SharedKernel.Results;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The weekly review takeover (<c>.devbook/domain/progress/features.md#the-weekly-review</c>)
/// against a double of the window's <see cref="IWeeklyReviewSource"/>: one stage at a
/// time, each line with its action in place, a stage done when it has nothing left, and
/// a write read again so the line it answered goes.
/// </summary>
public sealed class WeeklyReviewSurfaceTests : IDisposable
{
    private readonly BunitContext _context = new();
    private readonly FakeReviewSource _source = new();

    public WeeklyReviewSurfaceTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero)));
        _context.Services.AddSingleton<IWeeklyReviewSource>(_source);
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<WeeklyReviewSurface> Render() => _context.Render<WeeklyReviewSurface>();

    [Fact]
    public void The_review_opens_on_get_clear_and_shows_only_that_stages_lines()
    {
        _source.Inputs = new WeeklyReviewInputs(
            [new ReviewInboxItem(Guid.NewGuid(), "Call the plumber")],
            null,
            [new ReviewEntry(Guid.NewGuid(), "Taxes", ReviewEntryStatus.Ready, DueOn: new DateOnly(2026, 11, 1))],
            [],
            []);

        var review = Render();

        review.WaitForAssertion(() =>
        {
            Assert.Equal("get-clear", review.Find("[data-testid='weekly-review-stage']").GetAttribute("data-stage"));
            var line = Assert.Single(review.FindAll("[data-testid='weekly-review-line']"));
            Assert.Contains("Call the plumber", line.TextContent);
            Assert.NotNull(line.QuerySelector("[data-testid='weekly-review-action-open-inbox']"));
            Assert.Equal("true", review.Find("[data-testid='weekly-review-stage-get-clear']").GetAttribute("aria-pressed"));
            Assert.Contains("(1)", review.Find("[data-testid='weekly-review-stage-get-current']").TextContent);
        });

        review.Find("[data-testid='weekly-review-next']").Click();

        review.WaitForAssertion(() =>
        {
            Assert.Equal("get-current", review.Find("[data-testid='weekly-review-stage']").GetAttribute("data-stage"));
            var line = Assert.Single(review.FindAll("[data-testid='weekly-review-line']"));
            Assert.Contains("Taxes", line.TextContent);
            Assert.NotNull(line.QuerySelector("[data-testid='weekly-review-action-open-task']"));
        });
    }

    [Fact]
    public void A_stage_with_nothing_left_shows_done()
    {
        _source.Inputs = new WeeklyReviewInputs([], null, [], [], []);

        var review = Render();

        review.WaitForAssertion(() =>
        {
            Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-stage-done']"));
            Assert.Empty(review.FindAll("[data-testid='weekly-review-line']"));
            foreach (var slug in new[] { "get-clear", "get-current", "get-creative" })
            {
                var stage = review.Find($"[data-testid='weekly-review-stage-{slug}']");
                Assert.Equal("true", stage.GetAttribute("data-done"));
                Assert.Contains("done", stage.TextContent);
            }
        });
    }

    [Fact]
    public void A_missing_brain_is_said_on_the_review()
    {
        _source.Inputs = new WeeklyReviewInputs([], null, [], [], []);

        var review = Render();

        review.WaitForAssertion(() =>
            Assert.Contains(review.FindAll("[data-testid='weekly-review-unavailable']"), note => note.TextContent.Contains("Brain unavailable", StringComparison.Ordinal)));
    }

    [Fact]
    public void Marking_the_plans_next_action_acts_and_reads_again_so_the_line_goes()
    {
        var step = new ReviewEntry(Guid.NewGuid(), "Write the brief", ReviewEntryStatus.Draft, "launch");
        _source.Inputs = new WeeklyReviewInputs([], null, [step], ["launch"], []);
        _source.OnAct = action =>
        {
            if (action.Kind == WeeklyReviewActionKind.MarkNextAction)
            {
                _source.Inputs = _source.Inputs with { Entries = [step with { Status = ReviewEntryStatus.Ready }] };
            }
        };

        var review = Render();
        review.WaitForAssertion(() => Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-next']")));
        review.Find("[data-testid='weekly-review-next']").Click();

        review.WaitForAssertion(() => Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-action-mark-next-action']")));
        review.Find("[data-testid='weekly-review-action-mark-next-action']").Click();

        review.WaitForAssertion(() =>
        {
            var acted = Assert.Single(_source.Acted);
            Assert.Equal(new WeeklyReviewAction(WeeklyReviewActionKind.MarkNextAction, "Mark next action", step.Id.ToString("D")), acted);
            Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-stage-done']"));
        });
    }

    [Fact]
    public void Moving_a_note_to_the_archive_acts_on_its_path()
    {
        var note = new ReviewNote("projects/done.md", "Done");
        _source.Inputs = new WeeklyReviewInputs(
            [],
            new ReviewBrain(new ReviewBrainAreas("_inbox", "projects", "someday", "areas", true), [note], [], [], [note]),
            [],
            [],
            []);

        var review = Render();
        review.WaitForAssertion(() => Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-next']")));
        review.Find("[data-testid='weekly-review-next']").Click();
        review.WaitForAssertion(() => Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-action-archive-note']")));

        review.Find("[data-testid='weekly-review-action-archive-note']").Click();

        review.WaitForAssertion(() =>
        {
            var acted = Assert.Single(_source.Acted);
            Assert.Equal(WeeklyReviewActionKind.ArchiveNote, acted.Kind);
            Assert.Equal("projects/done.md", acted.Target);
        });
    }

    [Fact]
    public void Opening_a_note_from_get_creative_hands_its_path_to_the_window()
    {
        _source.Inputs = new WeeklyReviewInputs(
            [],
            new ReviewBrain(new ReviewBrainAreas("_inbox", "projects", "someday", "areas", true), [new("someday/boat.md", "Boat")], [], [], []),
            [],
            [],
            []);

        var review = Render();
        review.WaitForAssertion(() => Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-stage-get-creative']")));
        review.Find("[data-testid='weekly-review-stage-get-creative']").Click();
        review.WaitForAssertion(() => Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-action-open-note']")));

        review.Find("[data-testid='weekly-review-action-open-note']").Click();

        review.WaitForAssertion(() =>
            Assert.Equal(new WeeklyReviewAction(WeeklyReviewActionKind.OpenNote, "Open the note", "someday/boat.md"), Assert.Single(_source.Acted)));
    }

    [Fact]
    public void A_refused_action_is_said_and_the_line_stays()
    {
        var note = new ReviewNote("projects/done.md", "Done");
        _source.Inputs = new WeeklyReviewInputs(
            [],
            new ReviewBrain(new ReviewBrainAreas("_inbox", "projects", "someday", "areas", true), [note], [], [], [note]),
            [],
            [],
            []);
        _source.Answer = Result.Failure(Backlog.SharedKernel.Results.Error.Validation("weekly_review.archive_refused", "The note was not moved."));

        var review = Render();
        review.WaitForAssertion(() => Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-next']")));
        review.Find("[data-testid='weekly-review-next']").Click();
        review.WaitForAssertion(() => Assert.NotEmpty(review.FindAll("[data-testid='weekly-review-action-archive-note']")));
        review.Find("[data-testid='weekly-review-action-archive-note']").Click();

        review.WaitForAssertion(() =>
        {
            Assert.Contains("The note was not moved.", review.Find("[data-testid='weekly-review-message']").TextContent);
            Assert.Single(review.FindAll("[data-testid='weekly-review-line']"));
        });
    }

    [Fact]
    public void A_failed_read_is_said_and_no_stage_reads_done()
    {
        _source.ReadError = new IOException("The Brain's index is busy.");

        var review = Render();

        review.WaitForAssertion(() =>
        {
            Assert.Contains("The Brain's index is busy.", review.Find("[data-testid='weekly-review-message']").TextContent);
            Assert.Empty(review.FindAll("[data-testid='weekly-review-stage-done']"));
            Assert.All(review.FindAll("[data-testid^='weekly-review-stage-get-']"), stage => Assert.Equal("false", stage.GetAttribute("data-done")));
        });
    }

    private sealed class FakeReviewSource : IWeeklyReviewSource
    {
        public WeeklyReviewInputs Inputs { get; set; } = WeeklyReviewInputs.None;

        public Exception? ReadError { get; set; }

        public Result Answer { get; set; } = Result.Success();

        public Action<WeeklyReviewAction>? OnAct { get; set; }

        public List<WeeklyReviewAction> Acted { get; } = [];

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<WeeklyReviewInputs> ReadAsync(CancellationToken cancellationToken = default) =>
            ReadError is { } error ? Task.FromException<WeeklyReviewInputs>(error) : Task.FromResult(Inputs);

        public Task<Result> ActAsync(WeeklyReviewAction action, CancellationToken cancellationToken = default)
        {
            Acted.Add(action);
            if (Answer.IsSuccess) OnAct?.Invoke(action);
            return Task.FromResult(Answer);
        }
    }
}
