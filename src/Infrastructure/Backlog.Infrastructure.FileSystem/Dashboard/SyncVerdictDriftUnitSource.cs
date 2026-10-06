using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem.Dashboard;

/// <summary>
/// ADAPTER — answers the dashboard's <see cref="IDriftUnitSource"/> from the Devbook
/// context's sync verdicts.
/// <para>
/// The store keeps one verdict per chapter; the dashboard lists units. Each unit's row
/// is its root chapter's — the one the unit is named for, where the sweep's unit verdict,
/// action and link sit beside the chapter's own — and falls back to the latest of its
/// chapters when the root was never filed.
/// </para>
/// <para>
/// The verdicts are optional: a host that composed no Sessions surface records none, and
/// the port then answers that it cannot, rather than that no sweep ever ran.
/// </para>
/// </summary>
public sealed class SyncVerdictDriftUnitSource(IDevbookSyncVerdicts? verdicts) : IDriftUnitSource
{
    public bool IsAvailable => verdicts is not null;

    public IReadOnlyList<DriftUnit> Units(DashboardRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (verdicts is null || string.IsNullOrWhiteSpace(repository.FullName)) return [];

        return Units(repository.Alias, verdicts.ForRepository(repository.FullName));
    }

    internal static IReadOnlyList<DriftUnit> Units(string alias, IReadOnlyList<DevbookSyncVerdict> recorded) =>
    [
        .. recorded
            .Where(verdict => !string.IsNullOrWhiteSpace(verdict.Unit))
            .GroupBy(verdict => verdict.Unit.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(unit => unit
                .OrderByDescending(verdict => verdict.IsUnitRoot)
                .ThenByDescending(verdict => verdict.RecordedAt)
                .First())
            .Select(verdict => new DriftUnit(
                alias,
                verdict.Unit.Trim(),
                verdict.UnitKind,
                verdict.Sync,
                verdict.SyncFrom,
                verdict.UnitVerdict,
                verdict.Action,
                verdict.Link,
                verdict.RecordedAt))
    ];
}
