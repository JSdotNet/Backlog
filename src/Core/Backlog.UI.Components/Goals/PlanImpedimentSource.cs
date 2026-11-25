namespace Backlog.UI.Components.Goals;

/// <summary>
/// The hook a host supplies, as a cascading value, to name the open impediment that holds
/// a plan back (local ADR 0035 §5; local ADR 0062 §2–§3): the Tasks pane's rail and folded
/// plan lines read it to say what a Blocked plan waits on.
/// <para>
/// A cascading value for the reason <see cref="GoalControlSlot"/> is one: an impediment is
/// Progress's, and Tasks never calls Progress (local ADR 0035 §6). The pane hands over a
/// plan's tag and its open entries' ids and draws the title it gets back; it stores
/// nothing. With no source cascaded — Progress not composed, a storybook page that sets
/// none — a Blocked plan reads plain Blocked, as before.
/// </para>
/// </summary>
/// <param name="titleOf">The title of the first-raised open impediment that holds back the
/// plan (by its bare tag) or one of the entries (by id), or null when none does. Answered
/// from what the host already holds, so a render never waits on it.</param>
/// <param name="watch">Subscribes a callback raised when an answer may have changed, and
/// returns the subscription. Unset, nothing is ever raised.</param>
public sealed class PlanImpedimentSource(
    Func<string, IReadOnlyCollection<Guid>, string?> titleOf,
    Func<Action, IDisposable>? watch = null)
{
    private readonly Func<string, IReadOnlyCollection<Guid>, string?> _titleOf =
        titleOf ?? throw new ArgumentNullException(nameof(titleOf));

    /// <summary>The title of the open impediment holding back the plan
    /// <paramref name="planTag"/> — with or without its <c>+</c> — or one of
    /// <paramref name="entryIds"/>; null when none is linked.</summary>
    public string? TitleOf(string planTag, IReadOnlyCollection<Guid> entryIds)
    {
        ArgumentNullException.ThrowIfNull(planTag);
        ArgumentNullException.ThrowIfNull(entryIds);

        var title = _titleOf(planTag.TrimStart('+'), entryIds);

        return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
    }

    /// <summary>Raises <paramref name="changed"/> whenever an answer may have changed, until
    /// the returned subscription is disposed. It may be raised on any thread.</summary>
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
