namespace Backlog.Infrastructure.Claude;

/// <summary>How wide a time bucket the usage report should return.</summary>
public enum ClaudeUsageBucket
{
    Minute,
    Hour,
    Day
}

/// <summary>
/// A half-open time window to report on. Anthropic wants RFC 3339 instants, and
/// keeping the pair together stops callers from passing them the wrong way
/// round.
/// </summary>
public sealed record ClaudeUsageWindow(DateTimeOffset StartingAt, DateTimeOffset EndingAt)
{
    /// <summary>The last <paramref name="days"/> whole days ending now.</summary>
    public static ClaudeUsageWindow LastDays(int days, DateTimeOffset? now = null)
    {
        if (days <= 0) throw new ArgumentOutOfRangeException(nameof(days), "A usage window covers at least one day.");

        var end = now ?? DateTimeOffset.UtcNow;
        return new ClaudeUsageWindow(end.AddDays(-days), end);
    }
}

/// <summary>
/// Token counts for one bucket and one grouping. <see cref="InputTokens"/> is
/// the uncached input only — Anthropic reports cache reads and cache writes
/// separately, and <see cref="TotalInputTokens"/> is the sum people actually
/// mean when they say "input".
/// </summary>
public sealed record ClaudeTokenUsage(
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens)
{
    public long TotalInputTokens => InputTokens + CacheCreationInputTokens + CacheReadInputTokens;

    public long TotalTokens => TotalInputTokens + OutputTokens;

    public static ClaudeTokenUsage Empty { get; } = new(0, 0, 0, 0);

    public static ClaudeTokenUsage operator +(ClaudeTokenUsage left, ClaudeTokenUsage right) => new(
        left.InputTokens + right.InputTokens,
        left.OutputTokens + right.OutputTokens,
        left.CacheCreationInputTokens + right.CacheCreationInputTokens,
        left.CacheReadInputTokens + right.CacheReadInputTokens);
}

/// <summary>One reported row inside a bucket, with whatever grouping keys the
/// request asked for.</summary>
public sealed record ClaudeUsageResult(
    ClaudeTokenUsage Tokens,
    string? Model,
    string? ApiKeyId,
    string? WorkspaceId,
    string? ServiceTier);

/// <summary>One time bucket of the messages usage report.</summary>
public sealed record ClaudeUsageBucketReport(
    DateTimeOffset StartingAt,
    DateTimeOffset EndingAt,
    IReadOnlyList<ClaudeUsageResult> Results)
{
    public ClaudeTokenUsage Totals => Results.Aggregate(ClaudeTokenUsage.Empty, (sum, r) => sum + r.Tokens);
}

/// <summary>The messages usage report for a window.</summary>
public sealed record ClaudeUsageReport(IReadOnlyList<ClaudeUsageBucketReport> Buckets)
{
    public ClaudeTokenUsage Totals => Buckets.Aggregate(ClaudeTokenUsage.Empty, (sum, b) => sum + b.Totals);

    public static ClaudeUsageReport Empty { get; } = new([]);
}

/// <summary>
/// One cost row. The amount is kept exactly as Anthropic reported it, together
/// with its currency, so nothing is lost to a unit assumption before it reaches
/// a view that knows how it wants to display money.
/// </summary>
public sealed record ClaudeCostResult(decimal Amount, string Currency, string? WorkspaceId, string? Description);

/// <summary>One day of the cost report — the cost API only buckets by day.</summary>
public sealed record ClaudeCostBucketReport(
    DateTimeOffset StartingAt,
    DateTimeOffset EndingAt,
    IReadOnlyList<ClaudeCostResult> Results)
{
    public decimal Total => Results.Sum(r => r.Amount);
}

/// <summary>The cost report for a window.</summary>
public sealed record ClaudeCostReport(IReadOnlyList<ClaudeCostBucketReport> Buckets)
{
    public decimal Total => Buckets.Sum(b => b.Total);

    public static ClaudeCostReport Empty { get; } = new([]);
}

/// <summary>What Claude Code did for one actor on one day.</summary>
public sealed record ClaudeCodeDailyUsage(
    DateOnly Date,
    string? Actor,
    int Sessions,
    long LinesAdded,
    long LinesRemoved,
    int Commits,
    int PullRequests,
    IReadOnlyList<ClaudeCodeModelUsage> Models);

/// <summary>Per-model token and cost detail inside a Claude Code day.</summary>
public sealed record ClaudeCodeModelUsage(string? Model, ClaudeTokenUsage Tokens, decimal EstimatedCost, string Currency);

/// <summary>The Claude Code analytics report for a single day.</summary>
public sealed record ClaudeCodeReport(DateOnly Date, IReadOnlyList<ClaudeCodeDailyUsage> Actors)
{
    public static ClaudeCodeReport Empty(DateOnly date) => new(date, []);
}

/// <summary>
/// Why Claude usage reporting is or is not usable right now, in words that can
/// go straight into Settings.
/// </summary>
public sealed record ClaudeUsageAvailability(bool IsAvailable, string Reason);
