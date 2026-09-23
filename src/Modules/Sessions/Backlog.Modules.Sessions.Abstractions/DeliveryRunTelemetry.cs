using System.Text.Json;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// Where a coding session's own hook events land: the measured half of a delivery run
/// that <see cref="IDeliverySurfaceLifecycle"/> cannot supply.
/// <para>
/// The lifecycle operations are what a flow <em>says</em> about its run — the stages,
/// the gates, the summary. What the run actually cost — the tool calls it made, the
/// tokens it spent, how full its context window got — is only visible from inside the
/// session, which is why the dashboards collect it from hooks rather than from tools.
/// This port is the receiving end of the same arrangement: the
/// <c>backlog-tools</c> plugin forwards each hook payload to the desktop application,
/// and an implementation attributes it to the run in progress for that session.
/// </para>
/// <para>
/// Best effort by contract. A payload that names no run in progress, a transcript that
/// cannot be read, a field of the wrong type — each is recorded as nothing rather than
/// raised, because the caller is a hook that must never fail the tool call it reports
/// on, and has nothing it could do with an error anyway.
/// </para>
/// </summary>
public interface IDeliveryRunTelemetry
{
    /// <summary>Records one hook event, given as the host sent it —
    /// <c>hook_event_name</c>, <c>session_id</c>, <c>cwd</c>,
    /// <c>transcript_path</c> and the event's own fields.</summary>
    Task RecordAsync(JsonElement hookEvent, CancellationToken cancellationToken = default);
}
