using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>list_run_usage</c>: every run in a window, across every repository, with the token
/// figures <c>get_run</c> leaves out — measured, and null where nothing measured them.
/// </summary>
public class UsageToolsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>
    /// The default window is the week back from now, and a run is placed by its start: one
    /// that started eight days ago and was still being written yesterday is last week's.
    /// </summary>
    [Fact]
    public async Task The_default_window_is_the_last_seven_days_by_start()
    {
        var tools = Tools(
            Run("inside", started: Now.AddDays(-2)),
            Run("too-old", started: Now.AddDays(-8), updated: Now.AddDays(-1)),
            Run("undated", undated: true, updated: Now.AddHours(-3)));

        var answer = await tools.ListRunUsageAsync(cancellationToken: Cancel);

        Assert.Equal(Now.AddDays(-7), answer.From);
        Assert.Equal(Now, answer.To);
        Assert.Equal(["inside", "undated"], answer.Runs.Select(run => run.Id).Order());
        Assert.Equal(2, answer.Count);
    }

    /// <summary>A range is whole UTC days, the last one included.</summary>
    [Fact]
    public async Task A_range_runs_from_its_first_day_through_its_last()
    {
        var tools = Tools(
            Run("first-morning", started: new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero)),
            Run("last-night", started: new DateTimeOffset(2026, 10, 21, 23, 59, 0, TimeSpan.Zero)),
            Run("day-after", started: new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero)),
            Run("day-before", started: new DateTimeOffset(2026, 10, 19, 23, 59, 0, TimeSpan.Zero)));

        var answer = await tools.ListRunUsageAsync(from: new DateOnly(2026, 10, 20), to: new DateOnly(2026, 10, 21), cancellationToken: Cancel);

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), answer.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), answer.To);
        Assert.Equal(["first-morning", "last-night"], answer.Runs.Select(run => run.Id).Order());
    }

    /// <summary>A window that cannot be read as one is refused in words naming what is
    /// wrong, rather than answered with an empty list that reads as a quiet week.</summary>
    [Theory]
    [InlineData(0, null, null, "usage.days_out_of_range")]
    [InlineData(367, null, null, "usage.days_out_of_range")]
    [InlineData(null, "2026-10-21", "2026-10-20", "usage.window_reversed")]
    [InlineData(null, null, "2026-10-20", "usage.to_without_from")]
    public async Task A_window_that_is_not_one_is_refused(int? days, string? from, string? to, string code)
    {
        var tools = Tools(Run("any", started: Now.AddDays(-1)));

        var refusal = await Assert.ThrowsAsync<McpException>(() => tools.ListRunUsageAsync(
            days,
            from is null ? null : DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            to is null ? null : DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
            Cancel));

        Assert.StartsWith(code, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bucket carries the four counts and the two sums a cost analysis ranks on: every
    /// token moved, and the uncached figure — the cache reads left out, since the cached
    /// prompt is served again on every turn.
    /// </summary>
    [Fact]
    public async Task A_run_carries_its_totals_and_its_uncached_figure()
    {
        var tools = Tools(Run(
            "measured",
            usage: new DeliveryRunTokenUsage(
                Tokens(calls: 10, input: 100, output: 50, cacheRead: 10_000, cacheWrite: 400),
                Tokens(calls: 4, input: 40, output: 20, cacheRead: 3_000, cacheWrite: 100),
                [new DeliveryRunStageTokens("Implement", Tokens(calls: 6, input: 60, output: 30, cacheRead: 6_000, cacheWrite: 300), Tokens(calls: 4, input: 40, output: 20, cacheRead: 3_000, cacheWrite: 100))],
                ["claude-opus-5-5"])));

        var run = Assert.Single((await tools.ListRunUsageAsync(cancellationToken: Cancel)).Runs);

        var total = Assert.IsType<UsageTokensPayload>(run.Total);
        Assert.Equal(10, total.ModelCalls);
        Assert.Equal(10_550, total.Tokens);
        Assert.Equal(550, total.UncachedTokens);
        Assert.Equal(160, run.SubAgent!.UncachedTokens);

        var stage = Assert.Single(run.Stages);
        Assert.Equal("Implement", stage.Stage);
        Assert.Equal(390, stage.Total.UncachedTokens);
        Assert.Equal(160, stage.SubAgent.UncachedTokens);
    }

    /// <summary>A run that recorded no usage answers with nulls and empty lists — never a
    /// zero nobody measured.</summary>
    [Fact]
    public async Task A_run_with_nothing_recorded_has_no_figures_rather_than_zeroes()
    {
        var tools = Tools(Run("silent"));

        var answer = await tools.ListRunUsageAsync(cancellationToken: Cancel);
        var run = Assert.Single(answer.Runs);

        Assert.Null(run.Total);
        Assert.Null(run.SubAgent);
        Assert.Empty(run.Stages);
        Assert.Empty(run.Models);
        Assert.Empty(run.SubAgents);
        Assert.Null(run.PeakContextTokens);
        Assert.Null(run.Compactions);

        var repository = Assert.Single(answer.Repositories);
        Assert.Equal(1, repository.RunsWithoutUsage);
        Assert.Null(repository.Total);
    }

    /// <summary>
    /// Per model, the figures are Claude Code's own report of the requests the run's
    /// sessions made while it was open — once per request id, and not the ones before it
    /// started or after it ended.
    /// </summary>
    [Fact]
    public async Task Models_are_read_from_the_requests_claude_code_reported_while_the_run_was_open()
    {
        var started = Now.AddDays(-1);
        var run = Run(
            "reported",
            started: started,
            updated: started.AddHours(2),
            status: "done",
            sessionIds: ["s1"],
            usage: new DeliveryRunTokenUsage(Tokens(), Tokens(), [], ["claude-opus-5-5", "claude-haiku-4-5"]));

        var opus = Request("r1", "s1", started.AddMinutes(5), "claude-opus-5-5", input: 100, output: 10, cacheRead: 1_000, cacheWrite: 50, costMicros: 120_000);
        var store = new FakeClaudeApiRequestStore(
            opus,
            opus,
            Request("r2", "s1", started.AddMinutes(30), "claude-opus-5-5", input: 200, output: 20, cacheRead: 2_000, cacheWrite: 0, costMicros: 80_000),
            Request("r3", "s1", started.AddMinutes(40), "claude-haiku-4-5", input: 5, output: 5, cacheRead: 0, cacheWrite: 0, costMicros: null),
            Request("before", "s1", started.AddMinutes(-10), "claude-opus-5-5", input: 9_999, output: 0, cacheRead: 0, cacheWrite: 0, costMicros: 1),
            Request("after", "s1", started.AddHours(3), "claude-opus-5-5", input: 9_999, output: 0, cacheRead: 0, cacheWrite: 0, costMicros: 1),
            Request("other", "s2", started.AddMinutes(5), "claude-opus-5-5", input: 9_999, output: 0, cacheRead: 0, cacheWrite: 0, costMicros: 1));

        var answer = await Tools(store, run).ListRunUsageAsync(cancellationToken: Cancel);

        var models = Assert.Single(answer.Runs).Models;
        Assert.Equal(["claude-opus-5-5", "claude-haiku-4-5"], models.Select(model => model.Model));
        Assert.All(models, model => Assert.Equal("reported", model.Source));

        var heaviest = models[0];
        Assert.Equal(2, heaviest.Tokens!.ModelCalls);
        Assert.Equal(300, heaviest.Tokens.InputTokens);
        Assert.Equal(3_000, heaviest.Tokens.CacheReadTokens);
        Assert.Equal(380, heaviest.Tokens.UncachedTokens);
        Assert.Equal(0.2m, heaviest.ReportedCostUsd);

        Assert.Null(models[1].ReportedCostUsd);
    }

    /// <summary>
    /// A session that drove two runs has each request counted once, by the run it fell
    /// in; and a run abandoned while marked under way stops spending where the next run
    /// of its session started, rather than taking that run's requests as its own.
    /// </summary>
    [Fact]
    public async Task A_request_belongs_to_one_run_and_an_abandoned_run_ends_where_the_next_began()
    {
        var first = Now.AddDays(-2);
        var second = Now.AddDays(-1);

        var abandoned = Run("abandoned", started: first, updated: first.AddHours(1), status: "in_progress", sessionIds: ["s1"]);
        var next = Run("next", started: second, updated: second.AddHours(1), status: "done", sessionIds: ["s1"]);

        var store = new FakeClaudeApiRequestStore(
            Request("early", "s1", first.AddMinutes(10), "claude-opus-5-5", input: 1, output: 1, cacheRead: 0, cacheWrite: 0, costMicros: null),
            Request("late", "s1", second.AddMinutes(10), "claude-opus-5-5", input: 7, output: 1, cacheRead: 0, cacheWrite: 0, costMicros: null),
            Request("after-both", "s1", second.AddHours(5), "claude-opus-5-5", input: 50, output: 0, cacheRead: 0, cacheWrite: 0, costMicros: null));

        var answer = await Tools(store, abandoned, next).ListRunUsageAsync(cancellationToken: Cancel);
        var runs = answer.Runs.ToDictionary(run => run.Id);

        Assert.Equal(1, Assert.Single(runs["abandoned"].Models).Tokens!.ModelCalls);
        Assert.Equal(1, Assert.Single(runs["abandoned"].Models).Tokens!.InputTokens);
        Assert.Equal(7, Assert.Single(runs["next"].Models).Tokens!.InputTokens);
    }

    /// <summary>A model whose requests carried no token count has null tokens, not zeroes.</summary>
    [Fact]
    public async Task A_model_whose_requests_counted_no_tokens_has_none()
    {
        var run = Run("uncounted", started: Now.AddHours(-5), updated: Now.AddHours(-4), sessionIds: ["s1"]);
        var store = new FakeClaudeApiRequestStore(
            new ClaudeApiRequest("r1", "s1", Now.AddHours(-5).AddMinutes(1), "claude-opus-5-5", null, 5_000, null, null, null, null, null, null, null, null, null));

        var model = Assert.Single(Assert.Single((await Tools(store, run).ListRunUsageAsync(cancellationToken: Cancel)).Runs).Models);

        Assert.Equal("reported", model.Source);
        Assert.Null(model.Tokens);
        Assert.Equal(0.005m, model.ReportedCostUsd);
    }

    /// <summary>A gauge that read 0 of 0 was never sampled: its peak and limit are null,
    /// while the compactions the telemetry listed beside it still count.</summary>
    [Fact]
    public async Task An_unsampled_context_has_no_peak_but_keeps_its_compactions()
    {
        var tools = Tools(Run("unsampled", context: new DeliveryRunContext(0, 0, 0) { Compactions = 1 }));

        var run = Assert.Single((await tools.ListRunUsageAsync(cancellationToken: Cancel)).Runs);

        Assert.Null(run.PeakContextTokens);
        Assert.Null(run.ContextLimit);
        Assert.Equal(1, run.Compactions);
    }

    /// <summary>A run whose sessions reported no requests still names its models, with no
    /// figures beside them — what ran, without a guess at how much.</summary>
    [Fact]
    public async Task Without_reported_requests_a_run_names_its_models_and_nothing_more()
    {
        var tools = Tools(Run(
            "named",
            sessionIds: ["s1"],
            usage: new DeliveryRunTokenUsage(Tokens(), Tokens(), [], ["claude-opus-5-5"])));

        var model = Assert.Single(Assert.Single((await tools.ListRunUsageAsync(cancellationToken: Cancel)).Runs).Models);

        Assert.Equal("claude-opus-5-5", model.Model);
        Assert.Equal("named", model.Source);
        Assert.Null(model.Tokens);
        Assert.Null(model.ReportedCostUsd);
    }

    /// <summary>
    /// The run's delegated agents are summed over its stages, most tokens first. A stage
    /// that recorded its calls one by one is read from them; one that only declared its
    /// agents gives their call count and no tokens.
    /// </summary>
    [Fact]
    public async Task Sub_agents_are_summed_over_the_stages_they_ran_in()
    {
        var tools = Tools(Run(
            "delegated",
            stages:
            [
                new DeliveryRunStage("Review", "done", 1_000, 1)
                {
                    SubAgentRuns =
                    [
                        new DeliveryRunSubAgentRun("reviewer", "claude-sonnet-5", 60_000, 30_000, 12, Failed: false),
                        new DeliveryRunSubAgentRun("reviewer", "claude-sonnet-5", 30_000, 10_000, 4, Failed: true)
                    ]
                },
                new DeliveryRunStage("Verify", "done", 1_000, 1)
                {
                    SubAgentRuns = [new DeliveryRunSubAgentRun("qa:qa", "claude-opus-5-5", 90_000, 90_000, 40, Failed: false)]
                },
                new DeliveryRunStage("Scope", "done", 1_000, 1)
                {
                    Agents = [new DeliveryRunStageAgent("architecture:architect", "claude-opus-5-5", Count: 2, Failed: 0)]
                }
            ]));

        var agents = Assert.Single((await tools.ListRunUsageAsync(cancellationToken: Cancel)).Runs).SubAgents;

        Assert.Equal(["qa:qa", "reviewer", "architecture:architect"], agents.Select(agent => agent.Agent));

        var reviewer = agents[1];
        Assert.Equal(2, reviewer.Calls);
        Assert.Equal(1, reviewer.Failed);
        Assert.Equal(40_000, reviewer.Tokens);
        Assert.Equal(90_000, reviewer.DurationMs);
        Assert.Equal(["claude-sonnet-5"], reviewer.Models);
        Assert.Equal(["Review"], reviewer.Stages);

        var declared = agents[2];
        Assert.Equal(2, declared.Calls);
        Assert.Null(declared.Tokens);
        Assert.Equal(["Scope"], declared.Stages);
    }

    /// <summary>The context peak and how often it was compacted travel with the run — the
    /// mark of a run outgrowing its window.</summary>
    [Fact]
    public async Task The_context_peak_and_compactions_travel_with_the_run()
    {
        var tools = Tools(Run("compacted", context: new DeliveryRunContext(90_000, 200_000, 198_000) { Compactions = 3 }));

        var run = Assert.Single((await tools.ListRunUsageAsync(cancellationToken: Cancel)).Runs);

        Assert.Equal(198_000, run.PeakContextTokens);
        Assert.Equal(200_000, run.ContextLimit);
        Assert.Equal(3, run.Compactions);
    }

    /// <summary>
    /// A run is placed in the repository it named, else the one its session recorded,
    /// else the one this machine placed that session under, else its first linked work
    /// item's — and a run nothing places is kept, under no repository, last.
    /// </summary>
    [Fact]
    public async Task Runs_are_placed_in_a_repository_by_what_named_one()
    {
        var sessions = new AgentSessionCatalog(
            [
                Sessions.Session("recorded", repository: "JSdotNet/devbook"),
                Sessions.Session("resolved", resolvedRepository: "JSdotNet/spec-manager")
            ],
            Unreadable: [],
            Discovered: 2);

        var tools = new UsageTools(
            new FakeDeliveryRunSource(new DeliveryRunCatalog(
                [
                    Run("named", repository: "JSdotNet/Backlog", sessionIds: ["recorded"]),
                    Run("by-session", sessionIds: ["recorded"]),
                    Run("by-folder", sessionIds: ["resolved"]),
                    Run("by-reference", references: [new DeliveryRunReference(DeliveryRunReferenceKind.Issue, "#12", null, null, "JSdotNet/Backlog")]),
                    Run("nowhere")
                ],
                Unreadable: ["orch-dashboard"])),
            new FakeAgentSessionSource(sessions),
            new FakeClaudeApiRequestStore(),
            new FakeTimeProvider(Now));

        var answer = await tools.ListRunUsageAsync(cancellationToken: Cancel);
        var placed = answer.Runs.ToDictionary(run => run.Id, run => run.Repository);

        Assert.Equal("JSdotNet/Backlog", placed["named"]);
        Assert.Equal("JSdotNet/devbook", placed["by-session"]);
        Assert.Equal("JSdotNet/spec-manager", placed["by-folder"]);
        Assert.Equal("JSdotNet/Backlog", placed["by-reference"]);
        Assert.Null(placed["nowhere"]);

        Assert.Equal(
            ["JSdotNet/Backlog", "JSdotNet/devbook", "JSdotNet/spec-manager", null],
            answer.Repositories.Select(repository => repository.Repository));
        Assert.Equal(2, answer.Repositories[0].Runs);
        Assert.Equal("nowhere", answer.Runs[^1].Id);
        Assert.Equal(["orch-dashboard"], answer.Unreadable);
    }

    /// <summary>A repository's totals are its runs' totals summed, uncached figure
    /// included.</summary>
    [Fact]
    public async Task A_repository_sums_its_runs()
    {
        var tools = Tools(
            Run("one", repository: "JSdotNet/Backlog", usage: Usage(Tokens(calls: 2, input: 10, output: 5, cacheRead: 100, cacheWrite: 1))),
            Run("two", repository: "jsdotnet/backlog", usage: Usage(Tokens(calls: 3, input: 20, output: 5, cacheRead: 200, cacheWrite: 2))),
            Run("three", repository: "JSdotNet/Backlog"));

        var repository = Assert.Single((await tools.ListRunUsageAsync(cancellationToken: Cancel)).Repositories);

        Assert.Equal(3, repository.Runs);
        Assert.Equal(1, repository.RunsWithoutUsage);
        Assert.Equal(5, repository.Total!.ModelCalls);
        Assert.Equal(343, repository.Total.Tokens);
        Assert.Equal(43, repository.Total.UncachedTokens);
    }

    private static UsageTools Tools(params DeliveryRun[] runs) => Tools(new FakeClaudeApiRequestStore(), runs);

    private static UsageTools Tools(FakeClaudeApiRequestStore store, params DeliveryRun[] runs) =>
        new(
            new FakeDeliveryRunSource(new DeliveryRunCatalog(runs, [])),
            new FakeAgentSessionSource(AgentSessionCatalog.Empty),
            store,
            new FakeTimeProvider(Now));

    private static DeliveryRun Run(
        string id,
        DateTimeOffset? started = null,
        DateTimeOffset? updated = null,
        bool undated = false,
        string status = "done",
        string? repository = null,
        IReadOnlyList<string>? sessionIds = null,
        DeliveryRunTokenUsage? usage = null,
        DeliveryRunContext? context = null,
        IReadOnlyList<DeliveryRunStage>? stages = null,
        IReadOnlyList<DeliveryRunReference>? references = null)
    {
        // A run file that does not date its start is asked for by name.
        DateTimeOffset? start = undated ? null : started ?? Now.AddDays(-1);

        return Runs.Run(id, worktree: $"wt-{id}", status: status, stages: stages ?? [], sessionIds: sessionIds) with
        {
            StartedAt = start,
            UpdatedAt = updated ?? (start ?? Now).AddHours(1),
            Repository = repository,
            TokenUsage = usage,
            Context = context,
            References = references ?? []
        };
    }

    private static DeliveryRunTokens Tokens(long calls = 0, long input = 0, long output = 0, long cacheRead = 0, long cacheWrite = 0) =>
        new(calls, input, output, ReasoningTokens: 0, cacheRead, cacheWrite);

    private static DeliveryRunTokenUsage Usage(DeliveryRunTokens total) => new(total, Tokens(), [], []);

    private static ClaudeApiRequest Request(
        string id,
        string session,
        DateTimeOffset at,
        string model,
        long input,
        long output,
        long cacheRead,
        long cacheWrite,
        long? costMicros) =>
        new(id, session, at, model, Effort: null, costMicros, input, output, cacheRead, cacheWrite, DurationMs: 100, QuerySource: null, AgentName: null, SkillName: null, PromptId: null);
}

/// <summary>Every run on the machine, as one catalog.</summary>
internal sealed class FakeDeliveryRunSource(DeliveryRunCatalog catalog) : IDeliveryRunSource
{
    public Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default) => Task.FromResult(catalog);
}

/// <summary>The stored <c>api_request</c> events, filtered by session the way the store
/// filters them.</summary>
internal sealed class FakeClaudeApiRequestStore(params ClaudeApiRequest[] requests) : IClaudeApiRequestStore
{
    public Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> added, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("list_run_usage reads; it never stores a request.");

    public Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClaudeApiRequest>>(
            [.. requests.Where(request => sessionId is null || request.SessionId == sessionId)]);
}
