using System.Globalization;
using System.Text.RegularExpressions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>How the one-plan view lays its entries out: in waves, or as the
/// Now / Next / Later list (local ADR 0062 §7).</summary>
public enum PlanFocusLayout
{
    /// <summary>Columns by dependency depth.</summary>
    Waves,

    /// <summary>The Now, Next and Later groups.</summary>
    List
}

/// <summary>One entry in its wave.</summary>
/// <param name="Entry">The entry with its state and group.</param>
/// <param name="Wave">Its wave, from 1.</param>
/// <param name="Number">How the card names it: <c>#14</c> off a numbered title, else
/// its place in the plan.</param>
/// <param name="Title">Its title without the step number.</param>
/// <param name="Detail">What the card says of it after its type: the merged pull
/// request, the stage, or its points and what it comes after.</param>
public sealed record PlanWaveEntry(PlanSectionEntry Entry, int Wave, string Number, string Title, string Detail)
{
    /// <summary>Whether it still waits, and so reads faded.</summary>
    public bool Waiting => Entry.Section is PlanSection.Waiting;

    /// <summary>The card's second line: <c>#14 · prompt · 5 pt · after #9</c>.</summary>
    public string MetaLine
    {
        get
        {
            var type = Entry.Row.PreviewType.ToString().ToLowerInvariant();

            return Detail.Length == 0 ? $"{Number} · {type}" : $"{Number} · {type} · {Detail}";
        }
    }

    /// <summary>The state as the card's coloured text; a waiting entry says nothing,
    /// its fading says it.</summary>
    public string StateText => Entry.State.State switch
    {
        PlanEntryQueueState.Waiting => string.Empty,
        PlanEntryQueueState.InPlanSession => "In session",
        _ => Entry.State.Label,
    };

    /// <summary>The state's colour and mark, as a class modifier.</summary>
    public string StateSlug => Entry.State.State switch
    {
        PlanEntryQueueState.Done or PlanEntryQueueState.Landed => "done",
        PlanEntryQueueState.InPlanSession => "session",
        PlanEntryQueueState.QueuedWithPlan or PlanEntryQueueState.Ready => "ready",
        PlanEntryQueueState.LeftForYou => "left",
        PlanEntryQueueState.HeldForYou => "held",
        _ => "waiting",
    };
}

/// <summary>One column of the waves view.</summary>
/// <param name="Number">The wave, from 1.</param>
/// <param name="Entries">Its entries, in the store's order.</param>
/// <param name="Current">Whether it is the wave the run is on: the first not yet
/// all done.</param>
public sealed record PlanWave(int Number, IReadOnlyList<PlanWaveEntry> Entries, bool Current)
{
    /// <summary>Whether every entry in it is done or landed.</summary>
    public bool Landed => Entries.All(entry => entry.Entry.Section is PlanSection.Done);

    /// <summary>The column's label: <c>landed</c>, <c>now — running and ready</c>, or
    /// <c>3 waiting</c>.</summary>
    public string Label
    {
        get
        {
            if (Landed) return "landed";

            if (Current)
            {
                var running = Entries.Any(entry => entry.Entry.Section is PlanSection.Now);
                var ready = Entries.Any(entry => entry.Entry.Section is PlanSection.Next);

                return (running, ready) switch
                {
                    (true, true) => "now — running and ready",
                    (true, false) => "now — running",
                    (false, true) => "now — ready",
                    _ => "now",
                };
            }

            var open = Entries.Count(entry => entry.Entry.Section is not PlanSection.Done);

            return $"{open.ToString(CultureInfo.InvariantCulture)} waiting";
        }
    }
}

/// <summary>
/// One plan's waves (local ADR 0062 §7;
/// <c>.devbook/domain/tasks/requirements.md#requirement-a-plans-waves-follow-its-dependencies</c>):
/// its entries in columns by dependency depth. Wave n holds the entries whose longest
/// chain of <c>after:</c> links inside the plan is n − 1 long, so an entry sits one
/// wave after the latest of the plan entries it waits on, however many waves back the
/// others are. A wait on something outside the plan counts as nothing, as does the
/// link that closes a cycle. Pure, over the plan's section entries.
/// </summary>
public static partial class PlanWaves
{
    /// <summary>Each entry's wave, from 1, by task id.</summary>
    /// <param name="entries">The plan's entries.</param>
    /// <param name="dependsOn">What an entry waits on, as resolved task ids.</param>
    public static IReadOnlyDictionary<string, int> WaveOf(
        IReadOnlyList<PlanSectionEntry> entries,
        Func<EntryRow, IReadOnlyList<string>> dependsOn)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(dependsOn);

