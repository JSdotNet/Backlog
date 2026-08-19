using System.Globalization;
using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// A Copilot seat and what it has been doing. <c>last_activity_at</c> only
/// appears when the person's editor reports telemetry, so a null is "unknown",
/// not "idle".
/// </summary>
public sealed record CopilotSeat(
    string Login,
    DateTimeOffset? LastActivityAt,
    string? LastActivityEditor,
    DateTimeOffset? LastAuthenticatedAt,
    DateTimeOffset? CreatedAt,
    string? PlanType,
    string? AssigningTeam);

/// <summary>
/// A generated Copilot metrics report. The API answers with signed download
/// links rather than the figures themselves, so this is a pointer to the data.
/// </summary>
public sealed record CopilotMetricsReport(
    IReadOnlyList<string> DownloadLinks,
    DateOnly? ReportDay,
    DateOnly? ReportStartDay,
    DateOnly? ReportEndDay)
{
    public static CopilotMetricsReport Empty { get; } = new([], null, null, null);
}

/// <summary>Which slice of the metrics reports to fetch.</summary>
public enum CopilotMetricsScope
{
    /// <summary>One day of organization-wide figures.</summary>
    OrganizationDaily,

    /// <summary>The latest rolling 28 days of organization-wide figures.</summary>
    Organization28Day,

    /// <summary>One day broken down per user.</summary>
    UsersDaily,

    /// <summary>The latest rolling 28 days broken down per user.</summary>
    Users28Day
}

/// <summary>
/// The Copilot usage questions Backlog asks GitHub. Everything here is
/// organization-scoped by necessity: GitHub exposes no endpoint for an
/// individual subscriber's own Copilot usage, so an org (and org-owner rights)
/// is the only route to the data.
/// </summary>
public interface ICopilotUsageClient
{
    /// <summary>Whether Copilot usage reporting can be used, and why not when it can't.</summary>
    Task<CopilotUsageAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>Every Copilot seat in the organization, paged to the end.</summary>
    Task<IReadOnlyList<CopilotSeat>> GetSeatsAsync(
        string organization,
        CancellationToken cancellationToken = default);

    /// <summary>One person's seat, which is the closest GitHub gets to "my own usage".</summary>
    Task<CopilotSeat?> GetSeatAsync(
        string organization,
        string login,
        CancellationToken cancellationToken = default);

