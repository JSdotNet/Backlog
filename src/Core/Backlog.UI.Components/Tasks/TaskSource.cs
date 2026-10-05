namespace Backlog.UI.Components.Tasks;

/// <summary>
/// Where a linked task came from, as a row, a panel and a badge need to know it:
/// the item's key, where it opens, and how the connector it came through
/// presents itself.
/// <para>
/// The library's own shape rather than the Tasks module's <c>SourceRef</c>,
/// because this library references no module. The host fills it from the
/// reference and the connector's descriptor; every field the screens draw comes
/// from those two, so a connector added later is drawn with no change here.
/// </para>
/// </summary>
/// <param name="DisplayKey">How a person names the item, such as <c>#412</c>.</param>
/// <param name="Url">Where the item opens at the source, or null when the
/// reference carries none.</param>
/// <param name="ConnectorName">The connector's display name, such as
/// <c>GitHub</c>. Null when this build has no connector by the stored id, and the
/// badge then shows the key alone under a neutral tone.</param>
/// <param name="Icon">The descriptor's icon name. Drawn when it names a provider
/// mark this library has, and left off otherwise.</param>
/// <param name="ColorToken">The descriptor's design token, such as
/// <c>color-primary-light</c>: the badge's ink and edge. Null is the neutral
/// tone.</param>
public sealed record TaskSource(
    string DisplayKey,
    string? Url = null,
    string? ConnectorName = null,
    string? Icon = null,
    string? ColorToken = null)
{
    /// <summary>Whether this build knows the connector the task came through.</summary>
    public bool IsKnownConnector => !string.IsNullOrWhiteSpace(ConnectorName);

    /// <summary>The source says the item cannot be worked on now.</summary>
    public bool Blocked { get; init; }

    /// <summary>Why the source says so, or null when it gave no reason.</summary>
    public string? BlockedReason { get; init; }

    /// <summary>The item carried more than one plan label; the first became the
    /// plan tag.</summary>
    public bool SeveralPlanLabels { get; init; }

    /// <summary>Done here while the source still holds the item open.</summary>
    public bool DoneHereOpenAtSource { get; init; }

    /// <summary>The item is no longer at the source, so the task was archived.</summary>
    public bool RemovedAtSource { get; init; }

    /// <summary>The flags, in the order a reader acts on them: what stops the work
    /// first, then what disagrees with the source, then what was tidied.</summary>
    public IReadOnlyList<TaskDetail> Flags =>
    [
        .. new TaskDetail?[]
        {
            Blocked ? new TaskDetail(TaskDetailKind.SourceBlocked, BlockedReason ?? "Blocked") : null,
            RemovedAtSource ? new TaskDetail(TaskDetailKind.SourceRemoved, "Removed at source") : null,
            DoneHereOpenAtSource ? new TaskDetail(TaskDetailKind.SourceOpen, "Open at source") : null,
            SeveralPlanLabels ? new TaskDetail(TaskDetailKind.SourcePlanLabels, "Several plan labels") : null,
        }.Where(flag => flag is not null).Select(flag => flag!)
    ];

    /// <summary>The badge's text: the connector's name and the key, or the key
    /// alone for a connector this build does not know.</summary>
    public string Label => IsKnownConnector ? $"{ConnectorName} {DisplayKey}" : DisplayKey;
}
