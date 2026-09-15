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
/// Runs each enabled source through its adapter, hands everything it found to
/// the delivery port, and gathers what each source had to say.
/// <para>
/// The rules live here and not in the adapters. An adapter fetches and
/// normalises; this decides what is new — by delivering every entry under an
/// id derived from the entry (<see cref="CaptureIds"/>) and counting only the
/// ones the receiving side had not seen — what each source's line says, and
/// that an entry without a title is nothing to deliver.
/// </para>
/// <para>
/// Always a success, and deliberately: a source that has no adapter yet, or
/// whose adapter threw, is a line in the result rather than a failed run. The
/// reader pressed one button and is owed one answer per thing they switched on,
/// and a run that stopped at the first broken feed would hide every source
/// after it. A delivery failing part-way is the same, with the count so far
/// kept: those items did land, and the pane refreshes on the count.
/// </para>
/// </summary>
public sealed class RunCaptureCommandHandler(
    ICaptureSourceSettings settings,
    IEnumerable<ICaptureSourceAdapter> adapters,
    ICaptureDelivery delivery,
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

        var delivered = 0;
        var notes = new List<string>();

        try
        {
            var findings = await adapter.RunAsync(source, cancellationToken);
            notes.AddRange(findings.Notes);

            foreach (var entry in findings.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Nothing to make an item of. The receiving side would say the
                // same, but an id needs an entry and a row needs a title, so it
                // is dropped here rather than named and then refused.
                if (string.IsNullOrWhiteSpace(entry.Title) || string.IsNullOrWhiteSpace(entry.ExternalId)) continue;

                var item = new CaptureItem(
                    CaptureIds.For(source.Kind, entry.ExternalId),
                    source.Kind,
                    entry.Title,
                    entry.Url,
                    entry.BodyMd,
                    entry.PublishedAt ?? clock.GetUtcNow());

                if (await delivery.DeliverAsync(item, cancellationToken) == CaptureDeliveryOutcome.Delivered) delivered++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One source's failure is that source's line, not the run's.
            notes.Add(ex.Message);
        }

        return new CaptureRunSourceResult(source.Kind, delivered, Describe(label, delivered, notes));
    }

    /// <summary>"YouTube: 2 new items." — and, when a target had something to
    /// say, "Website: 1 new item · https://x: no feed found." The full stop is
    /// the line's, so a note is never read as a sentence of its own.</summary>
    private static string Describe(string label, int delivered, List<string> notes)
    {
        var count = delivered == 1 ? "1 new item" : $"{delivered} new items";
        string[] parts = [$"{label}: {count}", .. notes.Select(note => note.TrimEnd('.'))];

        return string.Join(" · ", parts) + ".";
    }
}
