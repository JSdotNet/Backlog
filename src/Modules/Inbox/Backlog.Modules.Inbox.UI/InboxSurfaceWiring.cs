using Microsoft.AspNetCore.Components;

namespace Backlog.Modules.Inbox.UI;

/// <summary>
/// What the Inbox surface draws that another context answers: the Capture button's run
/// and the sources panel beside it, both the Capture context's (local ADR 0042 §1).
/// <para>
/// One per window, with the window's view state, filled by the host that composes
/// Capture beside the Inbox; the surface reads it as it draws. A host that fills
/// nothing draws the Inbox without Capture — no Capture button and no Sources — so
/// the Inbox stays free of Capture's types.
/// </para>
/// </summary>
public sealed class InboxSurfaceWiring
{
    /// <summary>Runs one capture over the sources switched on; null offers no Capture.</summary>
    public Func<Task>? Capture { get; set; }

    /// <summary>Whether a capture run is in flight in this window.</summary>
    public Func<bool>? CaptureRunning { get; set; }

    /// <summary>What the last run said, as a sentence; null before the first.</summary>
    public Func<string?>? CaptureMessage { get; set; }

    /// <summary>What configures the sources, drawn in the pane's Sources slot.</summary>
    public RenderFragment? Sources { get; set; }
}
