using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// GitHub reports Copilot usage per organization only, so these tests pin both
/// the JSON shapes and the refusal when no organization is given.
/// </summary>
public class CopilotUsageClientTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Seats_carry_the_activity_that_makes_them_useful()
    {
        var seats = CopilotUsageClient.ReadSeats(Parse(
            """
            {
              "total_seats": 1,
              "seats": [
                {
                  "created_at": "2025-01-02T03:04:05Z",
                  "last_activity_at": "2026-08-11T09:10:11Z",
                  "last_activity_editor": "vscode/1.95.0",
                  "last_authenticated_at": "2026-08-11T08:00:00Z",
                  "plan_type": "business",
                  "assignee": { "login": "octocat" },
                  "assigning_team": { "name": "Platform", "slug": "platform" }
                }
              ]
            }
            """));

        var seat = Assert.Single(seats);

        Assert.Equal("octocat", seat.Login);
        Assert.Equal("vscode/1.95.0", seat.LastActivityEditor);
        Assert.Equal("Platform", seat.AssigningTeam);
        Assert.Equal(new DateTimeOffset(2026, 8, 11, 9, 10, 11, TimeSpan.Zero), seat.LastActivityAt);
    }

    [Fact]
    public void A_seat_with_no_reported_activity_reads_as_unknown_rather_than_zero()
    {
        var seat = CopilotUsageClient.ReadSeat(Parse(
            """{ "assignee": { "login": "octocat" }, "last_activity_at": null }"""));

        Assert.NotNull(seat);
        Assert.Null(seat.LastActivityAt);
        Assert.Null(seat.LastActivityEditor);
    }

    [Fact]
    public void An_unassigned_seat_is_skipped()
    {
        Assert.Empty(CopilotUsageClient.ReadSeats(Parse("""{ "seats": [ { "plan_type": "business" } ] }""")));
    }

    [Fact]
    public void A_metrics_report_is_a_pointer_to_the_figures_not_the_figures()
    {
        var report = CopilotUsageClient.ReadMetricsReport(Parse(
            """
            {
              "download_links": ["https://example.invalid/report.json.gz"],
              "report_start_day": "2026-07-15",
              "report_end_day": "2026-08-11"
            }
            """));

        Assert.Single(report.DownloadLinks);
        Assert.Equal(new DateOnly(2026, 7, 15), report.ReportStartDay);
        Assert.Equal(new DateOnly(2026, 8, 11), report.ReportEndDay);
        Assert.Null(report.ReportDay);
    }

    [Fact]
    public async Task Asking_without_an_organization_explains_that_there_is_no_personal_endpoint()
    {
        var client = new CopilotUsageClient(new StubTransport());

        var error = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() => client.GetSeatsAsync(" "));

        Assert.Contains("individual subscriber", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_daily_metrics_report_needs_the_day_it_covers()
    {
        var client = new CopilotUsageClient(new StubTransport());

        await Assert.ThrowsAsync<GitHubException>(() =>
            client.GetMetricsReportAsync("acme", CopilotMetricsScope.OrganizationDaily));
    }

    [Fact]
    public async Task The_28_day_report_needs_no_day_and_asks_for_the_latest()
    {
        var transport = new StubTransport("""{ "download_links": [] }""");
        var client = new CopilotUsageClient(transport);

        await client.GetMetricsReportAsync("acme", CopilotMetricsScope.Users28Day);

        Assert.Equal("orgs/acme/copilot/metrics/reports/users-28-day/latest", Assert.Single(transport.Paths));
    }

    [Fact]
    public async Task A_daily_report_asks_for_the_day_in_the_format_github_wants()
    {
        var transport = new StubTransport("""{ "download_links": [] }""");
        var client = new CopilotUsageClient(transport);

        await client.GetMetricsReportAsync("acme", CopilotMetricsScope.UsersDaily, new DateOnly(2026, 8, 9));

        Assert.Equal("orgs/acme/copilot/metrics/reports/users-1-day?day=2026-08-09", Assert.Single(transport.Paths));
    }

    [Fact]
    public async Task Seats_stop_paging_once_a_short_page_arrives()
    {
        var transport = new StubTransport("""{ "seats": [ { "assignee": { "login": "octocat" } } ] }""");
        var client = new CopilotUsageClient(transport);

        var seats = await client.GetSeatsAsync("acme");

        Assert.Single(seats);
        Assert.Single(transport.Paths);
        Assert.Contains("per_page=100&page=1", transport.Paths[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Availability_says_plainly_that_usage_is_organization_scoped()
    {
        var availability = await new CopilotUsageClient(new StubTransport()).GetAvailabilityAsync();

        Assert.True(availability.IsAvailable);
        Assert.Contains("no personal", availability.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubTransport(string response = "null") : IGitHubTransport
    {
        public List<string> Paths { get; } = [];

        /// <summary>The API version each call asked for, so a client that has to
        /// pin one can be held to it. Copilot's does not, and this records the null
        /// that proves it leaves the default alone.</summary>
        public List<string?> ApiVersions { get; } = [];

        public string Description => "stub";

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<JsonElement> SendAsync(
            HttpMethod method,
            string path,
            object? body = null,
            string? apiVersion = null,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(path);
            ApiVersions.Add(apiVersion);
            return Task.FromResult(Parse(response));
        }
    }
}