        var byId = new Dictionary<string, PlanSectionEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) byId.TryAdd(entry.Row.TaskId, entry);

        var waves = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int Depth(string id)
        {
            if (waves.TryGetValue(id, out var known)) return known;

            // A link back to an entry still being placed closes a cycle: it counts as nothing.
            if (!visiting.Add(id)) return 0;

            var wave = 1;
            foreach (var blocker in dependsOn(byId[id].Row))
            {
                if (blocker is null || !byId.ContainsKey(blocker) || visiting.Contains(blocker)) continue;

                wave = Math.Max(wave, Depth(blocker) + 1);
            }

            visiting.Remove(id);
            waves[id] = wave;

            return wave;
        }

        foreach (var id in byId.Keys) Depth(id);

        return waves;
    }

    /// <summary>The plan's waves, each in the store's order, the current one marked.</summary>
    /// <param name="entries">The plan's entries, in the store's order.</param>
    /// <param name="dependsOn">What an entry waits on, as resolved task ids.</param>
    /// <param name="branch">The plan branch, for the pull request a done card names.</param>
    public static IReadOnlyList<PlanWave> Build(
        IReadOnlyList<PlanSectionEntry> entries,
        Func<EntryRow, IReadOnlyList<string>> dependsOn,
        string branch)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(dependsOn);

        var waveOf = WaveOf(entries, dependsOn);

        var numberOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Count; index++)
        {
            var named = PlanSections.NumberOf(entries[index].Row.PreviewTitle);
            numberOf.TryAdd(entries[index].Row.TaskId, named.StartsWith('#') ? named : "#" + (index + 1).ToString(CultureInfo.InvariantCulture));
        }

        var cards = entries
            .Select(entry => new PlanWaveEntry(
                entry,
                waveOf.GetValueOrDefault(entry.Row.TaskId, 1),
                numberOf[entry.Row.TaskId],
                TitleOf(entry.Row.PreviewTitle),
                DetailOf(entry, dependsOn(entry.Row).Where(numberOf.ContainsKey).Select(id => numberOf[id]).ToList(), branch)))
            .ToList();

        var count = cards.Count == 0 ? 0 : cards.Max(card => card.Wave);
        var columns = Enumerable.Range(1, count)
            .Select(number => cards.Where(card => card.Wave == number).ToList())
            .ToList();

        var current = columns.FindIndex(column => column.Any(card => card.Entry.Section is not PlanSection.Done));

        return [.. columns.Select((column, index) => new PlanWave(index + 1, column, index == current))];
    }

    /// <summary>The card's title: the entry's without its step number.</summary>
    public static string TitleOf(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        return StepNumber().Replace(title, string.Empty, 1).Trim();
    }

    /// <summary>
    /// What the card says after the type: a done entry's pull request (the one merged
    /// into the plan branch first, else its latest), or <c>by hand</c> for a done
    /// task or test; an entry in a session its stage; any other its points and the
    /// plan entries it comes after.
    /// </summary>
    public static string DetailOf(PlanSectionEntry entry, IReadOnlyList<string> after, string branch)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(after);

        var row = entry.Row;

        switch (entry.Section)
        {
            case PlanSection.Done:
                if (PullRequestOf(row, branch) is { } number) return $"PR #{number.ToString(CultureInfo.InvariantCulture)}";

                return row.PreviewType is EntryType.Task or EntryType.Test ? "by hand" : string.Empty;

            case PlanSection.Now:
                return entry.State.Stage ?? "in a session";
        }

        var parts = new List<string>();
        if (row.PreviewEffort is { } points) parts.Add($"{points.ToString(CultureInfo.InvariantCulture)} pt");
        if (after.Count > 0) parts.Add("after " + string.Join(", ", after));

        return string.Join(" · ", parts);
    }

    private static int? PullRequestOf(EntryRow row, string branch)
    {
        int? latest = null;

        foreach (var (link, pullRequest) in row.PullRequestStatuses)
        {
            if (pullRequest.State is LinkedPullRequestState.Merged
                && string.Equals(pullRequest.BaseRefName, branch, StringComparison.OrdinalIgnoreCase))
            {
                return link.Number;
            }
        }

        foreach (var link in row.PullRequestLinks) latest = link.Number;

        return latest;
    }

    [GeneratedRegex(@"^\s*\d+\s+-\s+")]
    private static partial Regex StepNumber();
}
