using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Features.RunCapture;
using Backlog.SharedKernel.Changes;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Capture.Services;

/// <summary>
/// The published <see cref="ICaptureRunner"/> port, wired to the slice behind
/// it. Deliberately nothing but delegation, the way <c>RoadmapPlanning</c> is:
/// every rule lives in the handler, so there is no second place to look.
/// <para>
/// The handler always answers with a success — see <see cref="RunCaptureCommandHandler"/>
/// for why — so unwrapping the <see cref="Result{T}"/> here loses nothing a
/// caller could have acted on.
/// </para>
/// </summary>
internal sealed class CaptureRunner(
    ICommandHandler<RunCaptureCommand, Result<CaptureRunResultDto>> runCapture) : ICaptureRunner
{
    public async Task<CaptureRunResultDto> RunAsync(CancellationToken cancellationToken = default)
    {
        // The adapters write the Inbox as the system: one run is one change set
        // (local ADR 0061 §2), whatever started it.
        using var change = ChangeContext.Begin(ChangeActor.System, "running capture");
        return await HandleAsync(new RunCaptureCommand(), cancellationToken);
    }

    public Task<CaptureRunResultDto> ImportAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var import = new MonitoredSource(CaptureSourceKind.Import, Enabled: true, Targets: [manifestPath]);
        return ImportCoreAsync(new RunCaptureCommand(import), cancellationToken);
    }

    /// <summary>An import the person asked for is their change set, unless the
    /// gesture that asked opened one already.</summary>
    private async Task<CaptureRunResultDto> ImportCoreAsync(RunCaptureCommand command, CancellationToken cancellationToken)
    {
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "importing into the Inbox");
        return await HandleAsync(command, cancellationToken);
    }

    private async Task<CaptureRunResultDto> HandleAsync(RunCaptureCommand command, CancellationToken cancellationToken)
    {
        var result = await runCapture.Handle(command, cancellationToken);

        return result switch
        {
            Ok<CaptureRunResultDto>(var run) => run,
            Error error => throw new InvalidOperationException(error.Message),
        };
    }
}
