namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// The pager's stack on the phone's Notifications tab
/// (<c>.devbook/domain/notifications/features.md#answering-from-another-device</c>): the open
/// notifications waiting on the person, oldest first, one per screen, and where the pager
/// stands among them. Pure: it holds no clock and draws nothing, and moving through it never
/// answers anything — the reply path stays <see cref="NotificationViewProjection.ReplyAsync"/>.
/// </summary>
public sealed class NotificationStack
{
    // Every item the pager has put on screen: at the end, the ones still waiting were passed
    // unanswered (skipped); the rest arrived by a refresh and were never shown (new).
    private readonly HashSet<Guid> _seen = [];
    private List<PhoneNotification> _items;
    private int _index;
    private Guid? _answering;

    /// <summary>A stack of the items in <paramref name="notifications"/> that wait on the
    /// person, opened on the oldest.</summary>
    public NotificationStack(IReadOnlyList<PhoneNotification> notifications)
    {
        _items = Waiting(notifications);
        Land(0);
    }

    /// <summary>The items in the stack, oldest first.</summary>
    public IReadOnlyList<PhoneNotification> Items => _items;

    /// <summary>The item on screen, or null at the end of the stack.</summary>
    public PhoneNotification? Current => _index < _items.Count ? _items[_index] : null;

    /// <summary>The 1-based position of <see cref="Current"/>, or null at the end.</summary>
    public int? Position => Current is null ? null : _index + 1;

    /// <summary>How many items the stack holds.</summary>
    public int Total => _items.Count;

    /// <summary>What the end of the stack reports, or null while an item is on screen.</summary>
    public NotificationStackEnd? End => Current is null
        ? new NotificationStackEnd(
            Skipped: _items.Count(item => _seen.Contains(item.Id)),
            New: _items.Count(item => !_seen.Contains(item.Id)))
        : null;

    /// <summary>Whether <see cref="Previous"/> moves: anywhere but the first item.</summary>
    public bool CanGoPrevious => _index > 0;

    /// <summary>Whether <see cref="Next"/> moves: while an item is on screen.</summary>
    public bool CanGoNext => Current is not null;

    /// <summary>Whether <paramref name="notification"/> waits on the person: an open question
    /// they may answer, or answer again after a rejection, one only the owner takes an answer
    /// to, or an open alert. A reply on its way and everything settled are not.</summary>
    public static bool IsWaiting(PhoneNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return notification.Standing is NotificationStanding.Answerable
            or NotificationStanding.Rejected
            or NotificationStanding.OwnerOnly
            or NotificationStanding.Alert;
    }

    /// <summary>Moves to the next item, or past the last to the end; does nothing at the end.</summary>
    public void Next()
    {
        if (CanGoNext) Land(_index + 1);
    }

    /// <summary>Moves to the previous item, or from the end back to the last; does nothing on
    /// the first.</summary>
    public void Previous()
    {
        if (CanGoPrevious) Land(_index - 1);
    }

    /// <summary>Goes through the stack again from the end: every item still in it was skipped
    /// or arrived unseen, so the pager lands on the oldest of them.</summary>
    public void Restart() => Land(0);

    /// <summary>
    /// The person is answering <paramref name="id"/>, the item on screen: a refresh keeps it
    /// on screen though the projection already reads it as
    /// <see cref="NotificationStanding.Sending"/>, so its confirmation shows before the pager
    /// moves on. <see cref="AfterAnswer"/> or <see cref="CancelAnswer"/> ends the hold.
    /// </summary>
    public void BeginAnswer(Guid id) => _answering = id;

    /// <summary>The phone could not keep the answer: the card is no longer held, and stays in
    /// the stack while it still waits.</summary>
    public void CancelAnswer() => _answering = null;

    /// <summary>
    /// The person answered <paramref name="id"/>: it leaves the stack at once, ahead of the
    /// refresh that reads it as <see cref="NotificationStanding.Sending"/>. Answered on screen,
    /// the pager lands on the item after it, or the end; answered elsewhere, the item on screen
    /// stays.
    /// </summary>
    public void AfterAnswer(Guid id)
    {
        if (_answering == id) _answering = null;

        var at = _items.FindIndex(item => item.Id == id);
        if (at < 0) return;

        _items.RemoveAt(at);
        Land(at < _index ? _index - 1 : _index);
    }

    /// <summary>
    /// Takes the projection's fresh <paramref name="notifications"/> — a pull, a resume, the
    /// poll. The item on screen stays while it still waits, or while it is being answered,
    /// wherever arrivals join around it; one that left (closed, or answered elsewhere) gives
    /// way to the next waiting item after it, else the end. At the end, the pager stays there.
    /// </summary>
    public void Refresh(IReadOnlyList<PhoneNotification> notifications)
    {
        var current = Current;
        var items = Waiting(notifications);

        if (current is not null && current.Id == _answering && items.TrueForAll(item => item.Id != current.Id))
        {
            var after = items.FindIndex(item => Later(item, current));
            items.Insert(after < 0 ? items.Count : after, current);
        }

        int index;
        if (current is null)
        {
            index = items.Count;
        }
        else if (items.FindIndex(item => item.Id == current.Id) is var still and >= 0)
        {
            index = still;
        }
        else
        {
            var after = items.FindIndex(item => Later(item, current));
            index = after < 0 ? items.Count : after;
        }

        _items = items;
        Land(index);
    }

    private void Land(int index)
    {
        _index = index;
        if (Current is { } shown) _seen.Add(shown.Id);
    }

    private static List<PhoneNotification> Waiting(IReadOnlyList<PhoneNotification> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return [.. notifications.Where(IsWaiting).OrderBy(Key)];
    }

    private static bool Later(PhoneNotification item, PhoneNotification than) =>
        Comparer<(DateTimeOffset, Guid)>.Default.Compare(Key(item), Key(than)) > 0;

    private static (DateTimeOffset CreatedAt, Guid Id) Key(PhoneNotification notification) =>
        (notification.Notification.CreatedAt, notification.Id);
}

/// <summary>What the pager shows past the last item: all caught up, or how many items it
/// passed over that still wait and how many arrived since without being shown.</summary>
/// <param name="Skipped">Items in the stack the person was shown and moved past unanswered.</param>
/// <param name="New">Items in the stack that arrived by a refresh and were never shown.</param>
public sealed record NotificationStackEnd(int Skipped, int New = 0)
{
    /// <summary>Whether nothing waits on the person any more.</summary>
    public bool AllCaughtUp => Skipped == 0 && New == 0;
}
