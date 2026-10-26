using Backlog.Infrastructure.Devbook.Scenarios;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// <c>run.json</c> in both shapes a repository commits, the register, and the state
/// the dot shows.
/// </summary>
public sealed class ScenarioRunReaderTests
{
    private const string Page = """
        # Scenarios

        ```meta
        type: scenarios
        ```

        ## Set up and fill the backlog

        ```meta
        type: scenario
        start: /products/webshop/backlog
        actor: actors.md#product-owner
        ```

        ### Statuses are set up
        - **Given** a product "Webshop"
        - **Then** the board has three columns

        ![The board](shot:statuses-set-up)

        ### An item is moved
        - **When** I drag it to "Doing"

        ![After the drag](shot:item-moved)
        """;

    private static ScenarioJourney Journey(string markdown = Page, string path = ".devbook/domain/work/scenarios.md") =>
        Assert.Single(ScenarioPageParser.ParseFile(markdown, path).Scenarios);

    [Fact]
    public void A_version_3_run_names_the_journey_it_executed()
    {
        var run = ScenarioRunReader.Read("""
            {
              "version": 3,
              "journey": ".devbook/domain/work/scenarios.md#set-up-and-fill-the-backlog",
              "signature": "9C41E2A0",
              "ranAt": "2026-10-07T08:30:00Z",
              "profile": "default",
              "parts": [ { "title": "Statuses are set up", "anchor": "statuses-are-set-up", "outcome": "passed" } ],
              "shots": {}
            }
            """);

        Assert.NotNull(run);
        Assert.Equal(3, run.Version);
        Assert.Equal("9c41e2a0", run.Signature);
        Assert.Equal(".devbook/domain/work/scenarios.md#set-up-and-fill-the-backlog", run.Journey);
        Assert.Null(run.Page);
        Assert.Equal(ScenarioOutcome.Passed, run.Part("statuses-are-set-up")!.Outcome);
    }

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
    public void A_version_2_failed_part_carries_the_step_it_stopped_on_and_its_message()
    {
        var run = ScenarioRunReader.Read("""
            {
              "version": 2,
              "signature": "9c41e2a0",
              "parts": [
                { "title": "Statuses are set up", "anchor": "statuses-are-set-up", "outcome": "passed" },
                { "title": "An item is moved", "anchor": "an-item-is-moved", "outcome": "failed",
                  "failure": { "step": 2, "message": "expected the card in Doing" } },
                { "title": "An item is archived", "anchor": "an-item-is-archived", "outcome": "failed",
                  "failure": { "step": null, "message": "timed out before the first step" } }
              ]
            }
            """);

        Assert.NotNull(run);
        Assert.Equal(2, run.Part("an-item-is-moved")!.FailureStep);
        Assert.Equal("expected the card in Doing", run.Part("an-item-is-moved")!.FailureMessage);
        Assert.Null(run.Part("an-item-is-archived")!.FailureStep);
        Assert.Equal("timed out before the first step", run.Part("an-item-is-archived")!.FailureMessage);
        Assert.Null(run.Part("statuses-are-set-up")!.FailureStep);
        Assert.Null(run.Part("statuses-are-set-up")!.FailureMessage);
    }

    /// <summary>
    /// The end-to-end tests register <c>tests/Fixtures/scenario-repository</c> and
    /// expect its journey to read failed — not stale — with the second part stopped on
    /// step 1. An edit that forgot to re-sign the committed run would turn that into a
    /// stale journey the browser test only finds once an AppHost is up; this finds it
    /// in the unit run.
    /// </summary>
    [Fact]
    public void The_scenario_repository_fixture_run_proves_its_journey_as_it_stands()
    {
        var root = RepositoryRoot.Directory("tests", "Fixtures", "scenario-repository");
        const string relative = ".devbook/domain/shop/scenarios.md";
        var journey = Assert.Single(ScenarioFiles.ReadFile(Path.Combine(root, ".devbook", "domain", "shop", "scenarios.md"), relative).Scenarios);
        var run = ScenarioFiles.ReadRun(Path.Combine(root, journey.RunFolder));

        Assert.Equal(".devbook/domain/shop/scenarios/place-an-order", journey.RunFolder);
        Assert.NotNull(run);
        Assert.Equal(journey.Address, run.Journey);
        Assert.Equal(run.Signature, ScenarioSignature.OfJourney(journey, root, ScenarioPageParser.ScenarioFolder));
        Assert.Equal(ScenarioState.Failed, ScenarioEvidence.StateOf(journey, run.Signature!, run));
        Assert.Equal(ScenarioOutcome.Passed, run.Part("the-cart-is-filled")!.Outcome);
        Assert.Equal(1, run.Part("the-order-is-placed")!.FailureStep);
    }

