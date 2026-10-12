namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// Pricing a run that is not on screen: its sessions' stored requests where they answer,
/// else its tokens at the person's prices, and a store that fails leaving the run on its
/// estimate rather than failing the caller.
/// </summary>
public sealed class DeliveryRunPricingTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static DeliveryRun Run(params string[] sessionIds) =>
        new("run-1", "backlog", "wt", "wt", "machine", "Machine", "flow-code", "A run", "done", null, [],
            Nine, Nine.AddHours(1),
            [new DeliveryRunStage("Implement", "done", 60_000, 1) { StartedAt = Nine, CompletedAt = Nine.AddMinutes(30) }],
            null, null, [], [])
        {
            SessionIds = sessionIds
        };

    private static ClaudeApiRequest Request(string id, string session, long costUsdMicros) =>
        new(id, session, Nine.AddMinutes(10), "claude-opus-5-5", null, costUsdMicros, 10, 10, 0, 0, 1_000, null, null, null, null);

    private sealed class Store(Func<string?, IReadOnlyList<ClaudeApiRequest>> list) : IClaudeApiRequestStore
    {
        public List<string?> Asked { get; } = [];

        public Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> requests, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default)
        {
            Asked.Add(sessionId);

            return Task.FromResult(list(sessionId));
        }
    }

    [Fact]
    public async Task A_run_is_priced_from_the_requests_of_every_session_it_names()
    {
        var store = new Store(session => session switch
        {
            "a" => [Request("r1", "a", 1_000_000)],
            "b" => [Request("r2", "b", 500_000)],
            _ => []
        });

        var costs = await DeliveryRunPricing.PriceAsync(Run("a", "b", "a"), "joined", null, store, ModelPriceTable.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], store.Asked);
        Assert.Equal(new CostFigure(1_500_000, CostSource.Reported), Assert.Single(costs).Stage);
    }

    [Fact]
    public async Task A_run_that_names_no_session_is_priced_from_the_session_it_was_joined_to()
    {
        var store = new Store(session => session == "joined" ? [Request("r1", "joined", 2_000_000)] : []);

        var costs = await DeliveryRunPricing.PriceAsync(Run(), "joined", null, store, ModelPriceTable.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(["joined"], store.Asked);
        Assert.Equal(2_000_000, Assert.Single(costs).Stage!.UsdMicros);
    }

    [Fact]
    public async Task A_store_that_fails_leaves_the_run_unpriced_rather_than_failing()
    {
        var store = new Store(_ => throw new InvalidOperationException("The database is locked."));

        var costs = await DeliveryRunPricing.PriceAsync(Run("a"), null, null, store, ModelPriceTable.Empty, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(costs).Stage);
    }

    [Fact]
    public async Task Without_a_store_the_run_is_priced_from_its_tokens_alone()
    {
        // A million uncached input tokens in the owner session, at $3 per million.
        var tokens = new DeliveryRunTokens(1, 1_000_000, 0, 0, 0, 0);
        var none = new DeliveryRunTokens(0, 0, 0, 0, 0, 0);
        var run = Run("a") with { TokenUsage = new DeliveryRunTokenUsage(tokens, none, [new DeliveryRunStageTokens("Implement", tokens, none)], ["claude-opus-5-5"]) };
        var prices = new ModelPriceTable([new ModelPrice("opus", 3m, 15m, 0.3m, 3.75m)]);

        var costs = await DeliveryRunPricing.PriceAsync(run, null, "claude-opus-5-5", null, prices, TestContext.Current.CancellationToken);

        Assert.Equal(new CostFigure(3_000_000, CostSource.Estimated), Assert.Single(costs).Stage);
    }

    [Fact]
    public async Task A_cancelled_read_is_not_mistaken_for_a_failed_store()
    {
        var store = new Store(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => DeliveryRunPricing.PriceAsync(Run("a"), null, null, store, ModelPriceTable.Empty, TestContext.Current.CancellationToken));
    }
}
