namespace Backlog.Modules.Capture.Abstractions.DataTransferObjects;

/// <summary>
/// Every monitorable source and the choice made about each — exactly one entry
/// per <see cref="CaptureSourceKinds.Monitorable"/> kind, in that order, so a
/// screen can lay them out without first asking which are missing.
/// </summary>
public sealed record CaptureSourceSettings
{
    /// <summary>Off, and pointed at nothing, until somebody says otherwise. A
    /// source that started on would poll a target nobody chose.</summary>
    public IReadOnlyList<MonitoredSource> Sources { get; init; } =
        [.. CaptureSourceKinds.Monitorable.Select(kind => new MonitoredSource(kind, Enabled: false, Targets: []))];

    /// <summary>The entry for one kind. Falls back to an off, empty one rather
    /// than throwing, because a settings record made whole by
    /// <see cref="Sources"/>' default never lacks one.</summary>
    public MonitoredSource For(CaptureSourceKind kind) =>
        Sources.FirstOrDefault(source => source.Kind == kind)
        ?? new MonitoredSource(kind, Enabled: false, Targets: []);

    /// <summary>The sources a run looks at.</summary>
    public IReadOnlyList<MonitoredSource> Enabled => [.. Sources.Where(source => source.Enabled)];
}
