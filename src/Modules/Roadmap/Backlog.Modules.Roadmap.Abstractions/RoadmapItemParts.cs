using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.Abstractions;

/// <summary>
/// One roadmap item laid out one <b>part</b> per repository, each part at its own
/// repository's pace (ADR 0013, rulings 4 and 5 as amended on 2026-10-07; local ADR
/// 0019 for the counting).
/// <para>
/// A part is the gathered work filed in one repository. Each part counts the full points
/// of every task filed there, so a task filed under two repositories counts in both; a
/// task filed under none, or under a repository none of the parts stands for, goes to the
/// first part. A part starts on the first worked day on or after the latest of today,
/// the item's floor — the day after its predecessors end — and the day after the latest
/// end among the parts it waits on. The item's window runs from the earliest part start to
/// the latest part end.
/// </para>
/// <para>
/// One rule for every place a part is formed: the importer that stores the window, the
/// keep-up projection, the roadmap's bands and the dashboard's outlook. It lives here
/// rather than in the module for the reason <see cref="EffortWindow"/> does: the view and
/// the infrastructure readers reach the plan through the abstractions only. Parts are
/// worked out on every reading and never stored.
/// </para>
/// </summary>
public static class RoadmapItemParts
{
    /// <summary>
    /// <paramref name="item"/>'s parts and the window they make.
    /// </summary>
    /// <param name="item">The item as stored.</param>
    /// <param name="rollup">What it gathered, or <c>null</c> when that was not read.</param>
    /// <param name="paces">The pace in use per repository, and the working week every part
    /// is counted through.</param>
    /// <param name="today">The day open work is placed from; <c>null</c> places nothing and
    /// draws the stored window.</param>
    /// <param name="bandOf">The band a stored repository name lands in — an alias or a full
    /// name, the item's or a task's — or <c>null</c> for none. The paces' own resolution
    /// (<see cref="PacesInUseDto.AliasOf"/>) when not given.</param>
    /// <param name="floor">The earliest day a part whose work has not begun may start: the
    /// day after the item's latest predecessor ends. Today when not given, and never
    /// earlier than today.</param>
    public static RoadmapItemLayout Of(
        RoadmapItemDto item,
        RoadmapItemRollupDto? rollup,
        PacesInUseDto paces,
        DateOnly? today,
        Func<string, string?>? bandOf = null,
        DateOnly? floor = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(paces);

        bandOf ??= paces.AliasOf;

        var bands = BandsOf(item, bandOf);
        var links = rollup is null ? [] : RoadmapRollup.InDependencyOrder(rollup.BacklogEntries);

        // The bands each task is filed in, by index; first appearance of a key wins.
        var bandsOfTask = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<RoadmapGatheredLink>();
        foreach (var link in links)
        {
            if (bandsOfTask.ContainsKey(link.Key)) continue;

            var filed = link.Repositories
                .Select(name => IndexOf(bands, bandOf(name)))
                .Where(index => index >= 0)
                .Distinct()
                .Order()
                .ToList();

            if (filed.Count == 0) filed.Add(0);

            bandsOfTask[link.Key] = filed;
            ordered.Add(link);
        }

        var placement = PlacementOf(item, ordered, today);

        // A pace nobody could divide by places nothing: the work is not counted at no pace.
        if (placement is PartsPlacement.ByEffort or PartsPlacement.FromWork && bands.Any(band => paces.PaceOf(band.Name) <= 0))
        {
            placement = PartsPlacement.Stored;
        }

        if (placement is PartsPlacement.Stored) return Stored(item, bands, ordered, bandsOfTask, paces);

        var segments = Segments(ordered, bandsOfTask);
        var placed = new List<RoadmapItemPart>();

        foreach (var segment in segments)
        {
            var band = bands[segment.Band];
            var part = new RoadmapItemPart(
                band.Name,
                band.Aliases,
                segment.Phase,
                segment.Links,
                item.Start,
                item.End,
                paces.PaceOf(band.Name),
                segment.WaitsOn);

            placed.Add(placement is PartsPlacement.WhereItRan
                ? WhereItRan(part, item, today)
                : Placed(part, placed, today!.Value, floor, paces.Week));
        }

        if (placement is PartsPlacement.FromWork && item.EndPinned) Pin(placed, item.End);

        var start = placed.Min(part => part.Start);
        var end = placed.Max(part => part.End);

        // A repository the item names that holds none of its tasks draws over the window
        // the others make, so the item still shows it is filed there.
        var held = segments.Select(segment => segment.Band).ToHashSet();
        for (var index = 0; index < bands.Count; index++)
        {
            if (held.Contains(index)) continue;
            placed.Add(new RoadmapItemPart(bands[index].Name, bands[index].Aliases, 0, [], start, end, paces.PaceOf(bands[index].Name), []));
        }

        return new RoadmapItemLayout(placement, placed, start, end);
    }

