namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The tri-state, copied from DevbookReferenceLink rather than re-derived, and
/// the two chips a reference can carry.
/// </summary>
public sealed class IntegrationLinkTests
{
    private static readonly IntegrationLinkRef Pull =
        IntegrationLinkRef.PullRequest("74", "PR #74", IntegrationArtifactState.Open, "Add the roadmap band");

    [Fact]
    public void A_reference_with_a_url_is_an_anchor_that_opens_away_from_the_app()
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Url = "https://example.invalid/pull/74" })
            .Add(l => l.TestId, "link"));

        var anchor = link.Find("a[data-testid='link']");

        Assert.Equal("https://example.invalid/pull/74", anchor.GetAttribute("href"));
        Assert.Equal("_blank", anchor.GetAttribute("target"));
        Assert.Equal("noopener", anchor.GetAttribute("rel"));
    }

    [Fact]
    public void A_reference_with_only_a_callback_is_a_button()
    {
        // A Copilot CLI session is a local process with nothing to address, so it
        // arrives this way — and a fourth renderer invented for that case is the
        // drift this family exists to prevent.
        using var context = new BunitContext();
        IntegrationLinkRef? opened = null;

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, IntegrationLinkRef.Session("4a1c", "session 4a1c", IntegrationProvider.Copilot, IntegrationSessionState.Running))
            .Add(l => l.OnOpen, reference => opened = reference)
            .Add(l => l.TestId, "link"));

        link.Find("button[data-testid='link']").Click();

        Assert.Equal("4a1c", opened?.Id);
    }

    [Fact]
    public void A_reference_nobody_can_follow_is_inert_text()
    {
        // It should not invite a click, and it should not take a tab stop to
        // refuse one.
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull)
            .Add(l => l.TestId, "link"));

        Assert.Contains("integration-link--inert", link.Find("span[data-testid='link']").ClassList);
        Assert.Empty(link.FindAll("a"));
        Assert.Empty(link.FindAll("button"));
    }

    [Fact]
    public void A_url_wins_over_a_callback()
    {
        // A link already navigates, and two ways to reach one place is one too
        // many.
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Url = "https://example.invalid/pull/74" })
            .Add(l => l.OnOpen, _ => { }));

        Assert.Single(link.FindAll("a"));
        Assert.Empty(link.FindAll("button"));
    }

    [Fact]
    public void The_state_and_the_drift_are_two_chips_and_not_one()
    {
        // A disagreement between the artifact and what we believe is a different
        // fact from the artifact's own state, and on screen two facts are two
        // chips.
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Drift = IntegrationDrift.LocalAhead })
            .Add(l => l.StateTestId, "state")
            .Add(l => l.DriftTestId, "drift"));

        Assert.Contains("badge--integration-open", link.Find("[data-testid='state']").ClassList);
        Assert.Contains("badge--integration-local-ahead", link.Find("[data-testid='drift']").ClassList);
    }

    [Fact]
    public void State_as_ink_colours_the_link_and_says_the_state_in_words_instead_of_a_chip()
    {
        // One button per artifact: the state is the link's own colour, and the
        // words travel in the title and an sr-only span so colour is never the
        // sole carrier.
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Url = "https://example.invalid/pull/74" })
            .Add(l => l.StateAsInk, true)
            .Add(l => l.TestId, "link")
            .Add(l => l.StateTestId, "state"));

        var anchor = link.Find("a[data-testid='link']");

        Assert.Contains("integration-link--state-open", anchor.ClassList);
        Assert.Empty(link.FindAll(".badge--integration"));
        Assert.Equal("Open", link.Find("[data-testid='state']").TextContent.Trim());
        Assert.Contains("sr-only", link.Find("[data-testid='state']").ClassList);
        Assert.Contains("Open on GitHub", anchor.GetAttribute("title"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_drift_note_replaces_the_general_sentence()
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with
            {
                Drift = IntegrationDrift.Detached,
                DriftNote = "Repository was renamed last week."
            })
            .Add(l => l.DriftTestId, "drift"));

        Assert.Equal("Repository was renamed last week.", link.Find("[data-testid='drift']").GetAttribute("title"));
    }

    [Fact]
    public void Compact_drops_the_title_and_names_the_mark()
    {
        // There is no room for the artifact's title, and the mark stops being
        // decoration the moment it is the only thing saying which tool this is.
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull)
            .Add(l => l.Density, IntegrationDensity.Compact)
            .Add(l => l.MarkTestId, "mark")
            .Add(l => l.TestId, "link"));

        Assert.DoesNotContain("Add the roadmap band", link.Find("[data-testid='link']").TextContent, StringComparison.Ordinal);
        Assert.Equal("img", link.Find("[data-testid='mark']").GetAttribute("role"));

        // Still on the title, so nothing is actually lost.
        Assert.Equal("Add the roadmap band", link.Find("[data-testid='link']").GetAttribute("title"));
    }

    [Fact]
    public void The_repository_is_named_only_where_a_host_asked_for_it()
    {
        // The list groups by repository and says it once per group; this is for a
        // reference standing on its own.
        using var context = new BunitContext();
        var reference = Pull with { Repository = new IntegrationRepositoryRef("r1", "jsdotnet/backlog", "Backlog") };

        var quiet = context.Render<IntegrationLink>(p => p.Add(l => l.Link, reference).Add(l => l.TestId, "link"));

        Assert.DoesNotContain("Backlog", quiet.Find("[data-testid='link']").TextContent, StringComparison.Ordinal);

        var named = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, reference)
            .Add(l => l.ShowRepository, true)
            .Add(l => l.TestId, "link"));

        Assert.Contains("Backlog", named.Find("[data-testid='link']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>A pull request's check roll-up rides on the link as a small mark of
    /// its own, after the label — and says itself in words as well as in ink,
    /// because colour is never the only carrier.</summary>
    [Theory]
    [InlineData(IntegrationCheckState.Pending, "pending", "Checks pending")]
    [InlineData(IntegrationCheckState.Passing, "passing", "Checks passing")]
    [InlineData(IntegrationCheckState.Failing, "failing", "Checks failing")]
    public void Checks_render_as_a_mark_that_says_its_state_in_words(IntegrationCheckState checks, string slug, string words)
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Checks = checks })
            .Add(l => l.ChecksTestId, "checks"));

        var mark = link.Find("[data-testid='checks']");

        Assert.Contains("integration-link__checks", mark.ClassList);
        Assert.Contains($"integration-link__checks--{slug}", mark.ClassList);
        Assert.Equal(words, mark.GetAttribute("title"));
        Assert.Equal(words, mark.QuerySelector(".sr-only")!.TextContent.Trim());
        Assert.Equal("true", mark.QuerySelector("svg")!.GetAttribute("aria-hidden"));
    }

    /// <summary>After the label, so the number a reader scans for stays first and
    /// the mark reads as something said about it.</summary>
    [Fact]
    public void The_checks_mark_follows_the_label()
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { Checks = IntegrationCheckState.Passing, AutoMerge = true })
            .Add(l => l.TestId, "link"));

        var classes = link.Find("[data-testid='link']").Children.Select(child => child.ClassName ?? string.Empty).ToList();
        var label = classes.FindIndex(name => name.Contains("integration-link__label", StringComparison.Ordinal));
        var checks = classes.FindIndex(name => name.Contains("integration-link__checks", StringComparison.Ordinal));
        var autoMerge = classes.FindIndex(name => name.Contains("integration-link__auto-merge", StringComparison.Ordinal));

        Assert.True(label >= 0 && checks > label && autoMerge > checks);
    }

    /// <summary>No checks is a real state — a repository that runs none — and
    /// draws nothing rather than a mark promising a result that never comes.</summary>
    [Fact]
    public void No_checks_and_no_auto_merge_draw_nothing()
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull)
            .Add(l => l.ChecksTestId, "checks")
            .Add(l => l.AutoMergeTestId, "auto-merge"));

        Assert.Empty(link.FindAll("[data-testid='checks']"));
        Assert.Empty(link.FindAll("[data-testid='auto-merge']"));
        Assert.Empty(link.FindAll(".integration-link__checks"));
        Assert.Empty(link.FindAll(".integration-link__auto-merge"));
    }

    [Fact]
    public void Auto_merge_renders_as_a_mark_that_says_so_in_words()
    {
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, Pull with { AutoMerge = true })
            .Add(l => l.AutoMergeTestId, "auto-merge"));

        var mark = link.Find("[data-testid='auto-merge']");

        Assert.Contains("integration-link__auto-merge", mark.ClassList);
        Assert.Equal("Auto-merge on", mark.GetAttribute("title"));
        Assert.Equal("Auto-merge on", mark.QuerySelector(".sr-only")!.TextContent.Trim());
    }

    [Fact]
    public void A_session_reference_shows_a_session_state_and_never_an_artifact_one()
    {
        // The factories are what make the wrong pairing unreachable.
        using var context = new BunitContext();

        var link = context.Render<IntegrationLink>(parameters => parameters
            .Add(l => l.Link, IntegrationLinkRef.Session("s1", "session 4a1c", IntegrationProvider.Claude, IntegrationSessionState.Stalled))
            .Add(l => l.StateTestId, "state"));

        Assert.Contains("badge--integration-stalled", link.Find("[data-testid='state']").ClassList);
    }
}
