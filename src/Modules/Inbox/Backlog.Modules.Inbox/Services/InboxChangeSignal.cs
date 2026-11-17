using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Modules.Inbox.Services;

/// <summary>The one <see cref="IInboxChangeSignal"/>: an event, raised as asked.</summary>
public sealed class InboxChangeSignal : IInboxChangeSignal
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
