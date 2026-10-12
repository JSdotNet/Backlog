using Backlog.Infrastructure.Devbook.Scenarios;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// <c>run.json</c> in both shapes a repository commits, the register, and the state
/// the dot shows.
/// </summary>
public sealed class ScenarioRunReaderTests
{
    private const string Page = """
        # Set up and fill the backlog

        ```meta
        type: scenario
        start: /products/webshop/backlog
        actor: actors.md#product-owner
        ```

        ## Statuses are set up
        - **Given** a product "Webshop"
        - **Then** the board has three columns

        ![The board](shot:statuses-set-up)

        ## An item is moved
        - **When** I drag it to "Doing"

        ![After the drag](shot:item-moved)
        """;

    [Fact]
    public void A_version_2_run_carries_its_signature_parts_and_shots()
    {
        var run = ScenarioRunReader.Read("""
            {
              "version": 2,
              "page": ".devbook/domain/work/set-up-and-fill-the-backlog.md",
              "signature": "9C41E2A0",
              "ranAt": "2026-10-07T08:30:00Z",
              "profile": "tenant-acme",
              "parts": [
                { "title": "Statuses are set up", "anchor": "statuses-are-set-up", "outcome": "passed", "durationMs": 3100 },
                { "title": "An item is moved", "anchor": "an-item-is-moved", "outcome": "failed", "durationMs": 900, "failureShot": "fail.an-item-is-moved.png" }
              ],
              "shots": {
                "statuses-set-up": { "file": "statuses-set-up.png", "part": "statuses-are-set-up", "ranAt": "2026-10-06T08:30:00Z" }
              }
            }
            """);

        Assert.NotNull(run);
        Assert.Equal(2, run.Version);
        Assert.Equal("9c41e2a0", run.Signature);
        Assert.Equal("tenant-acme", run.Profile);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 8, 30, 0, TimeSpan.Zero), run.RanAt);
        Assert.Equal(ScenarioOutcome.Failed, run.Part("an-item-is-moved")!.Outcome);
        Assert.Equal("fail.an-item-is-moved.png", run.Part("an-item-is-moved")!.FailureShot);
        Assert.Equal("statuses-set-up.png", run.Shots["statuses-set-up"].File);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 8, 30, 0, TimeSpan.Zero), run.Shots["statuses-set-up"].RanAt);
        Assert.Equal(1, run.Failed);
    }

    [Fact]
    public void A_version_1_run_reads_its_outcomes_and_prints_but_proves_no_signature()
    {
        var run = ScenarioRunReader.Read("""
            {
              "versie": 1,
              "feature": "Set up and fill the backlog",
              "gedraaidOp": "2026-09-01T10:00:00Z",
              "scenarios": [
                { "titel": "Statuses are set up", "slug": "statuses-are-set-up", "handtekening": "0a1b2c3d", "uitkomst": "geslaagd", "duurMs": 1200,
                  "afdrukken": [ { "bestand": "01-statuses-set-up.png", "naam": "statuses-set-up" } ] },
                { "titel": "An item is moved", "slug": "an-item-is-moved", "handtekening": "ffff0000", "uitkomst": "gefaald", "duurMs": 800, "afdrukken": [] }
              ]
            }
            """);

        Assert.NotNull(run);
        Assert.Equal(1, run.Version);
        Assert.Null(run.Signature);
        Assert.Equal(ScenarioOutcome.Passed, run.Part("statuses-are-set-up")!.Outcome);
        Assert.Equal(ScenarioOutcome.Failed, run.Part("an-item-is-moved")!.Outcome);
        Assert.Equal("statuses-are-set-up/01-statuses-set-up.png", run.Shots["statuses-set-up"].File);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), run.Shots["statuses-set-up"].RanAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "version": 3, "parts": [] }""")]
    [InlineData("""{ "versie": 2 }""")]
    public void A_missing_unreadable_or_unknown_run_is_no_run(string? json)
    {
        Assert.Null(ScenarioRunReader.Read(json));
    }

    [Fact]
    public void The_state_follows_never_run_then_stale_then_failed_then_passed()
    {
        var page = ScenarioPageParser.Parse(Page, ".devbook/domain/work/set-up-and-fill-the-backlog.md");
        var signature = ScenarioSignature.Of(page);

        Assert.Equal(ScenarioState.NeverRun, ScenarioEvidence.StateOf(page, signature, null));
        Assert.Equal(ScenarioState.Stale, ScenarioEvidence.StateOf(page, signature, Run("00000000", ScenarioOutcome.Failed)));
        Assert.Equal(ScenarioState.Failed, ScenarioEvidence.StateOf(page, signature, Run(signature, ScenarioOutcome.Failed)));
        Assert.Equal(ScenarioState.Passed, ScenarioEvidence.StateOf(page, signature, Run(signature, ScenarioOutcome.Passed)));
        Assert.Equal(ScenarioState.Stale, ScenarioEvidence.StateOf(page, signature, Run(signature, ScenarioOutcome.Passed) with { Signature = null }));
    }

    [Fact]
    public void A_part_the_run_never_reached_is_not_a_passed_page()
    {
        var page = ScenarioPageParser.Parse(Page, "x.md");
        var signature = ScenarioSignature.Of(page);
        var run = new ScenarioRun(2, signature, null, null, [new ScenarioRunPart("Statuses are set up", "statuses-are-set-up", ScenarioOutcome.Passed, null, null)], new Dictionary<string, ScenarioRunShot>());

        Assert.Equal(ScenarioState.Failed, ScenarioEvidence.StateOf(page, signature, run));
    }

    [Fact]
    public void Editing_a_step_changes_the_signature_and_editing_a_caption_does_not()
    {
        var page = ScenarioPageParser.Parse(Page, "x.md");
        var recaptioned = ScenarioPageParser.Parse(Page.Replace("![The board]", "![The empty board]", StringComparison.Ordinal), "x.md");
        var restepped = ScenarioPageParser.Parse(Page.Replace("three columns", "four columns", StringComparison.Ordinal), "x.md");

        Assert.Equal(ScenarioSignature.Of(page), ScenarioSignature.Of(recaptioned));
        Assert.NotEqual(ScenarioSignature.Of(page), ScenarioSignature.Of(restepped));
    }

    [Fact]
    public void The_parser_reads_setup_parts_steps_and_screenshot_points()
    {
        var page = ScenarioPageParser.Parse(Page, ".devbook/domain/work/set-up-and-fill-the-backlog.md");

        Assert.Equal("set-up-and-fill-the-backlog", page.Stem);
        Assert.Equal("Set up and fill the backlog", page.Title);
        Assert.Equal("/products/webshop/backlog", page.Setup.Start);
        Assert.Equal(["actors.md#product-owner"], page.Setup.Actor);
        Assert.Null(page.Setup.Profile);
        Assert.Equal(["statuses-are-set-up", "an-item-is-moved"], page.Parts.Select(part => part.Anchor));
        Assert.Equal("Given", page.Parts[0].Steps[0].Keyword);
        Assert.Equal("The board", page.Parts[0].Shots.Single().Caption);
        Assert.Equal(["statuses-set-up", "item-moved"], page.Labels);
    }

    [Fact]
    public void A_page_without_type_scenario_is_not_one()
    {
        Assert.False(ScenarioPageParser.IsScenarioPage("# Features\n\n```meta\ntype: features\n```\n"));
        Assert.True(ScenarioPageParser.IsScenarioPage(Page));
    }

    [Fact]
    public void The_register_is_read_at_schema_version_1_only()
    {
        var register = ScenarioRegister.Parse("""
            { "schemaVersion": 1, "scope": ".", "scenarios": [
              { "stem": "set-up-and-fill-the-backlog", "path": ".devbook/domain/work/set-up-and-fill-the-backlog.md", "title": "Set up and fill the backlog", "status": null }
            ] }
            """);

        Assert.NotNull(register);
        var entry = Assert.Single(register.Scenarios);
        Assert.Equal("set-up-and-fill-the-backlog", entry.Stem);
        Assert.Equal(".devbook/domain/work/set-up-and-fill-the-backlog.md", entry.Path);
        Assert.Null(ScenarioRegister.Parse("""{ "schemaVersion": 2, "scenarios": [] }"""));
        Assert.Null(ScenarioRegister.Parse("{"));
    }

    private static ScenarioRun Run(string signature, ScenarioOutcome second) =>
        new(2, signature, DateTimeOffset.UnixEpoch, null,
            [
                new ScenarioRunPart("Statuses are set up", "statuses-are-set-up", ScenarioOutcome.Passed, null, null),
                new ScenarioRunPart("An item is moved", "an-item-is-moved", second, null, null)
            ],
            new Dictionary<string, ScenarioRunShot>());
}
