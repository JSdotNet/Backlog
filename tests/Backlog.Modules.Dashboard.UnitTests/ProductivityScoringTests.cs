using System.Reflection;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Backlog.UI.Components.Metrics;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The score, its inputs, and the one duplication in this module that is worth a
/// test of its own.
/// </summary>
/// <remarks>
/// The defect these were rewritten for was on screen and unmissable: full marks
/// came from two per-week constants, so a reader who had merged 441 pull requests
/// in four weeks was shown "441 of 6" and five of eight weight points were pinned
/// permanently at full. Several of the tests below exist only to make sure no
/// version of that survives — a constant, a target smaller than the reading, or a
/// bar the reader is already standing on.
/// </remarks>
public class ProductivityScoringTests
{
    /// <summary>
    /// The record this repository's owner actually set, so the arithmetic in these
    /// tests is the arithmetic that was wrong on screen: 304 merged pull requests in
    /// one four-week block, which is 76 a week.
    /// </summary>
    private const int BestBlockMerged = 304;

    private static ProductivityTargets Record(decimal merged = 76m, decimal closed = 18m, decimal sessions = 11m) =>
        new(merged, closed, sessions);

    /// <summary>
    /// The module restates the metrics library's formula because a module may not
    /// reference a UI library. This is the assertion that keeps the restatement
    /// honest: the score card renders from the inputs and lets the component do the
    /// arithmetic, while the trend chart renders from the module's own figure, so if
    /// these two ever disagree the same window shows two different scores.
    /// </summary>
    /// <remarks>
    /// The later rows carry the widened composition — the two size inputs and the
    /// sessions one — because a formula that agreed over four inputs and drifted over
    /// seven would be a formula that agreed over the shape nobody ships.
    /// </remarks>
    [Theory]
    [InlineData(0, 0, -1)]
    [InlineData(3, 12, -1)]
    [InlineData(9, 30, -1)]
    [InlineData(18, 30, -1)]
    [InlineData(9, 30, 0)]
    [InlineData(18, 30, 44)]
    [InlineData(441, 72, 300)]
    public void The_modules_score_agrees_with_the_component_librarys(int merged, int closed, int sessions)
    {
        var inputs = ProductivityScoring.InputsFor(
            [.. Enumerable.Range(1, merged).Select(number => Merged(number, churned: number % 3 == 0))],
            [.. Enumerable.Range(1, closed).Select(Closed)],
            weeks: 12,
            Record(),
            sessions < 0 ? null : sessions);

        var library = inputs
            .Select(input => new MetricScoreComponent(input.Label, input.Value, input.Max, input.Weight))
            .ToList();

        Assert.Equal(MetricScoring.Score(library), ProductivityScoring.Score(inputs));
    }

    /// <summary>
    /// Full marks is derived from the reader, not decided for them — and there is no
    /// per-week constant left in the file for it to fall back to.
    /// </summary>
    [Fact]
    public void Full_marks_is_the_readers_own_best_block_rather_than_a_constant()
    {
        var inputs = ProductivityScoring.InputsFor([], [], weeks: 4, Record());

        var merged = Assert.Single(inputs, input => input.Label == "Pull requests merged");

        // 76 a week, over four weeks, a quarter above.
        Assert.Equal(380m, merged.Max);

        // And nothing is left behind to fall back to. The two constants this replaced
        // were named MergedPullRequestsPerWeek and ClosedIssuesPerWeek; the assertion
        // is on the shape rather than on those two names, so reintroducing the idea
        // under a third name lands here as well.
        Assert.DoesNotContain(
            typeof(ProductivityScoring).GetFields(BindingFlags.NonPublic | BindingFlags.Static),
            field => field.IsLiteral && field.Name.Contains("PerWeek", StringComparison.Ordinal));
    }

    /// <summary>
    /// The acceptance test for the whole defect. A reader standing exactly on their
    /// own record reads 0.8, not full marks: there is somewhere left to go, which is
    /// the one thing "441 of 6" could never say.
    /// </summary>
    [Fact]
    public void A_reader_at_their_own_record_does_not_score_full_marks()
    {
        var inputs = ProductivityScoring.InputsFor(
            [.. Enumerable.Range(1, BestBlockMerged).Select(number => Merged(number, churned: false, reviewed: false))],
            [],
            weeks: 4,
            Record());

        var merged = Assert.Single(inputs, input => input.Label == "Pull requests merged");

        Assert.Equal(BestBlockMerged, merged.Value);
        Assert.Equal(380m, merged.Max);
        Assert.Equal(0.8m, merged.Normalized);
    }

