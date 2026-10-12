using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// What the Overview's Needs-you list asks for: the things across the dashboard's tabs
/// that need the person, for the scope in view.
/// <para>
/// A derivation over the other insights and never a source of its own. Each rule reads
/// its tab's existing insight — drift, the roadmap, the month-end projection, rework — so
/// the list cannot disagree with the tab it links to, and a source those insights cannot
/// read is skipped rather than failing the list.
/// </para>
/// <para>
/// No invalidation: every insight it reads keeps its own cache, and a refresh of one of
/// them is what moves this.
/// </para>
/// </summary>
public interface IAttentionInsights
{
    /// <summary>The Needs-you items for <paramref name="scope"/>, in rule order: drift,
    /// roadmap items behind, providers past budget, rework. Always a value — an empty one
    /// is <see cref="NeedsYouInsight.NothingNeedsYou"/>.</summary>
    Task<NeedsYouInsight> GetNeedsYouAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);
}
