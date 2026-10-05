using System.Collections.Concurrent;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.UI.Components.Tasks;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// What the Tasks screens know about where linked tasks come from: the installed
/// connectors' descriptors, the connected targets, and who "me" is at each source.
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
    private readonly ConcurrentDictionary<string, Task<string?>> _me = new(StringComparer.Ordinal);

    public LinkedTaskSources(IEnumerable<ITaskConnector> connectors, IConnectedTargets? targets = null)
    {
        _connectors = [.. connectors.DistinctBy(connector => connector.Descriptor.Id, StringComparer.Ordinal)];
        Targets = targets;

        // Who "me" is changes with the account: a sign-in after "me" was asked would
        // otherwise leave "Assigned to me" judging by nobody until the app restarts.
        // Both live for the app's lifetime, so the handler is never taken off.
        foreach (var connector in _connectors)
        {
            if (connector is not ITaskConnectorSignIn signIn) continue;

            var id = connector.Descriptor.Id;
            signIn.AccountChanged += () => _me.TryRemove(id, out _);
        }
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
        };
    }

    /// <summary>Whether the Tasks pane's "Assigned to me" starts on: some target
    /// that is synced asks for it (ADR 0020 §9).</summary>
    public bool AssignedToMeByDefault =>
        Targets?.List().Any(target => target.Enabled && target.AssignedToMeByDefault) ?? false;

    /// <summary>
    /// Who "me" is at <paramref name="connectorId"/>, asked once and kept for the
    /// session. Null when no account is connected, the connector is not installed,
    /// or the source could not be asked — and "Assigned to me" then keeps that
    /// source's tasks rather than hiding work it cannot judge.
    /// </summary>
    public Task<string?> WhoAmIAsync(string connectorId) =>
        _me.GetOrAdd(connectorId, id => AskAsync(_connectors.FirstOrDefault(connector => connector.Descriptor.Id == id)));

    /// <summary>"Me" at <paramref name="connectorId"/> when it is already known,
    /// without asking.</summary>
    public string? KnownMe(string connectorId) =>
        _me.TryGetValue(connectorId, out var asked) && asked.IsCompletedSuccessfully ? asked.Result : null;

    private static async Task<string?> AskAsync(ITaskConnector? connector)
    {
        if (connector is null) return null;

        try
        {
            var me = await connector.WhoAmIAsync(CancellationToken.None).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(me) ? null : me.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
