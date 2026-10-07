namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A pull request's review state on the link model: what it defaults to, the words
/// and the class name each value carries, and that carrying one changes nothing the
/// link draws yet — the mark is a later step's.
/// </summary>
public sealed class IntegrationReviewStateTests
{
    private static readonly IntegrationLinkRef Pull =
        IntegrationLinkRef.PullRequest("74", "PR #74", IntegrationArtifactState.Open, "Add the roadmap band");

    [Fact]
    public void A_link_says_no_review_state_until_a_host_sets_one()
    {
        Assert.Equal(IntegrationReviewState.None, Pull.Review);
        Assert.Equal(
            IntegrationReviewState.None,
            new IntegrationLinkRef("1", IntegrationProvider.GitHub, IntegrationLinkKind.PullRequest, "#1").Review);
    }

    [Theory]
    [InlineData(IntegrationReviewState.ReviewRequired, "review-required", "Review required")]
    [InlineData(IntegrationReviewState.Approved, "approved", "Approved")]
    [InlineData(IntegrationReviewState.ChangesRequested, "changes-requested", "Changes requested")]
    public void Each_review_state_has_its_own_slug_and_words(IntegrationReviewState review, string slug, string words)
    {
        Assert.Equal(slug, IntegrationStates.SlugOf(review));
        Assert.Equal(words, IntegrationStates.LabelOf(review));
    }

    [Fact]
    public void No_review_state_has_no_words_and_the_fallback_slug()
    {
        Assert.Equal(string.Empty, IntegrationStates.LabelOf(IntegrationReviewState.None));
        Assert.Equal("unknown", IntegrationStates.SlugOf(IntegrationReviewState.None));
    }

    [Fact]
    public void Every_review_state_is_mapped()
    {
        foreach (var review in Enum.GetValues<IntegrationReviewState>().Where(value => value is not IntegrationReviewState.None))
        {
            Assert.NotEqual("unknown", IntegrationStates.SlugOf(review));
            Assert.NotEqual(string.Empty, IntegrationStates.LabelOf(review));
        }
    }

    /// <summary>This step carries the state and draws nothing of it: the link renders
    /// the same markup whatever the review state is.</summary>
    [Theory]
    [InlineData(IntegrationReviewState.ReviewRequired)]
    [InlineData(IntegrationReviewState.Approved)]
    [InlineData(IntegrationReviewState.ChangesRequested)]
    public void A_review_state_does_not_change_what_the_link_draws(IntegrationReviewState review)
    {
        using var context = new BunitContext();

        var plain = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Checks = IntegrationCheckState.Passing })
            .Add(l => l.TestId, "link"));
        var reviewed = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Checks = IntegrationCheckState.Passing, Review = review })
            .Add(l => l.TestId, "link"));

        reviewed.MarkupMatches(plain.Markup);
    }
}