    /// <summary>
    /// How the item's parts are placed: over the stored window when nothing was gathered or
    /// there is no today to place from; where the work ran once it is all done; from the
    /// work once some of it has begun; by effort while the import still sizes the window;
    /// otherwise — a due date or a person placed it — over the stored window.
    /// </summary>
    private static PartsPlacement PlacementOf(RoadmapItemDto item, List<RoadmapGatheredLink> ordered, DateOnly? today)
    {
        if (ordered.Count == 0) return PartsPlacement.Stored;
        if (ordered.All(link => link.IsDone)) return PartsPlacement.WhereItRan;
        if (today is null) return PartsPlacement.Stored;
        if (ordered.Any(IsBegun)) return PartsPlacement.FromWork;

        return EffortWindow.IsDerived(item) ? PartsPlacement.ByEffort : PartsPlacement.Stored;
    }

    /// <summary>
    /// A part of work not yet finished, placed after the parts before it in the list it
    /// waits on.
    /// <list type="bullet">
    /// <item>All its tasks done: drawn where that work ran.</item>
    /// <item>Not begun: from the first worked day on or after the latest of today, the
    /// floor and the day after the parts it waits on, for its estimated points at its own
    /// pace — one working week when nothing is sized.</item>
    /// <item>Begun: from the day its work began, when that was earlier than planned, to a
    /// forecast of its open points — an unestimated task counted as one — at its own pace,
    /// counted from the first worked day on or after the later of today and its start.</item>
    /// </list>
    /// </summary>
    private static RoadmapItemPart Placed(
        RoadmapItemPart part,
        List<RoadmapItemPart> before,
        DateOnly today,
        DateOnly? floor,
        WorkingHours week)
    {
        var earliest = Max(today, floor ?? today);
        foreach (var waited in part.WaitsOn) earliest = Max(earliest, before[waited].End.AddDays(1));

        var planned = EffortWindow.FirstWorkedDay(earliest, week);

        if (part.Links.All(link => link.IsDone))
        {
            var ran = Ran(part.Links);
            var start = ran.Start ?? planned;
            return part with { Start = start, End = Max(start, ran.End ?? today), Ran = true };
        }

        if (!part.Links.Any(IsBegun))
        {
            var effort = part.Links.Sum(link => Math.Max(0, link.Effort ?? 0));
            return part with { Start = planned, End = EffortWindow.EndFrom(planned, effort, part.Pace, week) };
        }

        var began = part.Links.Where(IsBegun).Select(link => link.StartedOn ?? link.CreatedOn).Where(day => day is not null).Min();
        var from = began is { } day && day < planned ? day : planned;
        var openFrom = EffortWindow.FirstWorkedDay(Max(today, from), week);
        var open = part.Links.Where(link => !link.IsDone).Sum(link => Math.Max(0, link.Effort ?? 1));
        var forecast = EffortWindow.ForecastEnd(openFrom, open, part.Pace, week);

        return part with { Start = from, End = forecast, Begun = true, OpenFrom = openFrom, Forecast = forecast };
    }

    /// <summary>A part of a finished item, drawn where its work ran. When nothing says when
    /// it began, the item's start stands; when nothing says when it ended, the item's end —
    /// never after today, because finished work cannot end in the future.</summary>
    private static RoadmapItemPart WhereItRan(RoadmapItemPart part, RoadmapItemDto item, DateOnly? today)
    {
        var ran = Ran(part.Links);
        var start = ran.Start ?? item.Start;
        var end = ran.End ?? (today is { } day && day < item.End ? day : item.End);

        return part with { Start = start, End = Max(start, end), Ran = true };
    }

