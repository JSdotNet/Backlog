using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Capture.Features.RunCapture;

/// <summary>Look at every enabled source now. Carries nothing: which sources,
/// and what they point at, is the settings port's answer at the moment the run
/// starts.</summary>
public sealed record RunCaptureCommand;

/// <summary>
/// Runs each enabled source through its adapter and gathers what each one said.
/// <para>
/// Always a success, and deliberately: a source that has no adapter yet, or
/// whose adapter threw, is a line in the result rather than a failed run. The
/// reader pressed one button and is owed one answer per thing they switched on,
/// and a run that stopped at the first broken feed would hide every source
/// after it.
/// </para>
/// </summary>
public sealed class RunCaptureCommandHandler(
    ICaptureSourceSettings settings,
    IEnumerable<ICaptureSourceAdapter> adapters,
    TimeProvider clock) : ICommandHandler<RunCaptureCommand, Result<CaptureRunResultDto>>
{
    public async Task<Result<CaptureRunResultDto>> Handle(RunCaptureCommand command, CancellationToken cancellationToken = default)
    {
        var results = new List<CaptureRunSourceResult>();

        foreach (var source in settings.Current.Enabled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunSourceAsync(source, cancellationToken));
        }

        return Result.Success(new CaptureRunResultDto(results, clock.GetUtcNow()));
    }

    private async Task<CaptureRunSourceResult> RunSourceAsync(MonitoredSource source, CancellationToken cancellationToken)
    {
        var label = CaptureSourceKinds.Label(source.Kind);
        var adapter = adapters.FirstOrDefault(candidate => candidate.Kind == source.Kind);

        if (adapter is null)
        {
            return new CaptureRunSourceResult(source.Kind, 0, $"{label}: no adapter is available yet.");
        }

        try
        {
            return await adapter.RunAsync(source, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One source's failure is that source's line, not the run's.
            return new CaptureRunSourceResult(source.Kind, 0, $"{label}: {ex.Message}");
        }
    }
}
