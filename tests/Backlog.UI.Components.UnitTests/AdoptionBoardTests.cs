using Backlog.UI.Components.Devbook;

using Bunit;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The adoption board draws what it is handed and counts its findings from the
/// cards: these pin the strip, the findings, the ladder grid, the toggle and the
/// pick, on hand-made boards.
/// </summary>
public sealed class AdoptionBoardTests
{
    private static readonly AdoptionHome[] Homes =
    [
        new("decide", "Decide", AdoptionHomeGroup.BeforePullRequest, "01"),
        new("verify", "Verify", AdoptionHomeGroup.BeforePullRequest, "02"),
        new("integrate", "Integrate", AdoptionHomeGroup.AfterPullRequest, "03"),
        new("release", "Release & deploy", AdoptionHomeGroup.AfterPullRequest, "04"),
        new("harness", "Agent harness", AdoptionHomeGroup.Underneath)
    ];

    private static readonly AdoptionCard[] Cards =
    [
        new("a", "decide", "Tracker", AdoptionRung.Adopted, AdoptionEvidence.Recorded, "2026-09-25", "Plan entries linked."),
        new("b", "decide", "Sweep", AdoptionRung.Trial, AdoptionEvidence.None, "2026-09-26", "Nothing yet.", "its first brief lands.", ["Verify"]),
        new("c", "verify", "QA agent", AdoptionRung.Adopted, AdoptionEvidence.Partial, "2026-09-25"),
        new("d", "integrate", "Merge review", AdoptionRung.Trial, AdoptionEvidence.Fired, "2026-09-26", null, "its first verdict is posted."),
        new("e", "integrate", "Alerts", AdoptionRung.Candidate, AdoptionEvidence.None, "2026-09-25"),
        new("f", "harness", "Worktrees", AdoptionRung.Adopted, AdoptionEvidence.Recorded, "2026-09-25"),
        new("g", "harness", "Old guidance", AdoptionRung.Retired, AdoptionEvidence.Recorded, "2026-08-27"),
        new("h", "harness", "Security review", AdoptionRung.Hold, AdoptionEvidence.None, "2026-09-26")
    ];

    private static readonly AdoptionBoardModel Board = new(Homes, Cards);

    [Fact]
    public void The_findings_are_counted_from_the_cards()
    {
        var findings = Board.Findings;

        Assert.Equal(3, findings.Adopted);
        Assert.Equal(2, findings.AdoptedRecorded);
        Assert.Equal(2, findings.AdoptedBefore);
        Assert.Equal(0, findings.AdoptedAfter);
        Assert.Equal(1, findings.AdoptedUnderneath);
        Assert.Equal(["Integrate", "Release & deploy"], findings.HomesWithoutAdopted);
        Assert.Equal(2, findings.Trial);
        Assert.Equal(0, findings.TrialRecorded);
        Assert.Equal(0, findings.TrialPartial);
        Assert.Equal(1, findings.TrialFired);
        Assert.Equal(1, findings.TrialNone);
        Assert.Equal(3, findings.Waiting);
        Assert.Equal(2, findings.WaitingWithEvent);
    }

    [Fact]
    public void The_findings_cards_say_what_the_counts_say()
    {
        using var context = Context();

        var board = context.Render<AdoptionBoard>(parameters => parameters.Add(p => p.Model, Board));

        var adopted = board.Find("[data-testid='adoption-finding-adopted']");
        Assert.Contains("3 adopted · 2 with evidence", adopted.TextContent);
        Assert.Contains("Adopted ends at the pull request", adopted.TextContent);
        Assert.Contains("Integrate and Release & deploy have none.", adopted.TextContent);

        var trial = board.Find("[data-testid='adoption-finding-trial']");
        Assert.Contains("2 trial · none with recorded evidence", trial.TextContent);
        Assert.Contains("Trial is wired, not proven", trial.TextContent);
        Assert.Contains("1 fired, nothing published", trial.TextContent);
        Assert.Contains("1 none yet", trial.TextContent);

        Assert.Contains("3 usages wait on an event", board.Find("[data-testid='adoption-finding-next']").TextContent);
    }

    [Fact]
    public void The_strip_groups_its_tiles_and_says_each_mix_in_words()
    {
        using var context = Context();

        var board = context.Render<AdoptionBoard>(parameters => parameters.Add(p => p.Model, Board));

        var groups = board.FindAll("[data-testid='adoption-strip-group']");
        Assert.Equal(
            ["Before the pull request", "After it", "Underneath"],
            groups.Select(group => group.QuerySelector(".adoption-strip__group-name")!.TextContent));
        Assert.Equal(
            ["1 adopted · 1 trial", "1 adopted", "1 trial · 1 cand.", "no AI", "1 adopted"],
            board.FindAll("[data-testid='adoption-strip-mix']").Select(mix => mix.TextContent));
    }

