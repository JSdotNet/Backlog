using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Backlog.Modules.Sync.Observability;

/// <summary>
/// The Sync context's own traces and metrics (inherited ADR 0010). One
/// <see cref="ActivitySource"/> and one <see cref="Meter"/> for the module, both
/// named for the module rather than for the host, so the signal reads the same
/// whichever process the handlers end up in.
/// <para>
/// Static because an <see cref="ActivitySource"/> is designed to be: it is inert
/// until a listener attaches, so a handler that starts an activity nobody is
/// listening to pays a null check. The host is what opts in, by naming
/// <see cref="Name"/> in its tracer and meter providers.
/// </para>
/// <para>
/// Custom attributes carry the <c>backlog.sync.</c> prefix per the guideline.
/// The owner and device ids are opaque GUIDs and appear in traces only —
/// deliberately never as a metric tag, because a tag whose cardinality is "one
/// per person" is a bill rather than a dimension.
/// </para>
/// </summary>
public static class SyncTelemetry
{
    /// <summary>The name a host passes to <c>AddSource</c> and
    /// <c>AddMeter</c>.</summary>
    public const string Name = "Backlog.Modules.Sync";

    /// <summary>Attribute names, written once so a query does not have to guess
    /// which of two spellings a given span used.</summary>
    public const string OwnerIdTag = "backlog.sync.owner_id";

    public const string DeviceIdTag = "backlog.sync.device_id";

    public const string BatchSizeTag = "backlog.sync.batch_size";

    /// <summary>Why a cursor was turned away — the error code, so the metric and
    /// the problem body a client saw use one vocabulary.</summary>
    public const string ReasonTag = "backlog.sync.reason";

    /// <summary>Which Android push call a push metric counts — one of
    /// <see cref="PushOperations"/>. Low cardinality by construction.</summary>
    public const string PushOperationTag = "backlog.sync.push_operation";

    /// <summary>The values <see cref="PushOperationTag"/> takes.</summary>
    public static class PushOperations
    {
        public const string Send = "send";
        public const string Remove = "remove";
    }

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    /// <summary>
    /// Cursors refused, by reason. Without it a cross-owner replay answers 403
    /// into the dark: the response goes to whoever sent it and nothing is left
    /// behind on this side. .devbook/arc42/adr/0005 asks for that attempt to be visible,
    /// and one counter with a low-cardinality reason tag is the whole of what
    /// that costs.
    /// </summary>
    public static readonly Counter<long> CursorRejected = Meter.CreateCounter<long>(
        "backlog.sync.cursor_rejected",
        unit: "{cursor}",
        description: "Pull cursors refused, tagged by the reason they were refused.");

    /// <summary>Android pushes the hub took (local ADR 0038 §4) — one per notification that was
    /// published or moved state.</summary>
    public static readonly Counter<long> PushSent = Meter.CreateCounter<long>(
        "backlog.sync.push_sent",
        unit: "{push}",
        description: "Android pushes handed to the push hub.");

    /// <summary>Android push calls that failed and were contained, by operation. A push failure
    /// never fails the request that caused it, so without this it would go unseen.</summary>
    public static readonly Counter<long> PushFailed = Meter.CreateCounter<long>(
        "backlog.sync.push_failed",
        unit: "{push}",
        description: "Android push calls that failed and were contained, tagged by operation.");
}
