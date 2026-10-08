using Backlog.Infrastructure.Devbook.Scenarios;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// <see cref="ScenarioReferences"/>: a part's state against its page's last run,
/// and the <c>Proved by:</c> pointers a requirement chapter carries.
/// </summary>
public sealed class ScenarioReferencesTests
{
    private const string Page = """
        # Set up and fill the backlog

        ```meta
        type: scenario
        ```

        ## Statuses are set up
        - **Given** a product "Webshop"

        ## An item is moved
        - **When** I drag it to "Doing"
        """;

    private static ScenarioRun Run(string signature, params (string Anchor, ScenarioOutcome Outcome)[] parts) =>
        new(2, signature, new DateTimeOffset(2026, 10, 7, 8, 30, 0, TimeSpan.Zero), null,
            [.. parts.Select(part => new ScenarioRunPart(part.Anchor, part.Anchor, part.Outcome, null, null))],
            new Dictionary<string, ScenarioRunShot>());

    [Fact]
    public void A_part_with_no_run_was_never_run() =>
        Assert.Equal(ScenarioState.NeverRun, ScenarioReferences.PartState("9c41e2a0", null, "statuses-are-set-up"));

    [Fact]
    public void A_part_on_a_run_of_another_signature_is_stale_even_when_it_passed() =>
        Assert.Equal(
            ScenarioState.Stale,
            ScenarioReferences.PartState("9c41e2a0", Run("00000000", ("statuses-are-set-up", ScenarioOutcome.Passed)), "statuses-are-set-up"));

    [Fact]
    public void A_version_1_run_proves_no_signature_so_its_parts_are_stale()
    {
        var run = Run("9c41e2a0", ("statuses-are-set-up", ScenarioOutcome.Passed)) with { Signature = null };

        Assert.Equal(ScenarioState.Stale, ScenarioReferences.PartState("9c41e2a0", run, "statuses-are-set-up"));
    }

    [Fact]
    public void On_a_current_run_each_part_is_what_the_run_says()
    {
        var run = Run("9c41e2a0", ("statuses-are-set-up", ScenarioOutcome.Passed), ("an-item-is-moved", ScenarioOutcome.Failed));

        Assert.Equal(ScenarioState.Passed, ScenarioReferences.PartState("9C41E2A0", run, "statuses-are-set-up"));
        Assert.Equal(ScenarioState.Failed, ScenarioReferences.PartState("9c41e2a0", run, "an-item-is-moved"));
    }

    [Fact]
    public void A_part_a_current_run_did_not_reach_was_never_run()
    {
        var run = Run("9c41e2a0", ("statuses-are-set-up", ScenarioOutcome.NotRun));

        Assert.Equal(ScenarioState.NeverRun, ScenarioReferences.PartState("9c41e2a0", run, "statuses-are-set-up"));
        Assert.Equal(ScenarioState.NeverRun, ScenarioReferences.PartState("9c41e2a0", run, "an-item-is-moved"));
    }

    [Theory]
    [InlineData("an-item-is-moved")]
    [InlineData("An-Item-Is-Moved")]
    [InlineData("An item is moved")]
    public void A_part_is_found_by_its_anchor_however_it_was_typed(string anchor)
    {
        var page = ScenarioPageParser.Parse(Page, ".devbook/domain/work/set-up-and-fill-the-backlog.md");

        Assert.Equal("An item is moved", ScenarioReferences.FindPart(page, anchor)?.Title);
    }

    [Fact]
    public void An_anchor_no_part_has_finds_none()
    {
        var page = ScenarioPageParser.Parse(Page, ".devbook/domain/work/set-up-and-fill-the-backlog.md");

        Assert.Null(ScenarioReferences.FindPart(page, "renamed-away"));
    }

    private const string Requirements = """
        # Requirements

        ### Requirement: Columns follow the status order

        ```meta
        type: requirement
        ```

        #### Scenario: Statuses are set up

        Proved by: set-up-and-fill-the-backlog.md#statuses-are-set-up

        #### Scenario: An item is moved

        Proved by: ../work/set-up-and-fill-the-backlog.md#an-item-is-moved

        ```text
        Proved by: example.md#not-a-pointer
        ```

        ### Requirement: Items keep their order

        #### Scenario: Reordered

        Proved by: .devbook/domain/work/reorder.md#reordered
        """;

    [Fact]
    public void A_requirement_chapter_names_the_pointers_under_it_and_none_in_a_fence()
    {
        var targets = ScenarioReferences.ProvedByTargets(Requirements, "requirement-columns-follow-the-status-order");

        Assert.Equal(
            ["set-up-and-fill-the-backlog.md#statuses-are-set-up", "../work/set-up-and-fill-the-backlog.md#an-item-is-moved"],
            targets);
    }

    [Fact]
    public void A_whole_page_names_every_pointer_on_it() =>
        Assert.Equal(3, ScenarioReferences.ProvedByTargets(Requirements, null).Count);

    [Fact]
    public void An_anchor_no_heading_has_names_no_pointer() =>
        Assert.Empty(ScenarioReferences.ProvedByTargets(Requirements, "gone"));

    [Fact]
    public void A_pointer_is_a_repository_path_a_path_from_the_page_or_a_stem()
    {
        const string from = ".devbook/domain/board/requirements.md";

        Assert.Equal((".devbook/domain/work/reorder.md", "reordered", false), ScenarioReferences.ResolveTarget(from, ".devbook/domain/work/reorder.md#reordered"));
        Assert.Equal((".devbook/domain/work/set-up.md", "a-part", false), ScenarioReferences.ResolveTarget(from, "../work/set-up.md#a-part"));
        Assert.Equal(("set-up.md", "a-part", true), ScenarioReferences.ResolveTarget(from, "set-up.md#a-part"));
    }

    [Fact]
    public void A_root_layout_repository_path_is_the_repository_s_and_is_folded() =>
        Assert.Equal((".domain/work/set-up.md", "a-part", false), ScenarioReferences.ResolveTarget(".devbook/domain/board/requirements.md", ".domain/work/../work/set-up.md#a-part"));

    [Theory]
    [InlineData("*.md#a-part")]
    [InlineData("set-up?.md#a-part")]
    public void A_wildcard_stem_names_no_page(string target) =>
        Assert.Null(ScenarioReferences.ResolveTarget(".devbook/domain/board/requirements.md", target));

    [Theory]
    [InlineData("set-up.md")]
    [InlineData("set-up#a-part")]
    [InlineData("set-up.md#")]
    [InlineData("../../../../outside.md#a-part")]
    public void A_pointer_that_names_no_page_part_resolves_to_nothing(string target) =>
        Assert.Null(ScenarioReferences.ResolveTarget(".devbook/domain/board/requirements.md", target));
}