    [Fact]
    public void Beating_your_own_record_moves_the_reading_without_reaching_the_bar()
    {
        var atRecord = ProductivityScoring.InputsFor(
            [.. Enumerable.Range(1, BestBlockMerged).Select(number => Merged(number, churned: false, reviewed: false))],
            [],
            weeks: 4,
            Record());

        var past = ProductivityScoring.InputsFor(
            [.. Enumerable.Range(1, 350).Select(number => Merged(number, churned: false, reviewed: false))],
            [],
            weeks: 4,
            Record());

        var before = Assert.Single(atRecord, input => input.Label == "Pull requests merged");
        var after = Assert.Single(past, input => input.Label == "Pull requests merged");

        Assert.True(after.Normalized > before.Normalized);
        Assert.True(after.Normalized < 1m);
    }

    /// <summary>
    /// No history, no target — and no constant standing in for one. The proportions
    /// still score, so the card stays readable rather than going blank.
    /// </summary>
    [Fact]
    public void No_baseline_drops_the_volume_inputs_rather_than_inventing_a_target()
    {
        var inputs = ProductivityScoring.InputsFor(
            [Merged(1, churned: false), Merged(2, churned: true)],
            [Closed(1)],
            weeks: 4,
            targets: null,
            sessions: 9);

        Assert.DoesNotContain(inputs, input => input.Label == "Pull requests merged");
        Assert.DoesNotContain(inputs, input => input.Label == "Issues closed");
        Assert.DoesNotContain(inputs, input => input.Label == "Assistant sessions");

        // ...and what is left is the four proportions, which never needed a target.
        Assert.Contains(inputs, input => input.Label == "First review within a day");
        Assert.Contains(inputs, input => input.Label == "Merged without post-review churn");
    }

    /// <summary>
    /// A reader with no history at all must not be scored as having failed to reach
    /// a target of nothing. An empty history is an absent input, which the weight
    /// normalisation absorbs, rather than a zero one, which would drag the score down
    /// for an absence of evidence.
    /// </summary>
    [Fact]
    public void An_empty_history_is_no_baseline_rather_than_a_zero_one()
    {
        var pullRequests = new[] { Merged(1, churned: false), Merged(2, churned: false) };

        var empty = ProductivityScoring.InputsFor(pullRequests, [], weeks: 4, ProductivityTargets.None, sessions: 0);

        Assert.DoesNotContain(empty, input => input.Label == "Pull requests merged");
        Assert.DoesNotContain(empty, input => input.Label == "Assistant sessions");

        // Two clean, promptly reviewed merges score the same with no history as they
        // would if the volume inputs had never existed. A zeroed baseline would have
        // halved the figure.
        Assert.Equal(
            ProductivityScoring.Score([.. empty.Where(input => input.Max > 0m)]),
            ProductivityScoring.Score(empty));
        Assert.Equal(100m, ProductivityScoring.Score(empty));
    }

    [Fact]
    public void Sessions_are_scored_against_the_readers_own_best_block_of_sessions()
    {
        // Twenty sessions in the best four weeks is five a week.
        var inputs = ProductivityScoring.InputsFor(
            [],
            [],
            weeks: 4,
            Record(merged: 0m, closed: 0m, sessions: 5m),
            sessions: 20);

        var ran = Assert.Single(inputs, input => input.Label == "Assistant sessions");

        Assert.Equal(20m, ran.Value);
        Assert.Equal(25m, ran.Max);
        Assert.Equal(0.8m, ran.Normalized);

        // The lowest weight on the card. A session counts effort, not output.
        Assert.Equal(1m, ran.Weight);
    }

