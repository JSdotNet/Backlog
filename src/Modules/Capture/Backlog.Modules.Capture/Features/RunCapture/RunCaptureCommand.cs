using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Capture.Features.RunCapture;

/// <summary>Look at every enabled source now — or, with <paramref name="Only"/>,
/// at that one source and nothing else.</summary>
/// <param name="Only">A source to run on its own, whatever the settings say:
/// an import, which is run with a file rather than switched on and polled
/// (local ADR 0017). Null for the Capture button's run, where which sources and
/// what they point at is the settings port's answer at the moment the run
/// starts.</param>
public sealed record RunCaptureCommand(MonitoredSource? Only = null);

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
/// <para>
/// Every run is written to the <see cref="ICaptureRunLog"/> as it is
/// reported, so "when was this source last looked at, and what did it find"
/// is answered from the same lines the pane showed. The run never reads the
/// log back: what is new is decided by id at delivery, not by remembering.
/// </para>
/// </summary>
public sealed class RunCaptureCommandHandler(
    ICaptureSourceSettings settings,
    IEnumerable<ICaptureSourceAdapter> adapters,
    ICaptureDelivery delivery,
    ICaptureRunLog log,
    TimeProvider clock) : ICommandHandler<RunCaptureCommand, Result<CaptureRunResultDto>>
{
    public async Task<Result<CaptureRunResultDto>> Handle(RunCaptureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var results = new List<CaptureRunSourceResult>();
        IReadOnlyList<MonitoredSource> sources = command.Only is { } only ? [only] : settings.Current.Enabled;

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunSourceAsync(source, cancellationToken));
        }

        var run = new CaptureRunResultDto(results, clock.GetUtcNow());
        log.Record(run);

        return Result.Success(run);
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
        var alreadyKnown = 0;
        var missingLists = new List<string>();
        var notes = new List<string>();

        try
        {
            var findings = await adapter.RunAsync(source, cancellationToken);
            label = findings.Label ?? label;
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
                    entry.PublishedAt ?? clock.GetUtcNow(),
                    entry.Facts);

                switch (await delivery.DeliverAsync(item, cancellationToken))
                {
                    case CaptureDeliveryOutcome.Delivered:
                        delivered++;
                        break;
                    case CaptureDeliveryOutcome.DeliveredUnfiled:
                        delivered++;
                        if (entry.Facts?.List is { } list && !missingLists.Contains(list, StringComparer.OrdinalIgnoreCase)) missingLists.Add(list);
                        break;
                    case CaptureDeliveryOutcome.AlreadyKnown:
                        alreadyKnown++;
                        break;
                }
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

        if (missingLists.Count > 0)
        {
            // Named, because the reader asked for these lists by name.
            var names = string.Join(", ", missingLists.Select(name => $"\"{name}\""));
            notes.Add(
                missingLists.Count == 1
                    ? $"no list is called {names}, so what was meant for it landed unfiled"
                    : $"no lists are called {names}, so what was meant for them landed unfiled");
        }

        // An import is run by hand, often twice, and "nothing new" after the
        // second run only reads as working when it says why. A monitor is run
        // over the same feed all the time, and its line stays as it was.
        int? known = source.Kind == CaptureSourceKind.Import ? alreadyKnown : null;

        return new CaptureRunSourceResult(source.Kind, delivered, Describe(label, delivered, known, notes));
    }

    /// <summary>"YouTube: 2 new items." — and, when a target had something to
    /// say, "Website: 1 new item · https://x: no feed found." An import adds
    /// what it already had: "Import (microsoft-todo): 0 new items · 3 already
    /// known." The full stop is the line's, so a note is never read as a
    /// sentence of its own.</summary>
    private static string Describe(string label, int delivered, int? alreadyKnown, List<string> notes)
    {
        var count = delivered == 1 ? "1 new item" : $"{delivered} new items";
        string[] known = alreadyKnown is { } k ? [$"{k} already known"] : [];
        string[] parts = [$"{label}: {count}", .. known, .. notes.Select(note => note.TrimEnd('.'))];

        return string.Join(" · ", parts) + ".";
    }
}
