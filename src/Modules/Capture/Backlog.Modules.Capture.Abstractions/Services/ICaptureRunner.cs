using Backlog.Modules.Capture.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Capture.Abstractions.Services;

/// <summary>
/// The one thing a host may ask Capture to do: look at every enabled source now.
/// <para>
/// The service contract ADR 0005 asks a module to publish, delegating to the
/// feature slice behind it (ADR 0009) the way <c>IRoadmapPlanning</c> does. The
/// command record stays inside the module; the shell calls this and reads the
/// DTO back.
/// </para>
/// <para>
/// It never throws for a source that could not be read — each source answers
/// for itself in the result, so one failing feed does not hide the others.
/// </para>
/// </summary>
public interface ICaptureRunner
{
    Task<CaptureRunResultDto> RunAsync(CancellationToken cancellationToken = default);
}
