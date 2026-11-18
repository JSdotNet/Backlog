using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.Services;

namespace Backlog.Modules.Capture.Services;

/// <summary>
/// The module's own answer to <see cref="ICaptureSourceAvailability"/>, for a
/// host that composes Capture without saying whether the Events area is on:
/// every kind is offered but the event page.
/// <para>
/// Fail closed, because the event page source belongs to an area that starts
/// off. With nobody to say the area is on, the source stays withdrawn — the
/// state a fresh device is in — rather than appearing in a panel whose area
/// the person never switched on. The infrastructure adapter that reads the
/// <c>events</c> flag replaces this.
/// </para>
/// </summary>
internal sealed class DefaultCaptureSourceAvailability : ICaptureSourceAvailability
{
    /// <summary>Never raised: this answer never changes.</summary>
    public event Action? Changed
    {
        add { }
        remove { }
    }

    public bool IsOffered(CaptureSourceKind kind) => kind != CaptureSourceKind.EventPage;
}
