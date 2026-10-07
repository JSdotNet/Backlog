using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.UI.Components.Tasks;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// What the Tasks screens know about where linked tasks come from: the installed
/// connectors' descriptors and the connected targets.
/// <para>
/// Every screen reads a connector through its descriptor and nothing else (ADR
/// 0020 §3) — the badge, the Source filter, the settings page — so this is the one
/// place a <see cref="SourceRef"/> becomes something a screen draws, and a
/// connector added later needs no change to any of them.
/// </para>
/// </summary>
public sealed class LinkedTaskSources
{
    private readonly IReadOnlyList<ITaskConnector> _connectors;

    public LinkedTaskSources(IEnumerable<ITaskConnector> connectors, IConnectedTargets? targets = null)
    {
        _connectors = [.. connectors.DistinctBy(connector => connector.Descriptor.Id, StringComparer.Ordinal)];
        Targets = targets;
    }

    /// <summary>A host with no connector and nowhere to keep targets.</summary>
    public static LinkedTaskSources None { get; } = new([]);

    /// <summary>Where connected targets are kept, or null in a host that keeps none.</summary>
    public IConnectedTargets? Targets { get; }

    /// <summary>The installed connectors' descriptors, in their display order.</summary>
    public IReadOnlyList<TaskConnectorDescriptor> Descriptors =>
        [.. _connectors.Select(connector => connector.Descriptor).OrderBy(descriptor => descriptor.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>The installed connectors a person signs in to (those that also
    /// implement <see cref="ITaskConnectorSignIn"/>), in their display order.</summary>
    public IReadOnlyList<(TaskConnectorDescriptor Descriptor, ITaskConnectorSignIn SignIn)> SignIns =>
        [.. _connectors
            .Where(connector => connector is ITaskConnectorSignIn)
            .Select(connector => (connector.Descriptor, (ITaskConnectorSignIn)connector))
            .OrderBy(entry => entry.Descriptor.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>The descriptor for <paramref name="connectorId"/>, or null when this
    /// build has no connector by that id.</summary>
    public TaskConnectorDescriptor? Find(string? connectorId) =>
        _connectors.FirstOrDefault(connector => string.Equals(connector.Descriptor.Id, connectorId, StringComparison.Ordinal))?.Descriptor;

    /// <summary>The name a connector id is shown by: its descriptor's, or the id
    /// itself for one this build does not know.</summary>
    public string NameOf(string connectorId) => Find(connectorId)?.DisplayName ?? connectorId;

    /// <summary>
    /// A reference as the screens draw it, or null for local work. A connector this
    /// build does not know is drawn with its stored key under the neutral tone.
    /// </summary>
    public TaskSource? ToTaskSource(SourceRef? reference)
    {
        if (reference is null) return null;

        var descriptor = Find(reference.ConnectorId);
        return new TaskSource(
            string.IsNullOrWhiteSpace(reference.DisplayKey) ? reference.ExternalId : reference.DisplayKey,
            string.IsNullOrWhiteSpace(reference.Url) ? null : reference.Url,
            descriptor?.DisplayName,
            descriptor?.Icon,
            descriptor?.ColorToken)
        {
            Blocked = reference.Blocked,
            BlockedReason = reference.BlockedReason,
            SeveralPlanLabels = reference.HasFlag(LinkedTaskFlags.MultiplePlanTags),
            DoneHereOpenAtSource = reference.HasFlag(LinkedTaskFlags.DoneLocally),
            RemovedAtSource = reference.HasFlag(LinkedTaskFlags.Vanished),
            WriteBackRefusal = reference.WriteBackRefusal,
        };
    }

    /// <summary>
    /// The targets the installed connector by <paramref name="connectorId"/> offers
    /// to pick from on the settings page. A connector this build does not have
    /// offers none.
    /// <para>
    /// Never throws but for cancellation: a connector that fails while listing is
    /// answered as one that could not list, so the page falls back to typing the
    /// target rather than breaking.
    /// </para>
    /// </summary>
    public async Task<ConnectorTargetChoices> ListChoicesAsync(string? connectorId, CancellationToken cancellationToken)
    {
        var connector = _connectors.FirstOrDefault(candidate => string.Equals(candidate.Descriptor.Id, connectorId, StringComparison.Ordinal));
        if (connector is null) return ConnectorTargetChoices.None;

        try
        {
            return await connector.ListTargetChoicesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return ConnectorTargetChoices.Unavailable(
                $"{connector.Descriptor.DisplayName} could not list what there is to connect. Type it instead.");
        }
    }

    /// <summary>Whether the installed connector by <paramref name="connectorId"/> can
    /// finish an item at its source, so the settings page offers "Complete at the
    /// source" for its targets. False for a connector this build does not
    /// have.</summary>
    public bool CanComplete(string? connectorId) =>
        _connectors.FirstOrDefault(connector => string.Equals(connector.Descriptor.Id, connectorId, StringComparison.Ordinal))
            ?.Capabilities.CanComplete == true;
}
