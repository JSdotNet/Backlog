using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Modules.Tasks.DomainModels;

/// <summary>
/// Thrown when an <see cref="EntryStatus"/> transition is not permitted by the
/// backlog entry lifecycle.
/// </summary>
public sealed class InvalidStatusTransitionException : Exception
{
    public InvalidStatusTransitionException(EntryStatus from, EntryStatus to)
        : base($"Invalid status transition: {from} -> {to}.")
    {
        From = from;
        To = to;
    }

    public EntryStatus From { get; }

    public EntryStatus To { get; }
}
