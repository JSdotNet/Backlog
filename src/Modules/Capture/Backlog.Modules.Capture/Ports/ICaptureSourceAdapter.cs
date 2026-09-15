using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Capture.Ports;

/// <summary>
/// One source's monitor: what actually looks at a channel, a page or a mailbox
/// and says what is there.
/// <para>
/// A port declared here and answered in <c>src/Infrastructure</c>, one adapter
/// per <see cref="Kind"/>. An adapter only fetches and normalises: it does not
/// know what the Inbox already holds, does not decide what is new, and does not
/// deliver anything. The run does all three, which is what keeps every rule in
/// the handler and every adapter a thin reader of one kind of source.
/// </para>
/// <para>
/// A source with no adapter is still an ordinary thing — the run reports it as
/// such per source, so the settings screen can offer a kind before its monitor
/// ships.
/// </para>
/// </summary>
public interface ICaptureSourceAdapter
{
    /// <summary>Which source this adapter answers for.</summary>
    CaptureSourceKind Kind { get; }

    /// <summary>Looks at every one of the source's targets and reports what it
    /// found and what went wrong per target. One target failing is a note, not
    /// a throw; a throw is the adapter as a whole failing, and the run turns it
    /// into that source's message rather than letting it end the run.</summary>
    Task<CaptureSourceFindings> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default);
}
