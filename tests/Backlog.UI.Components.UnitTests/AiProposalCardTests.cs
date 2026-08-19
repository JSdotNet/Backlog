namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A suggestion held for review: attributable, reversible, durable. Those three
/// words are the whole test list, plus the one that says who "attributable" is
/// allowed to name.
/// </summary>
public sealed class AiProposalCardTests
{
    /// <summary>The product's own AI wrote this one, so it names no provider.
    /// The absent argument is the point of two of the tests below.</summary>
    private static readonly AiProposal Rewrite = new(
        "p1",
        AiProposalKind.Rewrite,
        "The roadmap band shows one quarter at a time.",
        Timestamp: "2 minutes ago",
        Original: "The band shows a quarter.",
        BlockIndex: 2);

    /// <summary>The same suggestion, come back from a Claude session this product
    /// forwarded the section to. That one left the application, so it carries the
    /// tool that wrote it.</summary>
    private static readonly AiProposal Forwarded = Rewrite with
    {
        Id = "p3",
        Provider = IntegrationProvider.Claude,
        Model = "claude-opus-5",
        SessionId = "session-7e12"
    };

    [Fact]
    public void A_proposal_that_names_no_provider_is_this_products_own_AI()
    {
        // The default is the rule, not a convenience. A default of Copilot would
        // brand every suggestion this product writes itself with a vendor unless
        // the host remembered to opt out — and hosts do not remember.
        Assert.Equal(
            IntegrationProvider.None,
            new AiProposal("p", AiProposalKind.Answer, "Because the band is quarterly.").Provider);
    }

