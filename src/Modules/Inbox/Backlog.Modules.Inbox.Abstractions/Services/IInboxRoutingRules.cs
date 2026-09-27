namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// The reader's routing rules — which items to suggest which repository for —
/// as Settings writes them and Classification reads them.
/// <para>
/// A port on the Inbox's surface with its store under <c>src/Infrastructure</c>,
/// the same split the capture sources take: the rules are the reader's own,
/// local to the machine like the organiser, and kept in a plain file next to
/// the app's other per-user settings. See <see cref="InboxRoutingRule"/> for
/// what a rule says.
/// </para>
/// </summary>
public interface IInboxRoutingRules
{
    /// <summary>Raised after the rules changed.</summary>
    event Action? Changed;

    /// <summary>The rules in the order the reader wrote them. Empty until any
    /// are written.</summary>
    IReadOnlyList<InboxRoutingRule> Current { get; }

    /// <summary>Replaces the rules with what <paramref name="text"/> says, one
    /// per line. Answers null when they were kept, or the message to show: a
    /// line that does not read changes nothing, and a set that could not be
    /// saved for next time is still in use until the app closes.</summary>
    string? SetRules(string? text);
}

/// <summary>No rules at all — what the module reads when a host registers no
/// store, so Classification still suggests tags and a destination.</summary>
public sealed class NoInboxRoutingRules : IInboxRoutingRules
{
    public static readonly NoInboxRoutingRules Instance = new();

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<InboxRoutingRule> Current => [];

    public string? SetRules(string? text) => "Routing rules cannot be kept on this machine.";
}
