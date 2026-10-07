namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// One model request a Claude Code session made, as Claude Code itself reported it in
/// a <c>claude_code.api_request</c> OpenTelemetry event.
/// <para>
/// The cost of a delivery run is read from these rather than worked out from tokens,
/// because Claude Code knows the price it applied to each request and a figure this
/// product computed afterwards would only be a second guess at the same number
/// (<c>.devbook/domain/sessions/features.md</c>, "Cost as Claude Code reports it").
/// </para>
/// <para>
/// Every figure but the identity is nullable: a value the event did not carry is
/// unknown, never zero, which is the rule the run's other measured figures already
/// follow.
/// </para>
/// </summary>
/// <param name="RequestId">The API's own id for the request — <c>request_id</c> —
/// and what makes storing an event twice store it once.</param>
/// <param name="SessionId">The Claude Code session that made it — <c>session.id</c>.</param>
/// <param name="Timestamp">When Claude Code recorded the event — <c>event.timestamp</c>.</param>
/// <param name="Model">The model the request went to.</param>
/// <param name="Effort">The effort level it ran at, as Claude Code names it.</param>
/// <param name="CostUsdMicros">Claude Code's estimate of the cost, in millionths of a
/// US dollar — <c>cost_usd_micros</c>, or <c>cost_usd</c> scaled where only that was
/// sent. Whole numbers, so a sum over thousands of requests does not drift.</param>
/// <param name="InputTokens">Uncached input tokens.</param>
/// <param name="OutputTokens">Output tokens.</param>
/// <param name="CacheReadTokens">Input tokens read from the prompt cache.</param>
/// <param name="CacheCreationTokens">Input tokens written to the prompt cache.</param>
/// <param name="DurationMs">How long the request took.</param>
/// <param name="QuerySource">What inside the session asked — the main loop, a
/// sub-agent, a compaction — as Claude Code labels it.</param>
/// <param name="AgentName">The sub-agent that made the request, when one did.</param>
/// <param name="SkillName">The skill active when the request was made, when one was.</param>
/// <param name="PromptId">The user prompt the request answers — <c>prompt.id</c>.</param>
public sealed record ClaudeApiRequest(
    string RequestId,
    string? SessionId,
    DateTimeOffset Timestamp,
    string? Model,
    string? Effort,
    long? CostUsdMicros,
    long? InputTokens,
    long? OutputTokens,
    long? CacheReadTokens,
    long? CacheCreationTokens,
    long? DurationMs,
    string? QuerySource,
    string? AgentName,
    string? SkillName,
    string? PromptId);

/// <summary>
/// Where the <see cref="ClaudeApiRequest"/>s the desktop app receives are kept: the
/// app's local database, one row per request.
/// <para>
/// Idempotent by <see cref="ClaudeApiRequest.RequestId"/>. An OTLP exporter retries a
/// batch it did not see acknowledged, so the same event arriving twice is the ordinary
/// case rather than an error, and the first copy is the one kept.
/// </para>
/// </summary>
public interface IClaudeApiRequestStore
{
    /// <summary>Stores the requests not stored yet and answers how many that was.</summary>
    Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>The stored requests, oldest first — every one, or one session's.</summary>
    Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Where this machine receives Claude Code's telemetry, for the settings page that
/// tells a person what to paste into Claude Code.
/// <para>
/// A port because the address is the desktop head's to know — the MCP listener's
/// configured port and its bearer token (local ADR 0024) — and the page that shows it
/// is this context's. A host that composes none, the browser harness, serves the
/// endpoint on its own origin with no token, and the page says that instead.
/// </para>
/// </summary>
public interface IClaudeCodeTelemetryEndpoint
{
    /// <summary>The OTLP/HTTP logs endpoint — loopback, the MCP server's port, and
    /// <c>/v1/logs</c>.</summary>
    Uri LogsEndpoint { get; }

    /// <summary>The bearer token a request has to carry, minted if this machine has
    /// none yet. The same token the MCP registrations carry: the endpoint sits behind
    /// the same guard.</summary>
    string EnsureToken();
}