    [Fact]
    public void The_products_own_AI_carries_no_vendor_mark_and_is_attributed_to_AI()
    {
        // A vendor mark is a passport stamp: it appears exactly when work crosses
        // out of this application. Ask AI, resolving a comment with AI and
        // rewriting a block with AI are features of this product, so a Claude or
        // a Copilot logo on the card would tell a reader their paragraph went
        // somewhere it never went.
        using var context = new BunitContext();

        var card = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, Rewrite)
            .Add(c => c.TestId, "card")
            .Add(c => c.AttributionTestId, "who"));

        Assert.Equal("Suggested by AI", card.Find("[data-testid='card']").GetAttribute("aria-label"));

        var attribution = card.Find("[data-testid='who']");

        Assert.Equal("AI", attribution.QuerySelector(".ai-proposal__provider")!.TextContent);
        Assert.Empty(card.FindAll("[data-testid='who'] svg"));
        Assert.Empty(card.FindAll("[data-testid='who'] .provider-mark"));

        // Not a vendor name anywhere on the card either, visible or announced.
        Assert.DoesNotContain("Claude", card.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Copilot", card.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_suggestion_that_came_back_from_outside_carries_the_mark_it_came_from()
    {
        // The other half of the same rule. This one was written in a session in
        // another repository, by a named tool, and points at something outside
        // that still exists — so it is marked, named, and keeps its model and
        // its session.
        using var context = new BunitContext();

        var card = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, Forwarded)
            .Add(c => c.TestId, "card")
            .Add(c => c.AttributionTestId, "who"));

        Assert.Equal("Suggested by Claude", card.Find("[data-testid='card']").GetAttribute("aria-label"));

        var attribution = card.Find("[data-testid='who']");

        Assert.Equal("Claude", attribution.QuerySelector(".ai-proposal__provider")!.TextContent);
        Assert.Single(card.FindAll("[data-testid='who'] .provider-mark--claude"));
    }

    [Fact]
    public void Attribution_is_visible_and_it_is_announced()
    {
        // The design principles ask for two things and they are not the same
        // thing: AI content must be visually distinguishable *and* labelled for
        // assistive technology. A tint alone is invisible to a screen reader and
        // an aria-label alone is invisible to everyone else. Both carriers are on
        // every proposal, marked or not — the mark says something else again.
        using var context = new BunitContext();

        var card = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, Forwarded)
            .Add(c => c.TestId, "card")
            .Add(c => c.AttributionTestId, "who"));

        Assert.Equal("Suggested by Claude", card.Find("[data-testid='card']").GetAttribute("aria-label"));
        Assert.Contains("ai-proposal", card.Find("[data-testid='card']").ClassList);

        var attribution = card.Find("[data-testid='who']");

        Assert.Contains("Claude", attribution.TextContent, StringComparison.Ordinal);
        Assert.Contains("claude-opus-5", attribution.TextContent, StringComparison.Ordinal);
        Assert.Contains("2 minutes ago", attribution.TextContent, StringComparison.Ordinal);
        Assert.Single(card.FindAll("[data-testid='who'] .provider-mark"));
    }

    [Fact]
    public void The_card_is_an_article_so_it_is_a_region_a_reader_can_land_on()
    {
        using var context = new BunitContext();

        var card = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, Rewrite)
            .Add(c => c.TestId, "card"));

        Assert.Equal("ARTICLE", card.Find("[data-testid='card']").TagName);
    }

    [Fact]
    public void Now_and_Suggested_are_shown_side_by_side()
    {
        // Original is what makes accept-or-reject a review rather than a gamble.
        using var context = new BunitContext();

        var card = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, Rewrite)
            .Add(c => c.OriginalTestId, "before")
            .Add(c => c.BodyTestId, "after"));

        Assert.Contains("The band shows a quarter.", card.Find("[data-testid='before']").TextContent, StringComparison.Ordinal);
        Assert.Contains("The roadmap band shows one quarter at a time.", card.Find("[data-testid='after']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_suggestion_that_replaces_nothing_invents_no_before()
    {
        // An Answer replaces nothing, and a "Now" heading over an empty block
        // would be the card claiming a before that never existed.
        using var context = new BunitContext();

        var card = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, new AiProposal("p2", AiProposalKind.Answer, "Because the band is quarterly."))
            .Add(c => c.OriginalTestId, "before"));

        Assert.Empty(card.FindAll("[data-testid='before']"));
    }

    [Fact]
    public void Accept_is_primary_and_reject_is_a_ghost_rather_than_a_danger()
    {
        // Discarding a suggestion destroys nothing: the document is exactly as it
        // was, and painting the refusal red would make declining a machine's
        // offer feel like deleting something.
        using var context = new BunitContext();

        var card = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, Rewrite)
            .Add(c => c.AcceptTestId, "accept")
            .Add(c => c.RejectTestId, "reject"));

        Assert.Contains("btn--primary", card.Find("[data-testid='accept']").ClassList);
        Assert.Contains("btn--ghost", card.Find("[data-testid='reject']").ClassList);
        Assert.DoesNotContain("btn--danger", card.Find("[data-testid='reject']").ClassList);
    }

    [Fact]
    public void Both_decisions_report_the_whole_proposal()
    {
        using var context = new BunitContext();
        AiProposal? accepted = null;
        AiProposal? rejected = null;

        var accept = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, Rewrite)
            .Add(c => c.OnAccept, proposal => accepted = proposal)
            .Add(c => c.AcceptTestId, "accept"));

        accept.Find("[data-testid='accept']").Click();

        var reject = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, Rewrite)
            .Add(c => c.OnReject, proposal => rejected = proposal)
            .Add(c => c.RejectTestId, "reject"));

        reject.Find("[data-testid='reject']").Click();

        // The record, not the text: the host needs the anchor and the provenance
        // to write the AI Work Log entry.
        Assert.Equal(2, accepted?.BlockIndex);
        Assert.Equal("p1", rejected?.Id);
    }

    [Theory]
    [InlineData(AiProposalState.Accepted, "Used")]
    [InlineData(AiProposalState.Rejected, "Discarded")]
    public void A_decided_proposal_drops_the_buttons_and_keeps_the_attribution(
        AiProposalState state, string label)
    {
        // The difference between provenance and a confirmation dialog is what
        // survives the click. Six months later, "did a person write this
        // paragraph" is a question the document has to be able to answer.
        using var context = new BunitContext();

        var card = context.Render<AiProposalCard>(parameters => parameters
            .Add(c => c.Proposal, Rewrite with { State = state })
            .Add(c => c.AttributionTestId, "who")
            .Add(c => c.AcceptTestId, "accept")
            .Add(c => c.RejectTestId, "reject"));

        Assert.Empty(card.FindAll("[data-testid='accept']"));
        Assert.Empty(card.FindAll("[data-testid='reject']"));

        var attribution = card.Find("[data-testid='who']");

        Assert.Equal("AI", attribution.QuerySelector(".ai-proposal__provider")!.TextContent);
        Assert.Contains(label, attribution.TextContent, StringComparison.Ordinal);
    }
}