    [Fact]
    public void A_version_1_run_records_no_failure_step()
    {
        var run = ScenarioRunReader.Read("""
            { "versie": 1, "scenarios": [ { "titel": "An item is moved", "slug": "an-item-is-moved", "uitkomst": "gefaald" } ] }
            """);

        Assert.NotNull(run);
        Assert.Null(run.Part("an-item-is-moved")!.FailureStep);
        Assert.Null(run.Part("an-item-is-moved")!.FailureMessage);
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
    [InlineData("""{ "version": 4, "parts": [] }""")]
    [InlineData("""{ "versie": 2 }""")]
    public void A_missing_unreadable_or_unknown_run_is_no_run(string? json)
    {
        Assert.Null(ScenarioRunReader.Read(json));
    }

    [Fact]
    public void The_state_follows_never_run_then_stale_then_failed_then_passed()
    {
        var page = Journey();
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
        var page = Journey();
        var signature = ScenarioSignature.Of(page);
        var run = new ScenarioRun(2, signature, null, null, [new ScenarioRunPart("Statuses are set up", "statuses-are-set-up", ScenarioOutcome.Passed, null, null)], new Dictionary<string, ScenarioRunShot>());

        Assert.Equal(ScenarioState.Failed, ScenarioEvidence.StateOf(page, signature, run));
    }

    [Fact]
    public void Editing_a_step_changes_the_signature_and_editing_a_caption_does_not()
    {
        var page = Journey();
        var recaptioned = Journey(Page.Replace("![The board]", "![The empty board]", StringComparison.Ordinal));
        var restepped = Journey(Page.Replace("three columns", "four columns", StringComparison.Ordinal));
        var retitled = Journey(Page.Replace("## Set up and fill the backlog", "## Set up the backlog", StringComparison.Ordinal));

        Assert.Equal(ScenarioSignature.Of(page), ScenarioSignature.Of(recaptioned));
        Assert.Equal(ScenarioSignature.Of(page), ScenarioSignature.Of(retitled));
        Assert.NotEqual(ScenarioSignature.Of(page), ScenarioSignature.Of(restepped));
    }

    [Fact]
    public void The_parser_reads_setup_parts_steps_and_screenshot_points()
    {
        var page = Journey();

        Assert.Equal("set-up-and-fill-the-backlog", page.Slug);
        Assert.Equal(".devbook/domain/work/scenarios.md#set-up-and-fill-the-backlog", page.Address);
        Assert.Equal("Set up and fill the backlog", page.Title);
        Assert.Equal("/products/webshop/backlog", page.Setup.Start);
        Assert.Equal(["actors.md#product-owner"], page.Setup.Actor);
        Assert.Null(page.Setup.Profile);
        Assert.Equal(["statuses-are-set-up", "an-item-is-moved"], page.Parts.Select(part => part.Anchor));
        Assert.Equal("Given", page.Parts[0].Steps[0].Keyword);
        Assert.Equal("The board", page.Parts[0].Shots.Single().Caption);
        Assert.Equal(["statuses-set-up", "item-moved"], page.Labels);
    }

    /// <summary>A page of the contract before devbook's 32 — the whole file typed
    /// <c>scenario</c>, its parts at <c>##</c> — holds no journey: its <c>##</c>
    /// chapters carry no <c>type: scenario</c> block of their own.</summary>
    [Fact]
    public void A_legacy_journey_page_holds_no_journey()
    {
        var file = ScenarioPageParser.ParseFile(
            "# Place an order\n\n```meta\ntype: scenario\n```\n\n## The cart is filled\n- **Given** an empty cart\n",
            ".devbook/domain/shop/place-an-order.md");

        Assert.True(file.LegacyPage);
        Assert.Empty(file.Scenarios);
    }

    [Theory]
    [InlineData(".devbook/domain/work/scenarios.md", true)]
    [InlineData(".devbook/domain/work/scenarios.returns.md", true)]
    [InlineData(".domain/work/Scenarios.md", true)]
    [InlineData(".devbook/domain/work/scenarios.a.b.md", false)]
    [InlineData(".devbook/domain/work/features.md", false)]
    [InlineData(".devbook/domain/work/my-scenarios.md", false)]
    public void A_scenarios_file_is_named_scenarios_or_a_split_of_it(string path, bool expected) =>
        Assert.Equal(expected, ScenarioPageParser.IsScenariosFileName(path));

    [Fact]
    public void The_register_is_read_at_schema_version_2_only()
    {
        var register = ScenarioRegister.Parse("""
            { "schemaVersion": 2, "scope": ".", "scenarios": [
              { "id": ".devbook/domain/work/scenarios.md#set-up-and-fill-the-backlog", "path": ".devbook/domain/work/scenarios.md",
                "anchor": "set-up-and-fill-the-backlog", "line": 7, "title": "Set up and fill the backlog", "status": null }
            ] }
            """);

        Assert.NotNull(register);
        var entry = Assert.Single(register.Scenarios);
        Assert.Equal("set-up-and-fill-the-backlog", entry.Anchor);
        Assert.Equal(".devbook/domain/work/scenarios.md", entry.Path);
        Assert.Equal(".devbook/domain/work/scenarios.md#set-up-and-fill-the-backlog", entry.Address);
        Assert.Null(ScenarioRegister.Parse("""{ "schemaVersion": 1, "scenarios": [] }"""));
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