    Task<CopilotMetricsReport> GetMetricsReportAsync(
        string organization,
        CopilotMetricsScope scope,
        DateOnly? day = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Why Copilot usage reporting is or is not usable right now.</summary>
public sealed record CopilotUsageAvailability(bool IsAvailable, string Reason);

/// <summary>
/// <see cref="ICopilotUsageClient"/> over the same transport the issue client
/// uses, so gh-CLI and token authentication both work without a second
/// credential.
/// </summary>
public sealed class CopilotUsageClient(IGitHubTransport transport) : ICopilotUsageClient
{
    /// <summary>GitHub caps seat pages at 100.</summary>
    private const int PageSize = 100;

    /// <summary>Paging is bounded rather than trusted to terminate.</summary>
    private const int MaxPages = 50;

    public async Task<CopilotUsageAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        await transport.IsAvailableAsync(cancellationToken).ConfigureAwait(false)
            ? new CopilotUsageAvailability(
                true,
                $"Reading Copilot usage with the {transport.Description}. GitHub only reports Copilot "
                + "usage per organization, and only to an organization owner — there is no personal "
                + "usage endpoint.")
            : new CopilotUsageAvailability(
                false,
                "No GitHub credential is available. Copilot usage needs an organization and a token "
                + "with owner-level access to it.");

    public async Task<IReadOnlyList<CopilotSeat>> GetSeatsAsync(
        string organization,
        CancellationToken cancellationToken = default)
    {
        var org = RequireOrganization(organization);

        var seats = new List<CopilotSeat>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await transport.SendAsync(
                HttpMethod.Get,
                $"orgs/{org}/copilot/billing/seats?per_page={PageSize}&page={page}",
                body: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var batch = ReadSeats(response);
            seats.AddRange(batch);

            if (batch.Count < PageSize) break;
        }

        return seats;
    }

    public async Task<CopilotSeat?> GetSeatAsync(
        string organization,
        string login,
        CancellationToken cancellationToken = default)
    {
        var org = RequireOrganization(organization);

        if (string.IsNullOrWhiteSpace(login))
        {
            throw new GitHubException("Say whose Copilot seat to look up.");
        }

        var response = await transport.SendAsync(
            HttpMethod.Get,
            $"orgs/{org}/members/{login.Trim()}/copilot",
            body: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ReadSeat(response);
    }

    public async Task<CopilotMetricsReport> GetMetricsReportAsync(
        string organization,
        CopilotMetricsScope scope,
        DateOnly? day = null,
        CancellationToken cancellationToken = default)
    {
        var org = RequireOrganization(organization);

        var isDaily = scope is CopilotMetricsScope.OrganizationDaily or CopilotMetricsScope.UsersDaily;
        if (isDaily && day is null)
        {
            throw new GitHubException("A daily Copilot metrics report needs the day to report on.");
        }

        var path = scope switch
        {
            CopilotMetricsScope.OrganizationDaily => $"orgs/{org}/copilot/metrics/reports/organization-1-day?day={day:yyyy-MM-dd}",
            CopilotMetricsScope.Organization28Day => $"orgs/{org}/copilot/metrics/reports/organization-28-day/latest",
            CopilotMetricsScope.UsersDaily => $"orgs/{org}/copilot/metrics/reports/users-1-day?day={day:yyyy-MM-dd}",
            CopilotMetricsScope.Users28Day => $"orgs/{org}/copilot/metrics/reports/users-28-day/latest",
            _ => throw new GitHubException("That Copilot metrics report isn't one Backlog knows how to ask for.")
        };

        var response = await transport
            .SendAsync(HttpMethod.Get, path, body: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return ReadMetricsReport(response);
    }

    internal static IReadOnlyList<CopilotSeat> ReadSeats(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return [];
        if (!element.TryGetProperty("seats", out var seats) || seats.ValueKind != JsonValueKind.Array) return [];

        return [.. seats.EnumerateArray().Select(ReadSeat).OfType<CopilotSeat>()];
    }

    internal static CopilotSeat? ReadSeat(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        string? login = null;
        if (element.TryGetProperty("assignee", out var assignee) && assignee.ValueKind == JsonValueKind.Object)
        {
            login = String(assignee, "login");
        }

        // A seat with nobody on it tells us nothing about usage.
        if (string.IsNullOrWhiteSpace(login)) return null;

        string? team = null;
        if (element.TryGetProperty("assigning_team", out var teamElement) && teamElement.ValueKind == JsonValueKind.Object)
        {
            team = String(teamElement, "name") ?? String(teamElement, "slug");
        }

        return new CopilotSeat(
            login,
            Timestamp(element, "last_activity_at"),
            String(element, "last_activity_editor"),
            Timestamp(element, "last_authenticated_at"),
            Timestamp(element, "created_at"),
            String(element, "plan_type"),
            team);
    }

    internal static CopilotMetricsReport ReadMetricsReport(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return CopilotMetricsReport.Empty;

        var links = element.TryGetProperty("download_links", out var downloads) && downloads.ValueKind == JsonValueKind.Array
            ? downloads.EnumerateArray()
                .Where(link => link.ValueKind == JsonValueKind.String)
                .Select(link => link.GetString()!)
                .ToList()
            : [];

        return new CopilotMetricsReport(
            links,
            Date(element, "report_day"),
            Date(element, "report_start_day"),
            Date(element, "report_end_day"));
    }

    private static string RequireOrganization(string organization)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            throw new GitHubNotConfiguredException(
                "Copilot usage is reported per organization. GitHub has no endpoint for an "
                + "individual subscriber's own usage, so configure the organization to report on.");
        }

        return organization.Trim();
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateOnly? Date(JsonElement element, string name) =>
        String(element, name) is { } text && DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? Timestamp(JsonElement element, string name) =>
        String(element, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
