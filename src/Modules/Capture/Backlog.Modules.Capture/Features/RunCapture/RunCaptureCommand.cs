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
/// <para>
/// What it does remember, per monitored target, is in the
/// <see cref="ICaptureTargetLedger"/>: whether it has looked at the target
/// before, and which entries it passed over there. A feed is not news on the
/// day it is added — a site whose feed lists every page it has would otherwise
/// fill the Inbox in one press — and one run takes a bounded number from a
/// target however many it suddenly lists. An import has no target and is
/// taken whole: it was asked for.
/// </para>
/// </summary>
public sealed class RunCaptureCommandHandler(
    ICaptureSourceSettings settings,
    IEnumerable<ICaptureSourceAdapter> adapters,
    ICaptureDelivery delivery,
    ICaptureRunLog log,
    ICaptureTargetLedger ledger,
    TimeProvider clock) : ICommandHandler<RunCaptureCommand, Result<CaptureRunResultDto>>
{
    /// <summary>How many entries the first look at a target hands over: enough
    /// to show the target works, few enough that a whole-site feed is not a
    /// morning's triage.</summary>
    public const int FirstLookCount = 5;

    /// <summary>How many new entries one run takes from one target after its
    /// first look. A news feed rarely gets near it between two presses; a site
    /// rebuild that re-dates every page does.</summary>
    public const int MaxNewPerTarget = 20;

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

        var tally = new Tally();
        var missingLists = tally.MissingLists;
        var notes = new List<string>();

        try
        {
            var findings = await adapter.RunAsync(source, cancellationToken);
            label = findings.Label ?? label;
            notes.AddRange(findings.Notes);

            // Nothing to make an item of. The receiving side would say the
            // same, but an id needs an entry and a row needs a title, so it is
            // dropped here rather than named and then refused.
            var entries = findings.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Title) && !string.IsNullOrWhiteSpace(entry.ExternalId));

            foreach (var group in entries.GroupBy(entry => entry.Target, StringComparer.Ordinal))
            {
                if (group.Key is null)
                {
                    foreach (var entry in group) await DeliverAsync(source.Kind, entry, tally, cancellationToken);
                }
                else if (await DeliverTargetAsync(source.Kind, group.Key, [.. group], tally, cancellationToken) is { } note)
                {
                    notes.Add(note);
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
        int? known = source.Kind == CaptureSourceKind.Import ? tally.AlreadyKnown : null;

        return new CaptureRunSourceResult(source.Kind, tally.Delivered, Describe(label, tally.Delivered, known, notes));
    }

    /// <summary>
    /// One monitored target's entries, newest first, against what the ledger
    /// says the run passed over there. The first look hands over the newest
    /// <see cref="FirstLookCount"/>; a later run hands over everything not
    /// passed over until <see cref="MaxNewPerTarget"/> of them were new. What
    /// was not handed over is passed over for good. Answers the target's note,
    /// or null when everything it had was handed over.
    /// </summary>
    private async Task<string?> DeliverTargetAsync(
        CaptureSourceKind kind,
        string target,
        IReadOnlyList<CapturedEntry> entries,
        Tally tally,
        CancellationToken cancellationToken)
    {
        // Stable, so entries a feed dates alike keep the feed's own order.
        var newestFirst = entries
            .DistinctBy(entry => entry.ExternalId, StringComparer.Ordinal)
            .Select(entry => (Entry: entry, Id: CaptureIds.For(kind, entry.ExternalId)))
            .OrderByDescending(pair => pair.Entry.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();
        var previous = ledger.PassedOverAt(kind, target);
        var firstLook = previous is null;

        // Never trimmed to what the feed lists today: one short listing — a
        // cached partial, a paged feed — would otherwise forget the archive,
        // and the next full one would hand it over twenty at a time.
        var passedOver = previous?.ToHashSet() ?? [];
        var offered = 0;
        var newHere = 0;
        var skipped = 0;

        foreach (var (entry, id) in newestFirst)
        {
            if (passedOver.Contains(id)) continue;

            if (firstLook ? offered >= FirstLookCount : newHere >= MaxNewPerTarget)
            {
                passedOver.Add(id);
                skipped++;
                continue;
            }

            offered++;
            if (await DeliverAsync(kind, entry, tally, cancellationToken) is CaptureDeliveryOutcome.Delivered or CaptureDeliveryOutcome.DeliveredUnfiled)
            {
                newHere++;
            }
        }

        ledger.Record(kind, target, passedOver);

        if (skipped == 0) return null;

        return firstLook
            ? $"{target}: first look, so only the newest {FirstLookCount} of its {newestFirst.Count} entries — the rest were already there"
            : $"{target}: more than {MaxNewPerTarget} new entries at once, so the older ones were passed over";
    }

    private async Task<CaptureDeliveryOutcome> DeliverAsync(CaptureSourceKind kind, CapturedEntry entry, Tally tally, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var item = new CaptureItem(
            CaptureIds.For(kind, entry.ExternalId),
            kind,
            entry.Title,
            entry.Url,
            entry.BodyMd,
            entry.PublishedAt ?? clock.GetUtcNow(),
            entry.Facts);

        var outcome = await delivery.DeliverAsync(item, cancellationToken);

        switch (outcome)
        {
            case CaptureDeliveryOutcome.Delivered:
                tally.Delivered++;
                break;
            case CaptureDeliveryOutcome.DeliveredUnfiled:
                tally.Delivered++;
                if (entry.Facts?.List is { } list && !tally.MissingLists.Contains(list, StringComparer.OrdinalIgnoreCase)) tally.MissingLists.Add(list);
                break;
            case CaptureDeliveryOutcome.AlreadyKnown:
                tally.AlreadyKnown++;
                break;
        }

        return outcome;
    }

    /// <summary>What one source's run has delivered so far — kept when a
    /// delivery fails part-way, because those items did land.</summary>
    private sealed class Tally
    {
        public int Delivered { get; set; }

        public int AlreadyKnown { get; set; }

        public List<string> MissingLists { get; } = [];
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
