namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The tri-state, copied from KnowledgeReferenceLink rather than re-derived, and
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
