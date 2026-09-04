using System.Globalization;
using System.Text.Json;

namespace Backlog.Infrastructure.Claude;

/// <summary>
/// The Claude usage questions Backlog asks: how many tokens went through, what
/// they cost, and what Claude Code did.
/// </summary>
public interface IClaudeUsageClient
{
    /// <summary>Whether usage reporting can be used at all, and why not when it can't.</summary>
    Task<ClaudeUsageAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<ClaudeUsageReport> GetMessageUsageAsync(
        ClaudeUsageWindow window,
        ClaudeUsageBucket bucket = ClaudeUsageBucket.Day,
        CancellationToken cancellationToken = default);

    Task<ClaudeCostReport> GetCostAsync(
        ClaudeUsageWindow window,
        CancellationToken cancellationToken = default);

    Task<ClaudeCodeReport> GetClaudeCodeUsageAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IClaudeUsageClient"/> over the Admin API. Paging is followed to
/// the end here so callers get a whole window rather than a first page.
/// </summary>
public sealed class ClaudeUsageClient(IClaudeTransport transport, ClaudeSettingsStore settings) : IClaudeUsageClient
{
    private const string MessagesUsagePath = "v1/organizations/usage_report/messages";
    private const string CostReportPath = "v1/organizations/cost_report";
    private const string ClaudeCodePath = "v1/organizations/usage_report/claude_code";

    /// <summary>Anthropic caps a page at 1000 rows; asking for fewer only costs round trips.</summary>
    private const int PageSize = 1000;

    /// <summary>A window can be long and buckets can be per-minute, so paging is
    /// bounded rather than trusted to terminate.</summary>
    private const int MaxPages = 100;

    public async Task<ClaudeUsageAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var current = settings.Current;

        if (!current.IsConfigured)
        {
            return new ClaudeUsageAvailability(
                false,
                "Add an Anthropic Admin API key in Settings. Usage and cost reports are an "
                + "organization feature of the Claude Console — Anthropic does not expose them to "
                + "individual accounts.");
        }

        // A key without the admin prefix is not refused here. Anthropic also accepts a
        // personal or service account key that isn't scoped to a workspace, and no key
        // string says which of those it is — Settings warns, and the server decides.
        return await transport.IsAvailableAsync(cancellationToken).ConfigureAwait(false)
            ? new ClaudeUsageAvailability(true, $"Reading organization usage with the {transport.Description}.")
            : new ClaudeUsageAvailability(false, "The Anthropic Admin API isn't reachable right now.");
    }

    public async Task<ClaudeUsageReport> GetMessageUsageAsync(
        ClaudeUsageWindow window,
        ClaudeUsageBucket bucket = ClaudeUsageBucket.Day,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        Validate(window);

        var query = BaseQuery(window);
        query.Add($"bucket_width={BucketWidth(bucket)}");
        query.Add("group_by[]=model");
        query.Add("group_by[]=api_key_id");

        var buckets = new List<ClaudeUsageBucketReport>();
        await foreach (var element in ReadPagesAsync(MessagesUsagePath, query, cancellationToken).ConfigureAwait(false))
        {
            buckets.Add(ReadUsageBucket(element));
        }

        return buckets.Count == 0 ? ClaudeUsageReport.Empty : new ClaudeUsageReport(buckets);
    }

    public async Task<ClaudeCostReport> GetCostAsync(
        ClaudeUsageWindow window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        Validate(window);

        var query = BaseQuery(window);

        // The cost report only supports daily buckets; sending anything else is
        // rejected rather than rounded.
        query.Add("bucket_width=1d");
        query.Add("group_by[]=description");

        var buckets = new List<ClaudeCostBucketReport>();
        await foreach (var element in ReadPagesAsync(CostReportPath, query, cancellationToken).ConfigureAwait(false))
        {
            buckets.Add(ReadCostBucket(element));
        }

        return buckets.Count == 0 ? ClaudeCostReport.Empty : new ClaudeCostReport(buckets);
    }

