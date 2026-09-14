using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Features.RunCapture;
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
        var result = await runCapture.Handle(new RunCaptureCommand(), cancellationToken);

        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(result.Error.Message);
    }
}