    /// <summary>
    /// No assistant records which repository a session was for, so a focused reader
    /// is handed no sessions figure at all rather than a whole-machine one dressed up
    /// as one repository's. Absent, not zero: zero would claim they ran none.
    /// </summary>
    [Fact]
    public void Sessions_are_left_out_when_one_repository_is_in_focus()
    {
        var focused = ProductivityScoring.InputsFor([], [], weeks: 4, Record(), sessions: null);

        Assert.DoesNotContain(focused, input => input.Label == "Assistant sessions");

        // And the same call with a figure does carry one, so the absence above is the
        // null rather than the target.
        Assert.Contains(
            ProductivityScoring.InputsFor([], [], weeks: 4, Record(), sessions: 0),
            input => input.Label == "Assistant sessions");
    }

    /// <summary>
    /// The size inputs are proportions of the pull requests whose size could actually
    /// be read, never of every merge. A pull request whose diff never arrived carries
    /// a zero that is not a small change, and counting it as one would turn a failed
    /// fetch into a compliment.
    /// </summary>
    [Fact]
    public void Size_and_files_are_proportions_of_what_could_actually_be_read()
    {
        var pullRequests = Enumerable.Range(1, 10)
            .Select(number => Merged(number, churned: false) with
            {
                SizeKnown = number <= 8,
                ChangedLines = number <= 5 ? 120 : 900,
                ChangedFiles = number <= 5 ? 4 : 40
            })
            .ToList();

        var inputs = ProductivityScoring.InputsFor(pullRequests, [], weeks: 4, Record());

        var lines = Assert.Single(inputs, input => input.Label.Contains("changed lines", StringComparison.Ordinal));
        var files = Assert.Single(inputs, input => input.Label.Contains("files or fewer", StringComparison.Ordinal));

        Assert.Equal(5m, lines.Value);
        Assert.Equal(8m, lines.Max);
        Assert.Equal(5m, files.Value);
        Assert.Equal(8m, files.Max);

        // The thresholds are in the labels, because that is what makes them arguable
        // on screen rather than only in this file.
        Assert.Equal("Merged under 400 changed lines", lines.Label);
        Assert.Equal("Merged touching 10 files or fewer", files.Label);
    }

    [Fact]
    public void Nothing_readable_leaves_both_size_inputs_out()
    {
        var inputs = ProductivityScoring.InputsFor(
            [.. Enumerable.Range(1, 6).Select(number => Merged(number, churned: false))],
            [],
            weeks: 4,
            Record());

        Assert.DoesNotContain(inputs, input => input.Label.Contains("changed lines", StringComparison.Ordinal));
        Assert.DoesNotContain(inputs, input => input.Label.Contains("files or fewer", StringComparison.Ordinal));
    }

    /// <summary>
    /// Seven inputs weighted 3-2-2-1-1-1-1 add to eleven, and every one of them at
    /// full marks is still 100 — the normalisation does not have to be rebalanced
    /// because three inputs were added to it.
    /// </summary>
    [Fact]
    public void The_weights_still_normalise_by_their_total()
    {
        var pullRequests = Enumerable.Range(1, 10)
            .Select(number => Merged(number, churned: false) with
            {
                ReviewTurnaround = TimeSpan.FromHours(2),
                SizeKnown = true,
                ChangedLines = 90,
                ChangedFiles = 3
            })
            .ToList();

        var inputs = ProductivityScoring.InputsFor(
            pullRequests,
            [.. Enumerable.Range(1, 10).Select(Closed)],
            weeks: 4,
            Record(merged: 1m, closed: 1m, sessions: 1m),
            sessions: 10);

        Assert.Equal(7, inputs.Count);
        Assert.Equal(11m, inputs.Sum(input => input.Weight));
        Assert.Equal(100m, ProductivityScoring.Score(inputs));
    }