    [Fact]
    public void The_grid_has_a_row_per_home_a_divider_at_each_group_and_the_ladder_as_its_columns()
    {
        using var context = Context();

        var board = context.Render<AdoptionBoard>(parameters => parameters.Add(p => p.Model, Board));

        Assert.Equal(
            ["decide", "verify", "integrate", "release", "harness"],
            board.FindAll("[data-testid='adoption-row']").Select(row => row.GetAttribute("data-home")));
        var dividers = board.FindAll("[data-testid='adoption-divider']");
        Assert.Equal(2, dividers.Count);
        Assert.StartsWith("pull request", dividers[0].TextContent.Trim());
        Assert.Equal(
            ["candidate", "trial", "adopted", "retired"],
            board.FindAll("[role='columnheader']").Skip(1).Select(head => head.TextContent));

        // The empty stage stays, and says so.
        var release = board.Find("[data-testid='adoption-row'][data-home='release']");
        Assert.Contains("No AI usage here yet.", release.TextContent);
        Assert.Empty(release.QuerySelectorAll("[data-testid='adoption-card']"));

        // Hold shares the retired column, and says it is on hold.
        var retired = board.Find("[data-testid='adoption-row'][data-home='harness'] .adoption-ladder__cell--retired");
        Assert.Equal(["retired", "hold"], retired.QuerySelectorAll("[data-testid='adoption-card']").Select(card => card.GetAttribute("data-rung")));
        Assert.Contains("on hold", retired.TextContent);
    }

    [Fact]
    public void A_card_shows_its_evidence_mark_since_evidence_reach_and_what_moves_it_up()
    {
        using var context = Context();

        var board = context.Render<AdoptionBoard>(parameters => parameters.Add(p => p.Model, Board));

        var sweep = board.FindAll("[data-testid='adoption-card']").Single(card => card.TextContent.Contains("Sweep", StringComparison.Ordinal));
        Assert.Equal("none", sweep.QuerySelector("[data-testid='adoption-dots']")!.GetAttribute("data-evidence"));
        Assert.Equal("no evidence yet", sweep.QuerySelector("[data-testid='adoption-dots']")!.GetAttribute("aria-label"));
        Assert.Contains("since 2026-09-26", sweep.TextContent);
        Assert.Contains("+Verify", sweep.TextContent);
        Assert.Contains("Nothing yet.", sweep.TextContent);
        Assert.Contains("Moves up when its first brief lands.", sweep.TextContent.Replace("\n", " ", StringComparison.Ordinal));

        var tracker = board.FindAll("[data-testid='adoption-card']").Single(card => card.TextContent.Contains("Tracker", StringComparison.Ordinal));
        Assert.Equal(3, tracker.QuerySelectorAll(".adoption-dots__dot--on").Length);
        Assert.Null(tracker.QuerySelector(".adoption-card__next"));
    }

    [Fact]
    public void The_toggle_steps_back_what_has_its_record_and_what_is_off_the_ladder()
    {
        using var context = Context();

        var board = context.Render<AdoptionBoard>(parameters => parameters.Add(p => p.Model, Board));

        Assert.Empty(board.FindAll(".adoption-card--settled"));
        var toggle = board.Find("[data-testid='adoption-gaps-toggle']");
        Assert.Equal("false", toggle.GetAttribute("aria-pressed"));

        toggle.Click();

        Assert.Equal("true", board.Find("[data-testid='adoption-gaps-toggle']").GetAttribute("aria-pressed"));
        Assert.Equal(
            ["Tracker", "Worktrees", "Old guidance", "Security review"],
            board.FindAll(".adoption-card--settled").Select(card => card.QuerySelector(".adoption-card__title")!.TextContent));

        // The state is said, not only dimmed.
        Assert.Equal(
            ["has its record", "has its record", "off the ladder", "off the ladder"],
            board.FindAll(".adoption-card--settled").Select(card => card.QuerySelector(".sr-only")!.TextContent));
    }

    [Fact]
    public void Picking_a_card_hands_it_back()
    {
        using var context = Context();
        AdoptionCard? picked = null;

        var board = context.Render<AdoptionBoard>(parameters => parameters
            .Add(p => p.Model, Board)
            .Add(p => p.OnOpen, (AdoptionCard card) => picked = card));

        board.FindAll("[data-testid='adoption-card']").Single(card => card.TextContent.Contains("Merge review", StringComparison.Ordinal)).Click();

        Assert.Equal("d", picked?.Key);
    }

    [Fact]
    public void An_empty_board_says_nothing_is_there_rather_than_printing_zeros()
    {
        using var context = Context();

        var board = context.Render<AdoptionBoard>(parameters => parameters.Add(p => p.Model, new AdoptionBoardModel(Homes, [])));

        var adopted = board.Find("[data-testid='adoption-finding-adopted']").TextContent;
        Assert.Contains("Nothing adopted yet", adopted);
        Assert.Contains("none adopted", adopted);
        Assert.DoesNotContain("0 ", adopted, StringComparison.Ordinal);
        Assert.Contains("none on trial", board.Find("[data-testid='adoption-finding-trial']").TextContent);
        Assert.Contains("No usage is on the board yet.", board.Find("[data-testid='adoption-finding-next']").TextContent);
        Assert.Contains("Nothing on trial", board.Find("[data-testid='adoption-finding-trial']").TextContent);
        Assert.Contains("Nothing waits to move", board.Find("[data-testid='adoption-finding-next']").TextContent);
        Assert.All(board.FindAll("[data-testid='adoption-strip-mix']"), mix => Assert.Equal("no AI", mix.TextContent));
        Assert.Equal(5, board.FindAll("[data-testid='adoption-row-empty']").Count);
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
