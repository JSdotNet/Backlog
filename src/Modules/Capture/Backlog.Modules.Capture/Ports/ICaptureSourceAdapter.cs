using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Capture.Ports;

/// <summary>
/// One source's monitor: what actually looks at a channel, a page or a mailbox
/// and turns what it finds into captures.
/// <para>
/// A port declared here and answered in <c>src/Infrastructure</c>, one adapter
/// per <see cref="Kind"/>. None ships yet — the run reports an enabled source
/// with no adapter as such, so the settings screen and the button can exist
/// before the first monitor does.
/// </para>
/// </summary>
public interface ICaptureSourceAdapter
{
    /// <summary>Which source this adapter answers for.</summary>
    CaptureSourceKind Kind { get; }

    /// <summary>Looks at the source's targets and reports what it found. May
    /// throw; the run turns a throw into that source's message rather than
    /// letting it end the run.</summary>
    Task<CaptureRunSourceResult> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default);
}
