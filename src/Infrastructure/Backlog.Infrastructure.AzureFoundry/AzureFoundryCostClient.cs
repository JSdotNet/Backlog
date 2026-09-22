using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Polly.Timeout;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>Whether the bill can be read at all, and why not when it cannot.</summary>
public sealed record AzureFoundryCostAvailability(bool IsAvailable, string Reason);

/// <summary>One day's charge on one meter, exactly as Azure reported it. A meter
/// is Azure's unit of billing — for a Foundry model deployment, the model and
/// whether the tokens went in or out — which is the nearest thing to "the model"
/// the bill knows.</summary>
public sealed record AzureFoundryCostLine(DateOnly Date, string Meter, decimal Cost, string Currency);

public sealed record AzureFoundryCostReport(IReadOnlyList<AzureFoundryCostLine> Lines)
{
    public static AzureFoundryCostReport Empty { get; } = new([]);
}

/// <summary>
/// What the Foundry resource cost, day by day and meter by meter, from Azure Cost
/// Management. The one question the dashboard asks Azure.
/// </summary>
public interface IAzureFoundryCostClient
{
    Task<AzureFoundryCostAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>Actual cost, daily, over an inclusive day range, for the scope in
    /// Settings. Paging is followed to the end so a caller gets the window rather
    /// than a first page of it.</summary>
    Task<AzureFoundryCostReport> GetDailyCostAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IAzureFoundryCostClient"/> over the Cost Management query API.
/// </summary>
/// <remarks>
/// <para>
/// A hand-written call rather than the Resource Manager SDK, like the Claude and
/// GitHub clients beside it: the query is one POST with one body shape, and the
/// SDK would bring the whole management plane in for it. The sign-in it does
/// need comes through <see cref="IAzureManagementTokenSource"/>.
/// </para>
/// <para>
/// Where Resource Manager is comes from the <see cref="HttpClient"/>'s base
/// address, which the registration sets to the public cloud and the harness
/// points at its stand-in. A sovereign cloud would be a different base address
/// and nothing else.
/// </para>
/// <para>
/// The figures are what Azure calls actual cost — billed amounts, not a price list
/// applied to a usage count — in the currency the subscription is billed in.
/// Azure settles a day up to a day late, so the running month is short its last
/// day or two; that is a fact about the bill, not an estimate on this side.
/// </para>
/// </remarks>
public sealed class AzureFoundryCostClient(
    HttpClient httpClient,
    AzureFoundrySettingsStore settingsStore,
    IAzureManagementTokenSource tokens) : IAzureFoundryCostClient
{
    public static readonly Uri PublicCloudManagementEndpoint = new("https://management.azure.com/");

    private const string ApiVersion = "2023-11-01";

    /// <summary>A window can be long and a scope can be a subscription with a
    /// meter per resource per day, so paging is bounded rather than trusted to
    /// terminate.</summary>
    private const int MaxPages = 50;

    internal const string NotConfiguredMessage =
        "Add the Azure resource ID of your Foundry account under Azure Foundry AI in Settings to read its "
        + "spend from Azure Cost Management.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AzureFoundryCostAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!settingsStore.Current.IsCostConfigured)
        {
            return new AzureFoundryCostAvailability(false, NotConfiguredMessage);
        }

        // The sign-in is checked here rather than left for the first query: a
        // missing one is the second-likeliest reason for no figures, and it reads
        // better as "sign in" than as a 401 from a query.
        try
        {
            await tokens.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AzureFoundryException ex)
        {
            return new AzureFoundryCostAvailability(false, ex.Message);
        }

        return new AzureFoundryCostAvailability(true, "Reading spend from Azure Cost Management with this machine's Azure sign-in.");
    }

