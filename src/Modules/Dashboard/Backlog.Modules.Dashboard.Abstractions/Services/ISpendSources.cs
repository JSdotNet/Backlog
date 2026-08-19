using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>One day's spend on one model, from one provider.</summary>
/// <param name="Date">The day the provider attributed it to.</param>
/// <param name="Model">Null when the provider did not name a model.</param>
/// <param name="Tokens">Null when the provider reports money but not tokens.</param>
/// <param name="Cost">The amount as reported, with its currency.</param>
public sealed record SpendEntry(DateOnly Date, string? Model, long? Tokens, DashboardMoney Cost);

/// <summary>What one provider reported for a window.</summary>
/// <param name="Entries">One row per day per model.</param>
/// <param name="Allowance">How much of the window's consumption the plan already
/// covered — GitHub reports it as a discount against the gross figure. Null for a
/// provider that does not report one, which Anthropic does not.</param>
/// <param name="IsEstimate">True when the provider calls its own figure an
/// estimate rather than a billed amount. Anthropic does; GitHub does not.</param>
public sealed record SpendReport(
    IReadOnlyList<SpendEntry> Entries,
    DashboardMoney? Allowance = null,
    bool IsEstimate = false)
{
    public static SpendReport Empty { get; } = new([]);
}

/// <summary>
/// PORT — one assistant's personal spend over a day range.
/// <para>
/// Two interfaces of the same shape rather than one with a provider argument. The
/// two adapters behind them share no credential, no availability reason and no
/// endpoint, so a single port would only be a switch statement in a different
/// file — and it would make a host unable to register one without the other.
/// </para>
/// </summary>
public interface IClaudeSpendSource
{
    Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<SpendReport> GetSpendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IClaudeSpendSource"/>
public interface ICopilotSpendSource
{
    Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<SpendReport> GetSpendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