    /// <summary>
    /// A person's pinned end, drawn on the open part that ends latest — on every one, when
    /// several tie — and never before the first day its open work can be drawn on. Every
    /// other part keeps its own forecast, and the moved part keeps its own beside the pin
    /// (Q2 of the 2026-10-07 amendment).
    /// </summary>
    private static void Pin(List<RoadmapItemPart> parts, DateOnly pinned)
    {
        var open = parts.Where(part => !part.Ran).ToList();
        if (open.Count == 0) return;

        var latest = open.Max(part => part.End);
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            if (part.Ran || part.End != latest) continue;

            parts[index] = part with { End = Max(pinned, part.OpenFrom ?? part.Start), Forecast = part.End };
        }
    }

    /// <summary>When a part's work ran: the earliest start its tasks say — a task's own
    /// started day, else the day it was created — to the latest completion.</summary>
    private static (DateOnly? Start, DateOnly? End) Ran(IReadOnlyList<RoadmapGatheredLink> links) =>
    (
        links.Select(link => link.StartedOn ?? link.CreatedOn).Where(day => day is not null).Min(),
        links.Select(link => link.CompletedOn).Where(day => day is not null).Max()
    );

    private static bool IsBegun(RoadmapGatheredLink link) =>
        link.Progress is RoadmapProgress.InProgress or RoadmapProgress.Done;

    /// <summary>One part per band, each over the item's stored window, holding every task
    /// filed in that band — a stored window is one window, so the work is not cut into
    /// the phases of a hand-over.</summary>
    private static RoadmapItemLayout Stored(
        RoadmapItemDto item,
        List<(string? Name, IReadOnlyList<string> Aliases)> bands,
        List<RoadmapGatheredLink> ordered,
        Dictionary<string, List<int>> bandsOfTask,
        PacesInUseDto paces)
    {
        List<RoadmapItemPart> parts =
        [
            .. bands.Select((band, index) => new RoadmapItemPart(
                band.Name,
                band.Aliases,
                0,
                [.. ordered.Where(link => bandsOfTask[link.Key].Contains(index))],
                item.Start,
                item.End,
                paces.PaceOf(band.Name),
                []))
        ];

        return new RoadmapItemLayout(PartsPlacement.Stored, parts, item.Start, item.End);
    }

    /// <summary>The bands the item's own repository names land in, in the order it names
    /// them and once each, or the one no-repository band when they land in none.</summary>
    private static List<(string? Name, IReadOnlyList<string> Aliases)> BandsOf(RoadmapItemDto item, Func<string, string?> bandOf)
    {
        var bands = item.RepositoryAliases
            .Select(name => (Name: name, Band: bandOf(name)))
            .Where(entry => entry.Band is not null)
            .GroupBy(entry => entry.Band!, StringComparer.OrdinalIgnoreCase)
            .Select(group => ((string?)group.Key, (IReadOnlyList<string>)[.. group.Select(entry => entry.Name)]))
            .ToList();

        if (bands.Count == 0) bands.Add((null, [.. item.RepositoryAliases]));

        return bands;
    }

    private static int IndexOf(List<(string? Name, IReadOnlyList<string> Aliases)> bands, string? band) =>
        band is null ? -1 : bands.FindIndex(entry => string.Equals(entry.Name, band, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The tasks grouped into one segment per band and phase, every segment after the
    /// segments it waits on.
    /// <para>
    /// A task has a phase in every band it is filed in: the number of hand-overs between
    /// repositories on the longest chain of waits leading to it there. Waiting on a task in
    /// the same band keeps the phase; waiting on one in another band starts the next. A
    /// task filed in two bands is waited on in both, so what waits on it waits on both.
    /// The tasks come in dependency order, so a task's waits are seen before it — except a
    /// wait the order released to break a circle, or one on work not gathered, which counts
    /// as nothing.
    /// </para>
    /// <para>
    /// A segment waits on the segments holding the tasks its own tasks wait on, but only on
    /// one of a lower phase: a wait the phases did not count — the circle the order broke —
    /// is no wait between segments either, so the segments never wait in a circle.
    /// </para>
    /// </summary>
    private static List<Segment> Segments(List<RoadmapGatheredLink> ordered, Dictionary<string, List<int>> bandsOfTask)
    {
        var phaseOf = new Dictionary<(string Key, int Band), int>(new TaskInBand());

        foreach (var link in ordered)
        {
            foreach (var band in bandsOfTask[link.Key])
            {
                var phase = 0;

                foreach (var wait in link.Waits)
                {
                    if (string.IsNullOrEmpty(wait) || !bandsOfTask.TryGetValue(wait, out var waitBands)) continue;

                    foreach (var waitBand in waitBands)
                    {
                        if (!phaseOf.TryGetValue((wait, waitBand), out var before)) continue;

                        phase = Math.Max(phase, before + (waitBand == band ? 0 : 1));
                    }
                }

                phaseOf[(link.Key, band)] = phase;
            }
        }

        var segments = ordered
            .SelectMany(link => bandsOfTask[link.Key].Select(band => (Band: band, Phase: phaseOf[(link.Key, band)], Link: link)))
            .GroupBy(entry => (entry.Phase, entry.Band))
            .OrderBy(group => group.Key.Phase)
            .ThenBy(group => group.Key.Band)
            .Select(group => (group.Key.Phase, group.Key.Band, Links: group.Select(entry => entry.Link).ToList()))
            .ToList();

        var indexOf = new Dictionary<(int Phase, int Band), int>();
        for (var index = 0; index < segments.Count; index++) indexOf[(segments[index].Phase, segments[index].Band)] = index;

        return
        [
            .. segments.Select(segment => new Segment(
                segment.Phase,
                segment.Band,
                segment.Links,
                [.. segment.Links
                    .SelectMany(link => link.Waits)
                    .Where(wait => !string.IsNullOrEmpty(wait) && bandsOfTask.ContainsKey(wait))
                    .SelectMany(wait => bandsOfTask[wait]
                        .Where(band => phaseOf.ContainsKey((wait, band)))
                        .Select(band => (Phase: phaseOf[(wait, band)], Band: band)))
                    .Where(target => target.Phase < segment.Phase)
                    .Select(target => indexOf[target])
                    .Distinct()
                    .Order()]))
        ];
    }

    private static DateOnly Max(DateOnly first, DateOnly second) => first > second ? first : second;

    private sealed record Segment(int Phase, int Band, IReadOnlyList<RoadmapGatheredLink> Links, IReadOnlyList<int> WaitsOn);

    /// <summary>A task's key — compared without regard to case, as the gathering compares
    /// it — in one band.</summary>
    private sealed class TaskInBand : IEqualityComparer<(string Key, int Band)>
    {
        public bool Equals((string Key, int Band) x, (string Key, int Band) y) =>
            x.Band == y.Band && string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Key, int Band) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key), obj.Band);
    }
}