    public async Task<AzureFoundryCostReport> GetDailyCostAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new ArgumentException("The window's start is after its end.", nameof(from));
        }

        var settings = settingsStore.Current;
        if (!settings.IsCostConfigured)
        {
            throw new AzureFoundryException(NotConfiguredMessage);
        }

        var scope = settings.CostScope!;
        var token = await tokens.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        var body = new
        {
            type = "ActualCost",
            timeframe = "Custom",
            timePeriod = new
            {
                from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z",
                to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59Z"
            },
            dataset = new
            {
                granularity = "Daily",
                aggregation = new { totalCost = new { name = "Cost", function = "Sum" } },
                grouping = new[] { new { type = "Dimension", name = "Meter" } }
            }
        };

        var baseAddress = httpClient.BaseAddress ?? PublicCloudManagementEndpoint;
        var next = new Uri(baseAddress, $"{scope.TrimStart('/')}/providers/Microsoft.CostManagement/query?api-version={ApiVersion}");
        var lines = new List<AzureFoundryCostLine>();

        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, next)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AzureFoundryException(Refusal(response.StatusCode, scope, payload));
            }

            next = ReadPage(payload, lines);
        }

        return lines.Count == 0 ? AzureFoundryCostReport.Empty : new AzureFoundryCostReport(lines);
    }

    /// <summary>
    /// One page of the query result into lines, returning the next page's address
    /// or null at the end. Columns are matched by name rather than position: the
    /// API documents the set, not the order, and a grouping changes both.
    /// </summary>
    private static Uri? ReadPage(string payload, List<AzureFoundryCostLine> lines)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("properties", out var properties))
        {
            throw new AzureFoundryException("Azure Cost Management answered without a result.");
        }

        var columns = properties.GetProperty("columns")
            .EnumerateArray()
            .Select((column, index) => (Name: column.GetProperty("name").GetString() ?? string.Empty, Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);

        var cost = Column(columns, "Cost");
        var date = Column(columns, "UsageDate");
        var meter = Column(columns, "Meter");
        var currency = Column(columns, "Currency");

        foreach (var row in properties.GetProperty("rows").EnumerateArray())
        {
            var cells = row.EnumerateArray().ToList();
            var amount = cells[cost].GetDecimal();
            if (amount == 0m) continue;

            lines.Add(new AzureFoundryCostLine(
                UsageDate(cells[date]),
                cells[meter].GetString() is { Length: > 0 } name ? name : "Not reported",
                amount,
                cells[currency].GetString() ?? "USD"));
        }

        return properties.TryGetProperty("nextLink", out var link)
            && link.ValueKind == JsonValueKind.String
            && Uri.TryCreate(link.GetString(), UriKind.Absolute, out var nextLink)
            ? nextLink
            : null;
    }

    private static int Column(Dictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var index)
            ? index
            : throw new AzureFoundryException($"Azure Cost Management answered without a {name} column.");

    /// <summary>Azure writes the day as the number 20260901, not as a date.</summary>
    private static DateOnly UsageDate(JsonElement cell)
    {
        var text = cell.ValueKind == JsonValueKind.Number
            ? cell.GetInt64().ToString(CultureInfo.InvariantCulture)
            : cell.GetString() ?? string.Empty;

        return DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new AzureFoundryException($"Azure Cost Management reported a day it did not date: '{text}'.");
    }

    /// <summary>The refusals a person can act on get their own sentence; the rest
    /// carry Azure's status and words.</summary>
    private static string Refusal(HttpStatusCode status, string scope, string payload) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            $"Azure refused to read costs on {scope} ({(int)status}). The signed-in account needs the Cost Management "
            + "Reader role on the resource, its resource group, or its subscription.",
        HttpStatusCode.NotFound =>
            $"Azure has no resource at the cost scope in Settings ({scope}). Check the resource ID against the portal.",
        HttpStatusCode.TooManyRequests =>
            "Azure Cost Management is rate-limiting this account; try again in a minute.",
        _ => $"Azure Cost Management returned {(int)status}: {TrimForMessage(payload)}"
    };

    /// <summary>The send, with the transport's failures translated into this
    /// client's — the same three as the chat client's, for the same reason.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AzureFoundryException($"Could not reach Azure Cost Management: {ex.Message}", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new AzureFoundryException("Azure Cost Management did not answer before the request timed out.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AzureFoundryException("Azure Cost Management did not answer before the request timed out.", ex);
        }
    }

    private static string TrimForMessage(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "...";
    }
}
