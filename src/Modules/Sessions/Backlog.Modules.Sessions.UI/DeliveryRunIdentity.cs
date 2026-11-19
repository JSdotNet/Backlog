using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// One delivery run across readings: the dashboard that filed it, the worktree key it
/// was filed under, and its id — the same identity the run's card is keyed by. A
/// re-read hands the pane new <see cref="DeliveryRun"/> objects for the same runs, and
/// the record's own equality compares every figure, so neither reference nor value
/// equality still finds the run after one of its stages moved.
/// </summary>
internal sealed class DeliveryRunIdentity : IEqualityComparer<DeliveryRun>
{
    public static DeliveryRunIdentity Instance { get; } = new();

    private DeliveryRunIdentity()
    {
    }

    public static bool Same(DeliveryRun? left, DeliveryRun? right) => Instance.Equals(left, right);

    public bool Equals(DeliveryRun? x, DeliveryRun? y) =>
        ReferenceEquals(x, y)
        || (x is not null && y is not null
            && string.Equals(x.Dashboard, y.Dashboard, StringComparison.Ordinal)
            && string.Equals(x.Worktree, y.Worktree, StringComparison.Ordinal)
            && string.Equals(x.Id, y.Id, StringComparison.Ordinal));

    public int GetHashCode(DeliveryRun obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        return HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(obj.Dashboard ?? string.Empty),
            StringComparer.Ordinal.GetHashCode(obj.Worktree ?? string.Empty),
            StringComparer.Ordinal.GetHashCode(obj.Id ?? string.Empty));
    }
}
