namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// Finishing a linked task's item at its source, as the save that finished the task
/// asks for it: once, and without waiting.
/// <para>
/// A port rather than a call on <see cref="ITaskConnector"/>, because the save
/// handler runs in every head and the connectors and connected targets exist only on
/// the desktop ones; a head without them registers nothing, and the save asks
/// nobody. What decides whether the source is asked at all — the target's
/// <see cref="ConnectedTarget.CompleteAtSource"/>, the connector's
/// <see cref="TaskConnectorCapabilities.CanComplete"/>, the source's own state — is
/// the implementation's, read when it runs.
/// </para>
/// </summary>
public interface ILinkedTaskWriteBack
{
    /// <summary>
    /// Asks for <paramref name="taskId"/>'s item to be finished at its source, off
    /// the caller's path: returns at once, never throws, and never waits on the
    /// network. A refusal is recorded on the task's <see cref="SourceRef"/>, not
    /// reported here.
    /// </summary>
    void Request(Guid taskId);
}
