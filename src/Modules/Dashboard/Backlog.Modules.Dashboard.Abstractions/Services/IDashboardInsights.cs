using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// What the productivity half of the dashboard asks for.
/// <para>
/// Four methods rather than one that returns everything, and that is the
/// independent-parts rule expressed in the contract. A single call would make the
/// slowest source decide when the fastest part renders, and would make one
/// unconfigured provider able to fail the whole surface. Each part awaits its own
/// method, shows its own status, and refreshes on its own.
/// </para>
/// </summary>
public interface IProductivityInsights
{
    Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    Task<InsightResult<ProductivityTrend>> GetTrendAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    Task<InsightResult<ReworkInsight>> GetReworkAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>Drops whatever was cached for this scope, so the next call goes
    /// back to the provider. What a refresh button does.</summary>
    void Invalidate(DashboardScope scope);
}

/// <summary>
/// What the sessions part of the dashboard asks for.
/// <para>
/// One method, because there is one part. The scope reaches it in full and is honoured
/// in half: the window and the machine narrow the answer, the repository cannot,
/// because Claude records no repository against a session and filtering on one would
/// silently drop every Claude session from a part whose whole point is showing both
/// assistants. The parameter is still a <see cref="DashboardScope"/> rather than a
/// narrower pair — the part is one of a surface's parts and takes the surface's scope —
/// and the constraint is stated on the part instead of being hidden in a signature.
/// </para>
/// </summary>
public interface ISessionInsights
{
    Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>Drops what was read, so the next call goes back to the source.
    /// <para>
    /// No scope, unlike <see cref="IProductivityInsights.Invalidate"/> and for the same
    /// reason <see cref="ICostInsights.Invalidate"/> takes none: one read serves every
    /// scope here, because the scoping is a derivation over the whole report rather
    /// than a narrowing of the call. There is nothing per-scope to drop.
    /// </para></summary>
    void Invalidate();
}

/// <summary>
/// What the tasks section of the dashboard asks for.
/// <para>
/// The repository scope and the window narrow the answer; the machine cannot, because a
/// task records no machine it was ticked off on. The parts say so rather than leaving a
/// figure that will not move to explain itself.
/// </para>
/// </summary>
public interface ITaskInsights
{
    Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>The roadmap items the window shows in the repositories in scope, and
    /// how each one's work stands. Ready with <see cref="PlanInsight.Off"/> when the
    /// roadmap is switched off, because a feature somebody turned off is a state, not a
    /// failure.</summary>
    Task<InsightResult<PlanInsight>> GetPlanAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What the Hours worked part of the dashboard asks for (local ADR 0019, §7).
/// <para>
/// The window narrows the answer; the repository and the machine cannot. A working
/// stretch is the person's, merged over every session on every paired machine so time
/// two sessions shared counts once, and cutting it by machine or by repository would
/// count a person's hour twice or not at all. The part says so.
/// </para>
/// <para>
/// No invalidation: the source reads through the activity cache on every call, so a
/// refresh is a second call.
/// </para>
/// </summary>
public interface IHoursWorkedInsights
{
    /// <summary>The hours worked per day and per calendar week over the scope's window.
    /// Unavailable, with the reason, when the hours cannot be stated or could not be
    /// read — never a window of zeros.</summary>
    Task<InsightResult<HoursWorkedInsight>> GetHoursWorkedAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What the cost half of the dashboard asks for.
/// <para>
/// No <see cref="DashboardScope"/> anywhere, on purpose. Neither provider reports
/// spend per repository — Anthropic groups its Claude Code report by actor and
/// model, GitHub groups AI credits by product, SKU and model — so a method that
/// took a scope would accept a narrowing it cannot honour. Leaving it out of the
/// signature is how the constraint reaches the screen instead of being lost in a
/// parameter that gets quietly ignored.
/// </para>
/// </summary>
public interface ICostInsights
{
    Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default);

    Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default);

    Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops whatever was cached, so the next call goes back to the providers.</summary>
    void Invalidate();
}
