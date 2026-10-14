using System.Text.Json;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// A run's cumulative Quality Facts, read off the run file: <c>finish_run</c>'s
/// <c>quality</c> first, the one <c>runContext</c> keeps while the run is under way
/// otherwise, and nothing — never zero — for a run file that carries neither. The
/// fixtures carry the facts the way the delivery contract's example writes them.
/// </summary>
public sealed class DeliveryQualityFactsTests : IDisposable
{
    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "backlog-quality-facts-tests",
        Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task A_finished_run_reads_the_facts_finish_run_sent_over_the_ones_its_run_context_kept()
    {
        var run = await ReadAsync("quality-facts.json");

        var facts = Assert.IsType<DeliveryQualityFacts>(run.Quality);
        Assert.Equal(3, facts.ReviewRounds);
        Assert.Equal(2, facts.HighestSliceRounds);
        Assert.Equal(3, facts.BlockerRounds);
        Assert.Equal(2, facts.BlockersOpen);
        Assert.Equal(2.0 / 3, facts.BlockersPerRound!.Value, 6);
        Assert.Equal(2, facts.EscapedDefects);
        Assert.Equal(1, facts.VerifyDefects);

        // The stages' latest passes read clean; the run's record does not.
        Assert.Equal(0, run.Stages[3].Blockers);
        Assert.Equal(0, run.Stages[5].DefectsFound);
        Assert.Equal(2, DeliveryEconomics.RoundsOf(run));
        Assert.Equal(3, DeliveryEconomics.ReviewRoundsOf(run));
        Assert.Equal(1, DeliveryEconomics.VerifyDefectsOf(run));
    }

    [Fact]
    public async Task A_run_under_way_reads_the_facts_its_run_context_keeps()
    {
        var run = await ReadAsync("quality-facts-running.json");

        var facts = Assert.IsType<DeliveryQualityFacts>(run.Quality);
        Assert.Equal(2, facts.ReviewRounds);
        Assert.Equal(2, facts.BlockerRounds);
        Assert.Equal(1, facts.BlockersOpen);

        // It sent no escaped defects yet: absent, and Verify falls back to its stage.
        Assert.Null(facts.EscapedDefects);
        Assert.Null(facts.VerifyDefects);
        Assert.Null(DeliveryEconomics.VerifyDefectsOf(run));
    }

    [Theory]
    [InlineData("quality-signals.json")]
    [InlineData("delegated-phase-map.json")]
    [InlineData("no-phase-map-runners.json")]
    public async Task A_run_file_without_the_facts_has_none_and_falls_back_to_its_stage_counts(string fixture)
    {
        var run = await ReadAsync(fixture);

        Assert.Null(run.Quality);
        Assert.Equal(DeliveryEconomics.RoundsOf(run), DeliveryEconomics.ReviewRoundsOf(run));
    }

    [Fact]
    public async Task The_stage_counts_still_answer_for_a_run_without_the_facts()
    {
        var run = await ReadAsync("quality-signals.json");

        Assert.Equal(2, DeliveryEconomics.RoundsOf(run));
        Assert.Equal(1, DeliveryEconomics.VerifyDefectsOf(run));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"quality":"three rounds"}""")]
    [InlineData("""{"quality":{}}""")]
    [InlineData("""{"quality":{"reviewRounds":{"total":-1},"blockers":{"rounds":[]}}}""")]
    [InlineData("""{"runContext":{"phases":{}}}""")]
    public void A_quality_object_that_states_no_fact_reads_as_none(string json) =>
        Assert.Null(DeliveryQualityFacts.Of(Parse(json)));

    [Fact]
    public void A_total_stands_without_its_slices_and_slices_without_their_total()
    {
        var total = DeliveryQualityFacts.Of(Parse("""{"quality":{"reviewRounds":{"total":4}}}"""))!;
        Assert.Equal((4, 4), (total.ReviewRounds!.Value, total.HighestSliceRounds!.Value));

        var slices = DeliveryQualityFacts.Of(Parse("""{"quality":{"reviewRounds":{"slices":[{"slice":"a","rounds":1},{"slice":"b","rounds":3}]}}}"""))!;
        Assert.Equal((4, 3), (slices.ReviewRounds!.Value, slices.HighestSliceRounds!.Value));
    }

    [Fact]
    public void No_escaped_defect_is_zero_at_Verify_too_and_items_count_where_no_total_is_given()
    {
        var none = DeliveryQualityFacts.Of(Parse("""{"quality":{"escapedDefects":{"total":0}}}"""))!;
        Assert.Equal((0, 0), (none.EscapedDefects!.Value, none.VerifyDefects!.Value));

        var items = DeliveryQualityFacts.Of(Parse("""{"quality":{"escapedDefects":{"items":[{"foundAt":"verify","summary":"x"},{"foundAt":"Ready","summary":"y"}]}}}"""))!;
        Assert.Equal((2, 1), (items.EscapedDefects!.Value, items.VerifyDefects!.Value));

        // A total with no items says how many escaped, not where: Verify's share is unknown.
        var untold = DeliveryQualityFacts.Of(Parse("""{"quality":{"escapedDefects":{"total":2}}}"""))!;
        Assert.Null(untold.VerifyDefects);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static string FixtureFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "delivery-runs");

    /// <summary>The fixture through the product's own reader, laid out as the Backlog
    /// surface lays its folder out.</summary>
    private async Task<DeliveryRun> ReadAsync(string fixture)
    {
        var folder = Path.Combine(_home, Path.GetFileNameWithoutExtension(fixture), "backlog", "fixture-1a2b3c4d", "runs");

        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(FixtureFolder, fixture), Path.Combine(folder, fixture), overwrite: true);

        var catalog = await new LocalDeliveryRunSource(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(folder)))!, "machine", "Machine").GetRunsAsync(TestContext.Current.CancellationToken);

        return Assert.Single(catalog.Runs);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless.
        }
    }
}