    public async Task<ClaudeCodeReport> GetClaudeCodeUsageAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        // Unlike the other two reports this one covers exactly one day, so it
        // takes a date rather than a window.
        var query = new List<string> { $"starting_at={date:yyyy-MM-dd}" };

        var actors = new List<ClaudeCodeDailyUsage>();
        await foreach (var element in ReadPagesAsync(ClaudeCodePath, query, cancellationToken).ConfigureAwait(false))
        {
            actors.Add(ReadClaudeCodeDay(element, date));
        }

        return actors.Count == 0 ? ClaudeCodeReport.Empty(date) : new ClaudeCodeReport(date, actors);
    }

    private List<string> BaseQuery(ClaudeUsageWindow window)
    {
        var query = new List<string>
        {
            $"starting_at={Uri.EscapeDataString(Rfc3339(window.StartingAt))}",
            $"ending_at={Uri.EscapeDataString(Rfc3339(window.EndingAt))}"
        };

        var workspace = settings.Current.WorkspaceId;
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            query.Add($"workspace_ids[]={Uri.EscapeDataString(workspace)}");
        }

        return query;
    }

    /// <summary>Walks the report's pages, yielding every bucket in order.</summary>
    private async IAsyncEnumerable<JsonElement> ReadPagesAsync(
        string path,
        IReadOnlyList<string> query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? page = null;

        for (var pageNumber = 0; pageNumber < MaxPages; pageNumber++)
        {
            var parameters = new List<string>(query) { $"limit={PageSize}" };
            if (page is not null) parameters.Add($"page={Uri.EscapeDataString(page)}");

            var response = await transport
                .SendAsync(HttpMethod.Get, $"{path}?{string.Join('&', parameters)}", cancellationToken)
                .ConfigureAwait(false);

            if (response.ValueKind != JsonValueKind.Object)
            {
                throw new ClaudeException("Anthropic did not return a usage report.");
            }

            if (response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    yield return item;
                }
            }

            var hasMore = response.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True;
            page = hasMore ? String(response, "next_page") : null;

            if (page is null) yield break;
        }
    }

    internal static ClaudeUsageBucketReport ReadUsageBucket(JsonElement element)
    {
        var results = new List<ClaudeUsageResult>();

        if (element.TryGetProperty("results", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;

                results.Add(new ClaudeUsageResult(
                    ReadTokens(row),
                    String(row, "model"),
                    String(row, "api_key_id"),
                    String(row, "workspace_id"),
                    String(row, "service_tier")));
            }
        }

        return new ClaudeUsageBucketReport(
            Timestamp(element, "starting_at") ?? default,
            Timestamp(element, "ending_at") ?? default,
            results);
    }

    /// <summary>
    /// Reads a token row. Anthropic names the uncached figure
    /// <c>uncached_input_tokens</c> on the usage report and <c>input_tokens</c>
    /// on a message response, and both spellings turn up in the wild, so either
    /// is accepted.
    /// </summary>
    internal static ClaudeTokenUsage ReadTokens(JsonElement element)
    {
        var input = Number(element, "uncached_input_tokens") ?? Number(element, "input_tokens") ?? 0;

        var cacheCreation = Number(element, "cache_creation_input_tokens");
        if (cacheCreation is null
            && element.TryGetProperty("cache_creation", out var creation)
            && creation.ValueKind == JsonValueKind.Object)
        {
            // Cache writes are broken out per time-to-live; the total is what a
            // usage figure means.
            cacheCreation = creation.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Number)
                .Sum(p => p.Value.TryGetInt64(out var value) ? value : 0);
        }

        return new ClaudeTokenUsage(
            input,
            Number(element, "output_tokens") ?? 0,
            cacheCreation ?? 0,
            Number(element, "cache_read_input_tokens") ?? 0);
    }

    internal static ClaudeCostBucketReport ReadCostBucket(JsonElement element)
    {
        var results = new List<ClaudeCostResult>();

        if (element.TryGetProperty("results", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;

                results.Add(new ClaudeCostResult(
                    Money(row, "amount") ?? 0m,
                    String(row, "currency") ?? "USD",
                    String(row, "workspace_id"),
                    String(row, "description")));
            }
        }

        return new ClaudeCostBucketReport(
            Timestamp(element, "starting_at") ?? default,
            Timestamp(element, "ending_at") ?? default,
            results);
    }

    internal static ClaudeCodeDailyUsage ReadClaudeCodeDay(JsonElement element, DateOnly fallbackDate)
    {
        var date = String(element, "date") is { } text && DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallbackDate;

        string? actor = null;
        if (element.TryGetProperty("actor", out var actorElement) && actorElement.ValueKind == JsonValueKind.Object)
        {
            actor = String(actorElement, "email_address") ?? String(actorElement, "api_key_name");
        }

        var core = element.TryGetProperty("core_metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Object
            ? metrics
            : element;

        long added = 0, removed = 0;
        if (core.TryGetProperty("lines_of_code", out var lines) && lines.ValueKind == JsonValueKind.Object)
        {
            added = Number(lines, "added") ?? 0;
            removed = Number(lines, "removed") ?? 0;
        }

        var models = new List<ClaudeCodeModelUsage>();
        if (element.TryGetProperty("model_breakdown", out var breakdown) && breakdown.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in breakdown.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                var tokens = entry.TryGetProperty("tokens", out var tokenElement) && tokenElement.ValueKind == JsonValueKind.Object
                    ? ReadTokens(tokenElement)
                    : ClaudeTokenUsage.Empty;

                var cost = 0m;
                var currency = "USD";
                if (entry.TryGetProperty("estimated_cost", out var costElement) && costElement.ValueKind == JsonValueKind.Object)
                {
                    cost = Money(costElement, "amount") ?? 0m;
                    currency = String(costElement, "currency") ?? currency;
                }

                models.Add(new ClaudeCodeModelUsage(String(entry, "model"), tokens, cost, currency));
            }
        }

        return new ClaudeCodeDailyUsage(
            date,
            actor,
            (int)(Number(core, "num_sessions") ?? 0),
            added,
            removed,
            (int)(Number(core, "commits_by_claude_code") ?? 0),
            (int)(Number(core, "pull_requests_by_claude_code") ?? 0),
            models);
    }

    private static void Validate(ClaudeUsageWindow window)
    {
        if (window.EndingAt <= window.StartingAt)
        {
            throw new ClaudeException("A usage window has to end after it starts.");
        }
    }

    private static string BucketWidth(ClaudeUsageBucket bucket) => bucket switch
    {
        ClaudeUsageBucket.Minute => "1m",
        ClaudeUsageBucket.Hour => "1h",
        ClaudeUsageBucket.Day => "1d",
        _ => "1d"
    };

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    /// <summary>Anthropic reports money as a decimal string; a number is
    /// accepted too so a shape change doesn't silently zero the cost.</summary>
    private static decimal? Money(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? Timestamp(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}

/// <summary>Stands in when Claude usage support is not registered in a build.</summary>
public sealed class UnavailableClaudeUsageClient : IClaudeUsageClient
{
    private const string Message = "Claude usage reporting is not registered in this build.";

    public Task<ClaudeUsageAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ClaudeUsageAvailability(false, Message));

    public Task<ClaudeUsageReport> GetMessageUsageAsync(ClaudeUsageWindow window, ClaudeUsageBucket bucket = ClaudeUsageBucket.Day, CancellationToken cancellationToken = default) =>
        throw new ClaudeNotConfiguredException(Message);

    public Task<ClaudeCostReport> GetCostAsync(ClaudeUsageWindow window, CancellationToken cancellationToken = default) =>
        throw new ClaudeNotConfiguredException(Message);

    public Task<ClaudeCodeReport> GetClaudeCodeUsageAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        throw new ClaudeNotConfiguredException(Message);
}
