namespace Backlog.Modules.Capture.Abstractions.Services;

/// <summary>
/// Whether a monitorable source is offered on this device right now — listed
/// in the Sources panel and read by the Capture button's run.
/// <para>
/// A question apart from <see cref="ICaptureSourceSettings"/>, because the
/// answer is not the person's choice about the source. The event page source
/// exists for the Events area and shows only while that area's <c>events</c>
/// flag is on, and the flag is Events' to name: Capture may not reference
/// Events, so the module asks this port and an adapter that may see both
/// answers it. A withdrawn source keeps whatever was stored for it — switched
/// on, pointed at its pages — so turning the area back on brings it back as
/// it was left.
/// </para>
/// </summary>
public interface ICaptureSourceAvailability
{
    /// <summary>Raised after the answer may have changed — a flag switched —
    /// so a panel listing the sources can redraw.</summary>
    event Action? Changed;

    /// <summary>Whether the kind is offered now. A host that cannot say is
    /// expected to withdraw what depends on it rather than offer it.</summary>
    bool IsOffered(CaptureSourceKind kind);
}
