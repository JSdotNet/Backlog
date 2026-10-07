namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A pull request's review state on the link model: what it defaults to, the words
/// and the class name each value carries, and the mark a verdict draws beside the
/// checks — and that review required and no review draw none.
/// <para>
/// Also the session state a small badge wears on its mark, and the accessible name a
/// host gives a badge whose visible text is only a number.
/// </para>
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

    /// <summary>Review required is an open pull request's ordinary state, and draws
    /// nothing: the link renders the same markup as with no review state at all.</summary>
    [Fact]
    public void Review_required_does_not_change_what_the_link_draws()
    {
        using var context = new BunitContext();

        var plain = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Checks = IntegrationCheckState.Passing })
            .Add(l => l.TestId, "link"));
        var reviewed = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Checks = IntegrationCheckState.Passing, Review = IntegrationReviewState.ReviewRequired })
            .Add(l => l.TestId, "link"));

        reviewed.MarkupMatches(plain.Markup);
    }

    /// <summary>A verdict is a mark of its own, straight after the checks, carrying
    /// its words twice — on its title and in an sr-only span — so the ink is never
    /// the only carrier.</summary>
    [Theory]
    [InlineData(IntegrationReviewState.Approved, "approved", "Approved")]
    [InlineData(IntegrationReviewState.ChangesRequested, "changes-requested", "Changes requested")]
    public void A_verdict_draws_a_mark_after_the_checks(IntegrationReviewState review, string slug, string words)
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Checks = IntegrationCheckState.Failing, Review = review })
            .Add(l => l.ChecksTestId, "checks")
            .Add(l => l.ReviewTestId, "review"));

        var mark = link.Find("[data-testid='review']");
        Assert.Contains($"integration-link__review--{slug}", mark.ClassList);
        Assert.Equal(words, mark.GetAttribute("title"));
        Assert.Equal(words, mark.QuerySelector(".sr-only")!.TextContent.Trim());
        Assert.Equal("review", link.Find("[data-testid='checks']").NextElementSibling?.GetAttribute("data-testid"));
    }

    [Fact]
    public void A_verdict_is_drawn_without_checks_as_well()
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Review = IntegrationReviewState.Approved })
            .Add(l => l.ReviewTestId, "review"));

        Assert.NotNull(link.Find("[data-testid='review']"));
    }

    /// <summary>With state as ink, a session's state colours its mark and is said in
    /// an sr-only span, in place of the chip it would otherwise draw.</summary>
    [Theory]
    [InlineData(IntegrationSessionState.Stalled, "stalled")]
    [InlineData(IntegrationSessionState.Running, "running")]
    [InlineData(IntegrationSessionState.Finished, "finished")]
    public void State_as_ink_puts_a_sessions_state_on_its_mark(IntegrationSessionState state, string slug)
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, IntegrationLinkRef.Session("s-1", "s-1", IntegrationProvider.Claude, state))
            .Add(l => l.StateAsInk, true)
            .Add(l => l.ShowTitle, false)
            .Add(l => l.TestId, "link"));

        var element = link.Find("[data-testid='link']");
        Assert.Contains("integration-link--session", element.ClassList);
        Assert.Contains($"integration-link--session-{slug}", element.ClassList);
        Assert.Empty(link.FindAll(".integration-chip__glyph"));
        Assert.Equal(IntegrationStates.LabelOf(state), link.Find(".sr-only").TextContent.Trim());
    }

    /// <summary>Without state as ink a session keeps its chip, as before.</summary>
    [Fact]
    public void A_session_keeps_its_chip_without_state_as_ink()
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, IntegrationLinkRef.Session("s-1", "s-1", IntegrationProvider.Claude, IntegrationSessionState.Stalled))
            .Add(l => l.TestId, "link"));

        Assert.DoesNotContain("integration-link--session", link.Find("[data-testid='link']").ClassList);
        Assert.NotEmpty(link.FindAll(".integration-chip__glyph"));
    }

    /// <summary>The accessible name a host hands in lands on every shape; unset, the
    /// reference carries none and is named by its content, as before.</summary>
    [Fact]
    public void An_accessible_name_lands_on_the_reference_when_given()
    {
        using var context = new BunitContext();

        var named = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Url = "https://example.invalid/pull/74" })
            .Add(l => l.AriaLabel, "Pull request 74 · approved")
            .Add(l => l.TestId, "link"));
        var unnamed = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull)
            .Add(l => l.TestId, "link"));

        Assert.Equal("Pull request 74 · approved", named.Find("[data-testid='link']").GetAttribute("aria-label"));
        Assert.Null(unnamed.Find("[data-testid='link']").GetAttribute("aria-label"));
        Assert.Null(unnamed.Find("[data-testid='link']").GetAttribute("role"));
    }
}
