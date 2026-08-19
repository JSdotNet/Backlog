using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Backlog.UI.Components.Metrics;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The score, its inputs, and the one duplication in this module that is worth a
/// test of its own.
/// </summary>
public class ProductivityScoringTests
{
    /// <summary>
    /// The module restates the metrics library's formula because a module may not
    /// reference a UI library. This is the assertion that keeps the restatement
    /// honest: the score card renders from the inputs and lets the component do the
    /// arithmetic, while the trend chart renders from the module's own figure, so if
    /// these two ever disagree the same window shows two different scores.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 12)]
    [InlineData(9, 30)]
    [InlineData(18, 30)]
    public void The_modules_score_agrees_with_the_component_librarys(int merged, int closed)
    {
        var inputs = ProductivityScoring.InputsFor(
            [.. Enumerable.Range(1, merged).Select(number => Merged(number, churned: number % 3 == 0))],
            [.. Enumerable.Range(1, closed).Select(Closed)],
            weeks: 12);

        var library = inputs
            .Select(input => new MetricScoreComponent(input.Label, input.Value, input.Max, input.Weight))
            .ToList();

        Assert.Equal(MetricScoring.Score(library), ProductivityScoring.Score(inputs));
    }

    [Fact]
    public void An_input_nothing_could_have_scored_is_left_out_rather_than_scored_as_nil()
    {
        // Nothing was reviewed, so review promptness and freedom from churn have no
        // evidence either way. Scoring that silence as zero would drag the figure
        // down for an absence rather than for a result.
        var inputs = ProductivityScoring.InputsFor([Merged(1, churned: false, reviewed: false)], [], weeks: 4);

        Assert.Equal(2, inputs.Count);
        Assert.DoesNotContain(inputs, input => input.Label.Contains("review", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(inputs, input => input.Label.Contains("churn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Full_marks_moves_with_the_window_so_a_month_is_not_judged_against_a_quarter()
    {
        var merged = Enumerable.Range(1, 6).Select(number => Merged(number, churned: false)).ToList();

        var overFourWeeks = ProductivityScoring.InputsFor(merged, [], weeks: 4);
        var overTwelveWeeks = ProductivityScoring.InputsFor(merged, [], weeks: 12);

        var four = Assert.Single(overFourWeeks, input => input.Label == "Pull requests merged");
        var twelve = Assert.Single(overTwelveWeeks, input => input.Label == "Pull requests merged");

        Assert.Equal(6m, four.Max);
        Assert.Equal(18m, twelve.Max);

        // Six merges is full marks over a month and a third of it over a quarter.
        Assert.Equal(1m, four.Normalized);
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