    /// <summary>
    /// The literal regression: "441 of 6".
    /// </summary>
    /// <remarks>
    /// It could only happen because the target was fixed while the reading was not.
    /// Once the target comes from a block grid that includes the window being scored,
    /// the window's own count is at most the best block's, so the reading can never
    /// pass its own bar — 441 reads as "441 of 551", and the headroom is what puts
    /// the bar above the record rather than on it.
    /// </remarks>
    [Fact]
    public void The_reading_column_can_never_exceed_its_max()
    {
        // The window is itself the best block, which is what the grid guarantees.
        var inputs = ProductivityScoring.InputsFor(
            [.. Enumerable.Range(1, 441).Select(number => Merged(number, churned: number % 4 == 0))],
            [.. Enumerable.Range(1, 72).Select(Closed)],
            weeks: 4,
            Record(merged: 441m / 4m, closed: 72m / 4m, sessions: 300m / 4m),
            sessions: 300);

        Assert.All(inputs, input => Assert.True(
            input.Value <= input.Max,
            $"{input.Label} read {input.Value} of {input.Max}, which is a reading past its own target."));

        var merged = Assert.Single(inputs, input => input.Label == "Pull requests merged");

        Assert.Equal(551.25m, merged.Max);
        Assert.Equal(0.8m, merged.Normalized);
    }

    [Fact]
    public void An_input_nothing_could_have_scored_is_left_out_rather_than_scored_as_nil()
    {
        // Nothing was reviewed, so review promptness and freedom from churn have no
        // evidence either way. Scoring that silence as zero would drag the figure
        // down for an absence rather than for a result.
        var inputs = ProductivityScoring.InputsFor(
            [Merged(1, churned: false, reviewed: false)],
            [],
            weeks: 4,
            Record());

        Assert.Equal(2, inputs.Count);
        Assert.DoesNotContain(inputs, input => input.Label.Contains("review", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(inputs, input => input.Label.Contains("churn", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Full marks scales with the window, so a month is not judged against a
    /// quarter's worth of work. It is the same personal rate either way — what moves
    /// is how long the reader had to spend it over.
    /// </summary>
    [Fact]
    public void Full_marks_moves_with_the_window_so_a_month_is_not_judged_against_a_quarter()
    {
        var merged = Enumerable.Range(1, 6).Select(number => Merged(number, churned: false)).ToList();

        // A record of six merges in the best four weeks: one and a half a week.
        var record = Record(merged: 1.5m);

        var overFourWeeks = ProductivityScoring.InputsFor(merged, [], weeks: 4, record);
        var overTwelveWeeks = ProductivityScoring.InputsFor(merged, [], weeks: 12, record);

        var four = Assert.Single(overFourWeeks, input => input.Label == "Pull requests merged");
        var twelve = Assert.Single(overTwelveWeeks, input => input.Label == "Pull requests merged");

        Assert.Equal(7.5m, four.Max);
        Assert.Equal(22.5m, twelve.Max);

        // Six merges is four fifths of a month's bar and a fifteenth short of a
        // third of a quarter's.
        Assert.Equal(0.8m, four.Normalized);
        Assert.True(twelve.Normalized < four.Normalized);
    }

    [Fact]
    public void An_input_past_full_marks_does_not_earn_extra()
    {
        var input = new ProductivityScoreInput("Pull requests merged", 400m, 18m, 3m);

        Assert.Equal(1m, input.Normalized);
    }

    [Fact]
    public void A_window_with_no_activity_scores_zero_rather_than_dividing_by_it()
    {
        var inputs = ProductivityScoring.InputsFor([], [], weeks: 12);

        Assert.Equal(0m, ProductivityScoring.Score(inputs));
    }

    [Fact]
    public void Weights_are_normalised_by_their_total_so_every_weight_at_one_is_a_plain_average()
    {
        var inputs = new ProductivityScoreInput[]
        {
            new("First", 1m, 2m),
            new("Second", 2m, 2m)
        };

        // 0.5 and 1.0, averaged, on a 0..100 scale.
        Assert.Equal(75m, ProductivityScoring.Score(inputs));
    }

    private static ActivityPullRequest Merged(int number, bool churned, bool reviewed = true)
    {
        var mergedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero).AddDays(number);

        return new ActivityPullRequest(
            "backlog",
            number,
            mergedAt,
            reviewed ? mergedAt.AddHours(-4) : null,
            ReviewRounds: reviewed ? 1 : 0,
            CommitsAfterFirstReview: churned ? 2 : 0,
            ForcePushesAfterFirstReview: 0,
            FilesRetouched: churned ? 1 : 0,
            ChurnComplete: true)
        {
            ReviewTurnaround = reviewed ? TimeSpan.FromHours(number % 48) : null
        };
    }

    private static ActivityIssue Closed(int number) =>
        new("backlog", number, new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero).AddDays(number));
}