/// <summary>How an item's parts were placed.</summary>
public enum PartsPlacement
{
    /// <summary>Every part over the item's stored window: a due date or a person placed
    /// it, nothing was gathered, or nothing was placed from today.</summary>
    Stored,

    /// <summary>Each part placed by its effort at its own pace, after what it waits on —
    /// an item the import still sizes by its effort, whose work has not begun.</summary>
    ByEffort,

    /// <summary>Work in flight: each begun part from the day its work began to a forecast
    /// of its open points, each part not begun placed by its effort, each done part where
    /// it ran.</summary>
    FromWork,

    /// <summary>Finished: each part where its work ran.</summary>
    WhereItRan
}

/// <summary>
/// One repository's part of a roadmap item — or, when the work hands over between
/// repositories and back, one phase of that repository's part.
/// </summary>
/// <param name="Band">The configured alias of the repository it is drawn under, or
/// <c>null</c> for the no-repository band.</param>
/// <param name="Aliases">The item's own repository names that land in this band.</param>
/// <param name="Phase">Its place in the hand-over, from 0: the number of hand-overs
/// between repositories on the longest chain of waits leading to its tasks.</param>
/// <param name="Links">Its tasks, in dependency order.</param>
/// <param name="Start">The first day it is drawn on.</param>
/// <param name="End">The last day it is drawn on.</param>
/// <param name="Pace">Its repository's pace in use, in story points a working week.</param>
/// <param name="WaitsOn">The positions in <see cref="RoadmapItemLayout.Parts"/> of the
/// parts it waits on, each earlier in the list.</param>
public sealed record RoadmapItemPart(
    string? Band,
    IReadOnlyList<string> Aliases,
    int Phase,
    IReadOnlyList<RoadmapGatheredLink> Links,
    DateOnly Start,
    DateOnly End,
    decimal Pace,
    IReadOnlyList<int> WaitsOn)
{
    /// <summary>Its tasks are all done, and it is drawn where that work ran.</summary>
    public bool Ran { get; init; }

    /// <summary>Some of its tasks are in progress or done, and some still open.</summary>
    public bool Begun { get; init; }

    /// <summary>The first day its open work is counted from, for a begun part.</summary>
    public DateOnly? OpenFrom { get; init; }

    /// <summary>Where its open work is forecast to be done, for a begun part — kept when a
    /// pinned end draws the part elsewhere, so the pin does not hide what the pace says.</summary>
    public DateOnly? Forecast { get; init; }

    /// <summary>A repository the item names that holds none of its tasks; drawn over the
    /// item's window.</summary>
    public bool Empty => Links.Count == 0;
}

/// <summary>An item's parts, and the window they make: the earliest part start to the
/// latest part end.</summary>
/// <param name="Placement">How the parts were placed.</param>
/// <param name="Parts">The parts, every part after the parts it waits on: by phase, then
/// in the order the item names its repositories.</param>
/// <param name="Start">The earliest part start.</param>
/// <param name="End">The latest part end.</param>
public sealed record RoadmapItemLayout(
    PartsPlacement Placement,
    IReadOnlyList<RoadmapItemPart> Parts,
    DateOnly Start,
    DateOnly End)
{
    /// <summary>Whether the work hands over from one repository to another.</summary>
    public bool HandsOver => Parts.Any(part => part.Phase > 0);
}
