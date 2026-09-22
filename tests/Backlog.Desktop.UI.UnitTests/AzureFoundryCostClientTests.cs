using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Modules.Dashboard.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Cost Management client: the query it sends, the rows it reads back, and
/// the sentences it turns Azure's refusals into.
/// </summary>
public sealed class AzureFoundryCostClientTests : IDisposable
{
    private const string Scope = "/subscriptions/abc/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/ai";

    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is null) continue;

            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Without_a_cost_scope_the_client_is_unavailable_and_points_at_settings()
    {
        var handler = new RecordingHandler(_ => Ok("{}"));
        var client = new AzureFoundryCostClient(new HttpClient(handler), Settings(scope: null), new FixedToken());

        var availability = await client.GetAvailabilityAsync(TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.GetDailyCostAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 22), TestContext.Current.CancellationToken));

        Assert.False(availability.IsAvailable);
        Assert.Contains("resource ID", availability.Reason);
        Assert.Contains("Settings", availability.Reason);
        Assert.Equal(availability.Reason, ex.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Without_an_azure_sign_in_the_client_is_unavailable_with_the_sign_in_reason()
    {
        var handler = new RecordingHandler(_ => Ok("{}"));
        var client = new AzureFoundryCostClient(
            new HttpClient(handler),
            Settings(Scope),
            new DeveloperSignInTokenSource(new RefusingCredential()));

        var availability = await client.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.False(availability.IsAvailable);
        Assert.Contains("az login", availability.Reason);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task With_a_scope_and_a_sign_in_the_client_is_available()
    {
        var client = new AzureFoundryCostClient(new HttpClient(new RecordingHandler(_ => Ok("{}"))), Settings(Scope), new FixedToken());

        var availability = await client.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public async Task The_query_is_a_daily_actual_cost_by_meter_at_the_scope_with_the_bearer()
    {
        var handler = new RecordingHandler(_ => Ok(Page(rows: "[]")));
        var client = new AzureFoundryCostClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://management.example/") },
            Settings(Scope),
            new FixedToken("tok"));

        var report = await client.GetDailyCostAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 22), TestContext.Current.CancellationToken);

        Assert.Empty(report.Lines);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            "https://management.example" + Scope + "/providers/Microsoft.CostManagement/query?api-version=2023-11-01",
            handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("tok", handler.Request.Headers.Authorization.Parameter);

        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal("ActualCost", root.GetProperty("type").GetString());
        Assert.Equal("Custom", root.GetProperty("timeframe").GetString());
        Assert.Equal("2026-09-01T00:00:00Z", root.GetProperty("timePeriod").GetProperty("from").GetString());
        Assert.Equal("2026-09-22T23:59:59Z", root.GetProperty("timePeriod").GetProperty("to").GetString());
        var dataset = root.GetProperty("dataset");
        Assert.Equal("Daily", dataset.GetProperty("granularity").GetString());
        Assert.Equal("Cost", dataset.GetProperty("aggregation").GetProperty("totalCost").GetProperty("name").GetString());
        Assert.Equal("Meter", dataset.GetProperty("grouping")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rows_are_read_by_column_name_and_zero_charges_are_dropped()
    {
        // Columns in an order the client did not ask for, to prove it reads names.
        var handler = new RecordingHandler(_ => Ok("""
            {
              "properties": {
                "nextLink": null,
                "columns": [
                  { "name": "Currency", "type": "String" },
                  { "name": "Meter", "type": "String" },
                  { "name": "UsageDate", "type": "Number" },
                  { "name": "Cost", "type": "Number" }
                ],
                "rows": [
                  [ "EUR", "gpt-5.4 Input Tokens", 20260901, 1.25 ],
                  [ "EUR", "gpt-5.4 Output Tokens", 20260901, 0 ],
                  [ "EUR", "gpt-5.4 Output Tokens", 20260902, 3.5 ]
                ]
              }
            }
            """));
        var client = new AzureFoundryCostClient(new HttpClient(handler), Settings(Scope), new FixedToken());

        var report = await client.GetDailyCostAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new AzureFoundryCostLine(new DateOnly(2026, 9, 1), "gpt-5.4 Input Tokens", 1.25m, "EUR"),
                new AzureFoundryCostLine(new DateOnly(2026, 9, 2), "gpt-5.4 Output Tokens", 3.5m, "EUR")
            ],
            report.Lines);
    }

    [Fact]
    public async Task Paging_follows_next_link_to_the_end()
    {
        var calls = 0;
        var handler = new RecordingHandler(request =>
        {
            calls++;
            return request.RequestUri!.ToString().Contains("skiptoken", StringComparison.Ordinal)
                ? Ok(Page(rows: """[[ 2, 20260902, "b", "USD" ]]"""))
                : Ok(Page(rows: """[[ 1, 20260901, "a", "USD" ]]""", nextLink: "https://management.azure.com/next?skiptoken=x"));
        });
        var client = new AzureFoundryCostClient(new HttpClient(handler), Settings(Scope), new FixedToken());

        var report = await client.GetDailyCostAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.Equal(["a", "b"], report.Lines.Select(line => line.Meter));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Cost Management Reader")]
    [InlineData(HttpStatusCode.Unauthorized, "Cost Management Reader")]
    [InlineData(HttpStatusCode.NotFound, "no resource at the cost scope")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limiting")]
    [InlineData(HttpStatusCode.InternalServerError, "returned 500")]
    public async Task Refusals_become_sentences_a_person_can_act_on(HttpStatusCode status, string expected)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"error":{"code":"X","message":"boom"}}""", Encoding.UTF8, "application/json")
        });
        var client = new AzureFoundryCostClient(new HttpClient(handler), Settings(Scope), new FixedToken());

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.GetDailyCostAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), TestContext.Current.CancellationToken));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task An_unreachable_endpoint_is_reported_as_a_foundry_failure()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new AzureFoundryCostClient(new HttpClient(handler), Settings(Scope), new FixedToken());

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.GetDailyCostAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), TestContext.Current.CancellationToken));

        Assert.Contains("Could not reach Azure Cost Management", ex.Message);
        Assert.Contains("connection refused", ex.Message);
    }

    /// <summary>
    /// The three cost parts ask within the same second, and the Azure CLI mints a
    /// token by spawning a process: one fetch per expiry, not one per part.
    /// </summary>
    [Fact]
    public async Task The_developer_sign_in_token_is_fetched_once_until_it_nears_expiry()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero));
        var credential = new CountingCredential(expiresOn: clock.GetUtcNow().AddHours(1));
        var source = new DeveloperSignInTokenSource(credential, clock);

        _ = await source.GetTokenAsync(TestContext.Current.CancellationToken);
        _ = await source.GetTokenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, credential.Calls);

        clock.Now = clock.GetUtcNow().AddMinutes(57);
        _ = await source.GetTokenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, credential.Calls);
    }

    [Fact]
    public async Task The_spend_adapter_passes_meters_through_as_the_model_with_no_tokens_and_no_estimate()
    {
        var handler = new RecordingHandler(_ => Ok(Page(rows: """[[ 1.5, 20260901, "gpt-5.4 Input Tokens", "EUR" ]]""")));
        var source = new AzureFoundrySpendSource(
            new AzureFoundryCostClient(new HttpClient(handler), Settings(Scope), new FixedToken()));

        var availability = await source.GetAvailabilityAsync(TestContext.Current.CancellationToken);
        var report = await source.GetSpendAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), TestContext.Current.CancellationToken);

        Assert.True(availability.IsAvailable);
        var entry = Assert.Single(report.Entries);
        Assert.Equal(new DateOnly(2026, 9, 1), entry.Date);
        Assert.Equal("gpt-5.4 Input Tokens", entry.Model);
        Assert.Null(entry.Tokens);
        Assert.Equal(1.5m, entry.Cost.Amount);
        Assert.Equal("EUR", entry.Cost.Currency);
        Assert.Null(report.Allowance);
        Assert.False(report.IsEstimate);
    }

    private static string Page(string rows, string? nextLink = null) => $$"""
        {
          "properties": {
            "nextLink": {{(nextLink is null ? "null" : "\"" + nextLink + "\"")}},
            "columns": [
              { "name": "Cost", "type": "Number" },
              { "name": "UsageDate", "type": "Number" },
              { "name": "Meter", "type": "String" },
              { "name": "Currency", "type": "String" }
            ],
            "rows": {{rows}}
          }
        }
        """;

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private AzureFoundrySettingsStore Settings(string? scope)
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-foundry-cost", Guid.NewGuid().ToString("n"), "azure-foundry.json");
        _paths.Add(path);
        var store = new AzureFoundrySettingsStore(path);
        if (scope is not null) store.SetCostScope(scope);
        return store;
    }

    private sealed class FixedToken(string token = "token") : IAzureManagementTokenSource
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(token);
    }

    private sealed class RefusingCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new CredentialUnavailableException("No sign-in.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new CredentialUnavailableException("No sign-in.");
    }

    private sealed class CountingCredential(DateTimeOffset expiresOn) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            GetTokenAsync(requestContext, cancellationToken).Result;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new AccessToken($"token-{Calls}", expiresOn));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
