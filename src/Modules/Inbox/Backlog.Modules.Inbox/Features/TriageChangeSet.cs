using Backlog.SharedKernel.Changes;

namespace Backlog.Modules.Inbox.Features;

/// <summary>
/// The change set a triage decision writes under (local ADR 0061 §2): the item it
/// clarifies and the task, plan or knowledge event that becomes its home (local
/// ADR 0054 §1) are one change set, so restoring it reopens the item and takes back what
/// it made.
/// <para>
/// A decision inside a wider change set — an agent's MCP call, which already names its
/// own — joins it; one with none current is the person's gesture in the Inbox pane,
/// labelled by the item it decided.
/// </para>
/// </summary>
internal static class TriageChangeSet
{
    /// <summary>Opens the change set for a decision on the item titled
    /// <paramref name="title"/>, unless one is current.</summary>
    public static ChangeContext.Scope Begin(string title) =>
        ChangeContext.BeginIfNone(ChangeActor.Person, $"Triage of {title}");

    /// <summary>Opens one change set for a decision on <paramref name="count"/> items at
    /// once, unless one is current.</summary>
    public static ChangeContext.Scope BeginBatch(int count) =>
        ChangeContext.BeginIfNone(ChangeActor.Person, count == 1 ? "Triage of 1 item" : $"Triage of {count} items");
}
