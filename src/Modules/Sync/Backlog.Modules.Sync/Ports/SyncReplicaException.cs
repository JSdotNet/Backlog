using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// The replica could not answer, for a reason the calling device is entitled to
/// be told about.
/// <para>
/// An exception rather than a <c>Result</c> because of where these come from.
/// <see cref="ITaskReplica"/>'s methods answer questions about an owner's
/// documents; "the store is still starting up" and "that continuation is older
/// than the feed" are not answers to those questions, and threading a failure
/// union through four signatures to carry two conditions the handlers cannot
/// act on would put the noise everywhere and the handling nowhere. The host
/// catches this in one place and renders <see cref="Code"/> as the problem body.
/// </para>
/// <para>
/// It is deliberately not the catch-all. Anything else the store throws is a
/// bug or an outage nobody has classified, and those belong in the 500 the
/// exception handler already produces.
/// </para>
/// </summary>
/// <param name="code">One of <see cref="SyncErrorCodes"/>. The parameterless and
/// message-only constructors the framework guidelines ask for are left off on
/// purpose: an instance without a code could not be rendered, and a default
/// would guess.</param>
/// <param name="message">What to tell the device, safe to show a person.</param>
/// <param name="innerException">The store's own failure, for the trace.</param>
public sealed class SyncReplicaException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>The stable code the host maps to a status and a problem type —
    /// one of <see cref="SyncErrorCodes.SyncCursorExpired"/> or
    /// <see cref="SyncErrorCodes.ReplicaUnavailable"/>.</summary>
    public string Code { get; } = code;
}
