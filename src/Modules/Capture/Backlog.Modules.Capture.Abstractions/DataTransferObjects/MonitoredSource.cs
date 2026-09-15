namespace Backlog.Modules.Capture.Abstractions.DataTransferObjects;

/// <summary>
/// One source the reader can have watched: whether it is, and what it is
/// pointed at.
/// </summary>
/// <param name="Kind">Which source. Only a <see cref="CaptureSourceKinds.Monitorable"/>
/// kind is ever stored.</param>
/// <param name="Enabled">Whether a run looks at it.</param>
/// <param name="Targets">What to look at — channel URLs, page URLs, sender
/// addresses. Identifiers only, never a credential: this travels in a plain
/// settings file.</param>
public sealed record MonitoredSource(CaptureSourceKind Kind, bool Enabled, IReadOnlyList<string> Targets);
