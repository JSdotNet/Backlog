using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.Goals;

/// <summary>
/// The hook a host supplies, as a cascading value, to draw the goal control for one
/// piece of work.
/// <para>
/// A cascading value rather than a parameter, because the places that offer "Attach to
/// goal" are spread across the library and across contexts that may not reference
/// Progress — the task panel in Tasks, the plan shelf and the item editor in Roadmap, the
/// work cards and the pull requests pane. Each asks only for a
/// <see cref="GoalControlSlot"/> and draws <see cref="Render"/> for its
/// <see cref="GoalSubject"/>; the host that composes Progress cascades one whose fragment
/// reads and writes goals. With no slot cascaded — Progress not composed, a storybook
/// page that sets none — every one of those places draws exactly what it drew before.
/// </para>
/// <para>
/// A slot can be cascaded and still not be offered, when the host says so. A place that
/// wraps the control in a layout element of its own asks <see cref="IsOffered"/> first,
/// so it draws no empty wrapper, and <see cref="Watch"/>es the slot, so it draws one
/// again as that answer changes — whether or not the screen around it re-renders then.
/// </para>
/// </summary>
/// <param name="render">The control for one subject.</param>
/// <param name="isOffered">Whether the control is offered now. Unset, it always is.</param>
/// <param name="watch">Subscribes a callback raised when <paramref name="isOffered"/> may
/// have changed, and returns the subscription. Unset, nothing is ever raised.</param>
public sealed class GoalControlSlot(
    RenderFragment<GoalSubject> render,
    Func<bool>? isOffered = null,
    Func<Action, IDisposable>? watch = null)
{
    /// <summary>The control for one subject.</summary>
    public RenderFragment<GoalSubject> Render { get; } = render ?? throw new ArgumentNullException(nameof(render));

    /// <summary>Whether the host offers goals at all right now.</summary>
    public bool IsOffered => isOffered?.Invoke() ?? true;

    /// <summary>Raises <paramref name="changed"/> whenever <see cref="IsOffered"/> may have
    /// changed, until the returned subscription is disposed. It may be raised on any thread.</summary>
    public IDisposable Watch(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        return watch?.Invoke(changed) ?? NoWatch.Instance;
    }

    private sealed class NoWatch : IDisposable
    {
        public static readonly NoWatch Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// One component's watch on the <see cref="GoalControlSlot"/> cascaded to it: follows the
/// slot it is handed, raises the component's re-render when the slot's offer changes, and
/// lets go when the component is disposed.
/// </summary>
public sealed class GoalSlotWatch(Action changed) : IDisposable
{
    private GoalControlSlot? _slot;
    private IDisposable? _subscription;

    /// <summary>Watches <paramref name="slot"/>; a different slot than last time replaces
    /// the subscription, the same one keeps it.</summary>
    public void Follow(GoalControlSlot? slot)
    {
        if (ReferenceEquals(slot, _slot)) return;

        _subscription?.Dispose();
        _slot = slot;
        _subscription = slot?.Watch(changed);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _slot = null;
    }
}
