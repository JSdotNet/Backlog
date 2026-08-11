using System.Text.Json;
using Backlog.Infrastructure.Claude;

namespace Backlog.Infrastructure.Claude.UnitTests;

/// <summary>
/// The report JSON Anthropic answers with, read the way the client reads it.
/// </summary>
public class ClaudeUsageClientTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void A_usage_bucket_keeps_cached_and_uncached_input_apart()
    {
        var bucket = ClaudeUsageClient.ReadUsageBucket(Parse(
            """
            {
              "starting_at": "2026-08-01T00:00:00Z",
              "ending_at": "2026-08-02T00:00:00Z",
              "results": [
                {
                  "uncached_input_tokens": 100,
                  "output_tokens": 40,
                  "cache_creation_input_tokens": 25,
                  "cache_read_input_tokens": 900,
                  "model": "claude-opus-4-6",
                  "api_key_id": "apikey_01",
                  "service_tier": "standard"
                }
              ]
            }
            """));

        var result = Assert.Single(bucket.Results);

        Assert.Equal("claude-opus-4-6", result.Model);
        Assert.Equal("apikey_01", result.ApiKeyId);
        Assert.Equal(100, result.Tokens.InputTokens);
        Assert.Equal(1025, result.Tokens.TotalInputTokens);
        Assert.Equal(1065, result.Tokens.TotalTokens);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), bucket.StartingAt);
    }

    [Fact]
    public void Cache_writes_split_by_lifetime_still_add_up()
    {
        // A message-shaped payload nests cache writes per time-to-live instead
        // of reporting one number.
        var tokens = ClaudeUsageClient.ReadTokens(Parse(
            """
            {
              "input_tokens": 10,
              "output_tokens": 5,
              "cache_read_input_tokens": 0,
              "cache_creation": { "ephemeral_5m_input_tokens": 7, "ephemeral_1h_input_tokens": 3 }
            }
            """));

        Assert.Equal(10, tokens.CacheCreationInputTokens);
        Assert.Equal(20, tokens.TotalInputTokens);
    }

    [Fact]
    public void A_bucket_with_no_results_is_empty_rather_than_broken()
    {
        var bucket = ClaudeUsageClient.ReadUsageBucket(Parse(
            """{ "starting_at": "2026-08-01T00:00:00Z", "ending_at": "2026-08-02T00:00:00Z" }"""));

        Assert.Empty(bucket.Results);
        Assert.Equal(0, bucket.Totals.TotalTokens);
    }

    [Fact]
    public void Costs_arrive_as_decimal_strings_and_stay_exact()
    {
        var bucket = ClaudeUsageClient.ReadCostBucket(Parse(
            """
            {
              "starting_at": "2026-08-01T00:00:00Z",
              "ending_at": "2026-08-02T00:00:00Z",
              "results": [
                { "currency": "USD", "amount": "12.34", "description": "Claude Opus 4.6 input" },
                { "currency": "USD", "amount": "0.07", "description": "Claude Opus 4.6 output" }
              ]
            }
            """));

        Assert.Equal(12.41m, bucket.Total);
        Assert.Equal("USD", bucket.Results[0].Currency);
    }

    [Fact]
    public void A_claude_code_day_reports_the_actor_and_what_they_shipped()
    {
        var day = ClaudeUsageClient.ReadClaudeCodeDay(
            Parse(
                """
                {
                  "date": "2026-08-01",
                  "actor": { "type": "user_actor", "email_address": "me@example.com" },
                  "core_metrics": {
                    "num_sessions": 4,
                    "lines_of_code": { "added": 320, "removed": 45 },
                    "commits_by_claude_code": 6,
                    "pull_requests_by_claude_code": 2
                  },
                  "model_breakdown": [
                    {
                      "model": "claude-opus-4-6",
                      "tokens": { "input": 0, "uncached_input_tokens": 50, "output_tokens": 900, "cache_read_input_tokens": 100 },
                      "estimated_cost": { "currency": "USD", "amount": "1.25" }
                    }
                  ]
                }
                """),
            new DateOnly(2026, 1, 1));

        Assert.Equal(new DateOnly(2026, 8, 1), day.Date);
        Assert.Equal("me@example.com", day.Actor);
        Assert.Equal(4, day.Sessions);
        Assert.Equal(320, day.LinesAdded);
        Assert.Equal(2, day.PullRequests);

        var model = Assert.Single(day.Models);
        Assert.Equal(1.25m, model.EstimatedCost);
        Assert.Equal(150, model.Tokens.TotalInputTokens);
    }

    [Fact]
    public void A_claude_code_day_without_a_date_falls_back_to_the_day_asked_for()
    {
        var day = ClaudeUsageClient.ReadClaudeCodeDay(Parse("{}"), new DateOnly(2026, 8, 9));

        Assert.Equal(new DateOnly(2026, 8, 9), day.Date);
        Assert.Null(day.Actor);
        Assert.Empty(day.Models);
    }

    [Fact]
    public async Task Usage_is_unavailable_and_says_why_when_no_key_is_configured()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ClaudeSettingsStore(directory.File("claude.json"));
        var client = new ClaudeUsageClient(new StubTransport(available: false), settings);

        var availability = await client.GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("individual accounts", availability.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_regular_api_key_is_rejected_before_the_request_leaves()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ClaudeSettingsStore(directory.File("claude.json"));
        settings.SetAdminApiKey("sk-ant-api03-not-an-admin-key");

        var client = new ClaudeUsageClient(new StubTransport(available: true), settings);

        var availability = await client.GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("sk-ant-admin", availability.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_admin_key_makes_usage_available()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ClaudeSettingsStore(directory.File("claude.json"));
        settings.SetAdminApiKey("sk-ant-admin01-example");

        var client = new ClaudeUsageClient(new StubTransport(available: true), settings);

        Assert.True((await client.GetAvailabilityAsync()).IsAvailable);
    }

    [Fact]
    public async Task A_window_that_ends_before_it_starts_is_refused()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ClaudeSettingsStore(directory.File("claude.json"));
        var client = new ClaudeUsageClient(new StubTransport(available: true), settings);

        var now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<ClaudeException>(() =>
            client.GetMessageUsageAsync(new ClaudeUsageWindow(now, now.AddDays(-1))));
    }

    [Fact]
    public async Task The_messages_report_asks_for_daily_buckets_and_the_configured_workspace()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ClaudeSettingsStore(directory.File("claude.json"));
        settings.SetAdminApiKey("sk-ant-admin01-example");
        settings.SetWorkspaceId("wrkspc_01");

        var transport = new StubTransport(available: true, response: """{ "data": [], "has_more": false }""");
        var client = new ClaudeUsageClient(transport, settings);

        await client.GetMessageUsageAsync(
            new ClaudeUsageWindow(
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero)));

        var path = Assert.Single(transport.Paths);

        Assert.StartsWith("v1/organizations/usage_report/messages?", path, StringComparison.Ordinal);
        Assert.Contains("bucket_width=1d", path, StringComparison.Ordinal);
        Assert.Contains("workspace_ids[]=wrkspc_01", path, StringComparison.Ordinal);
        Assert.Contains("starting_at=2026-08-01T00%3A00%3A00Z", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cost_report_only_ever_asks_for_daily_buckets()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ClaudeSettingsStore(directory.File("claude.json"));
        settings.SetAdminApiKey("sk-ant-admin01-example");

        var transport = new StubTransport(available: true, response: """{ "data": [], "has_more": false }""");
        var client = new ClaudeUsageClient(transport, settings);

        await client.GetCostAsync(ClaudeUsageWindow.LastDays(7));

        Assert.Contains("bucket_width=1d", Assert.Single(transport.Paths), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paging_is_followed_to_the_end()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ClaudeSettingsStore(directory.File("claude.json"));
        settings.SetAdminApiKey("sk-ant-admin01-example");

        var transport = new StubTransport(available: true, responses:
        [
            """
            { "data": [ { "starting_at": "2026-08-01T00:00:00Z", "ending_at": "2026-08-02T00:00:00Z",
              "results": [ { "uncached_input_tokens": 10, "output_tokens": 1 } ] } ],
              "has_more": true, "next_page": "page_2" }
            """,
            """
            { "data": [ { "starting_at": "2026-08-02T00:00:00Z", "ending_at": "2026-08-03T00:00:00Z",
              "results": [ { "uncached_input_tokens": 20, "output_tokens": 2 } ] } ],
              "has_more": false }
            """
        ]);

        var client = new ClaudeUsageClient(transport, settings);

        var report = await client.GetMessageUsageAsync(ClaudeUsageWindow.LastDays(2));

        Assert.Equal(2, report.Buckets.Count);
        Assert.Equal(30, report.Totals.InputTokens);
        Assert.Contains("page=page_2", transport.Paths[1], StringComparison.Ordinal);
    }

    private sealed class StubTransport : IClaudeTransport
    {
        private readonly bool _available;
        private readonly IReadOnlyList<string> _responses;

        public StubTransport(bool available, string? response = null, IReadOnlyList<string>? responses = null)
        {
            _available = available;
            _responses = responses ?? [response ?? "null"];
        }

        public List<string> Paths { get; } = [];

        public string Description => "stub";

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(_available);

        public Task<JsonElement> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken = default)
        {
            var index = Math.Min(Paths.Count, _responses.Count - 1);
            Paths.Add(path);
            return Task.FromResult(Parse(_responses[index]));
        }
    }
}

/// <summary>A throwaway folder so settings tests never touch the real per-user file.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"backlog-claude-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }
}
