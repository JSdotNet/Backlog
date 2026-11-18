using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Capture.Events;

/// <summary>
/// Answers <see cref="ICaptureSourceAvailability"/> from the Events area's
/// flag: the event page source is offered while <see cref="EventsFeatures.Events"/>
/// is on, and every other kind always.
/// <para>
/// Here rather than in the module because the key is Events' and Capture may
/// not see Events; an adapter may see both. With no feature settings to ask —
/// a host that composed Capture without them — the event page stays withdrawn,
/// the module default's fail-closed answer, since the flag starts off.
/// </para>
/// <para>
/// The flag's <c>Changed</c> is passed straight through: it is raised for any
/// feature, which is more often than this answer changes, and a panel redraw
/// costs less than a second copy of the flag kept here to compare against.
/// </para>
/// </summary>
internal sealed class EventsAreaSourceAvailability(IAppFeatureSettings? features = null) : ICaptureSourceAvailability
{
    public event Action? Changed
    {
        add
        {
            if (features is not null) features.Changed += value;
        }
        remove
        {
            if (features is not null) features.Changed -= value;
        }
    }

    public bool IsOffered(CaptureSourceKind kind) =>
        kind != CaptureSourceKind.EventPage
        || (features is not null && features.IsEnabled(EventsFeatures.Events));
}
