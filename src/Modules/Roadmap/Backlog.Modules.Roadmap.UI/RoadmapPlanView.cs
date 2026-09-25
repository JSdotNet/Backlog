using System.Globalization;

using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Roadmap;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>One repository the plan may be filed against, as the band sees it.</summary>
/// <param name="Alias">The normalized alias the plan stores.</param>
/// <param name="Title">What to write on the band — the repository's full name.</param>
/// <param name="Colour">Which of the sanctioned identity hues this repository wears,
/// 1 to 5, or null for a band drawn neutral.
/// <para>
/// Handed in rather than worked out here. The hue says which repository, and which
/// repository is which is a workspace fact this context deliberately does not know —
/// it holds aliases as opaque strings precisely so it stays independent of repository
/// management. It is also the answer three other surfaces are reading, and a roadmap
/// that decided its own would be a second answer to the same question. See
/// <c>.design/color-scheme.md#band-identity-tokens</c>.
/// </para></param>
public sealed record PlannedRepository(string Alias, string Title, int? Colour = null);

/// <summary>Everything <c>RoadmapTimeline</c> needs, in the four shapes it takes.</summary>
public sealed record RoadmapTimelineModel(
    IReadOnlyList<RoadmapGroup> Groups,
    IReadOnlyList<RoadmapBar> Bars,
    IReadOnlyList<RoadmapMilestone> Milestones,
    IReadOnlyList<RoadmapLink> Links)
{
    public static RoadmapTimelineModel Empty { get; } = new([], [], [], []);

    public bool HasAnythingToDraw => Groups.Count > 0;
}

/// <summary>
/// Turns a stored plan into what the timeline draws: a band per configured
/// repository, the person's own lanes inside each, and the dependency arrows
/// between them. An item filed in several repositories is drawn once in each of
/// their bands, as that repository's part of it.
/// <para>
/// A pure function of the plan and the configured repositories, deliberately
/// separate from the component. Everything interesting about reading a plan — which
/// band something lands in, what happens to an alias nobody configured any more, how
/// priority becomes something you can see — is decided here, where it can be
/// asserted on without rendering anything.
/// </para>
/// </summary>
public static class RoadmapPlanView
{
    /// <summary>The band for work that names no repository, or names one that is no
    /// longer configured. Not an error state: a plan is allowed to contain work
    /// nobody has filed yet.</summary>
    public const string UnfiledGroupId = "unfiled";

    public const string UnfiledGroupTitle = "Unfiled";

    /// <summary>
    /// The band every milestone sits on, at the top of the chart.
    /// <para>
    /// One shared band rather than a milestones row inside each repository's band. A
    /// release date is a fact about the plan, not about one project, and a row per band
    /// spent a line of a short chart repeating the same dates. At the top because that
    /// is where a reader looks for the dates everything else is measured against —
    /// beside the quarters, reading as part of the header rather than as one more lane.
    /// </para>
    /// <para>
    /// It takes no colour, for the same reason the unfiled band does not: a hue here
    /// means "which repository", and this band is not one.
    /// </para>
    /// </summary>
    public const string MilestoneGroupId = "milestones";

    public const string MilestoneGroupTitle = "Dates";

    private const string MilestoneRowId = "milestones::all";

    /// <param name="plan">The stored plan.</param>
    /// <param name="repositories">The configured repositories, in Settings order.</param>
    /// <param name="rollups">What each item gathered, keyed by item id, as
    /// <c>IRoadmapItemRollup.GatherPlanAsync</c> answers it. An item missing from it
    /// — or no rollups at all — draws with no steps, which is what an item nothing
    /// points at looks like anyway.</param>
    public static RoadmapTimelineModel From(
        RoadmapPlanDto? plan,
        IReadOnlyList<PlannedRepository>? repositories,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto>? rollups = null)
    {
        if (plan is null || plan.IsEmpty) return RoadmapTimelineModel.Empty;

        var configured = Configured(repositories);
        var contradicting = plan.Contradictions.Select(contradiction => contradiction.NodeId).ToHashSet();

        // Ordered before anything is grouped, so lanes appear in the order the work
        // actually starts rather than in whatever order the file happened to list
        // it. Two runs over the same plan must draw the same picture.
        var items = plan.Items
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .SelectMany(item => PartsOf(item, configured, rollups))
            // Again by start once split, so a segment late in its item's window is
            // stacked after work that begins before it — first-fit needs the order.
            .OrderBy(part => part.Start)
            .ThenBy(part => part.Item.Title, StringComparer.CurrentCulture)
            .ToList();

        // Milestones are not grouped by repository: they all share one band at the top.
        var milestones = plan.Milestones
            .OrderBy(milestone => milestone.On)
            .ThenBy(milestone => milestone.Title, StringComparer.CurrentCulture)
            .ToList();

        var stacked = Stack(items);
        var groups = BuildGroups(items, stacked.Rows, milestones.Count > 0, configured);
        var drawn = groups.SelectMany(group => group.RowList).Select(row => row.Id).ToHashSet();

        var bars = items
            .Select(part => Bar(part, stacked.RowOf[part.BarId], contradicting, configured))
            .Where(bar => drawn.Contains(bar.RowId))
            .ToList();

        var markers = milestones
            .Select(milestone => Marker(milestone, MilestoneRowId, contradicting))
            .Where(marker => drawn.Contains(marker.RowId))
            .ToList();

        // What actually reached the chart, and in which band, so an arrow is only
        // drawn when both of its ends are on screen — and between two parts in one
        // repository's band where both ends have a part there.
        var bandOfBar = items.ToDictionary(part => part.BarId, part => part.GroupId, StringComparer.Ordinal);
        var segmented = items.Where(part => part.IsSegment).Select(part => part.Item.Id).ToHashSet();
        var placed = bars
            .Select(bar => (bar.Id, GroupId: bandOfBar[bar.Id], bar.Start, bar.End))
            .Concat(markers.Select(marker => (marker.Id, GroupId: MilestoneGroupId, Start: marker.On, End: marker.On)))
            .Where(placedBar => NodeIdOf(placedBar.Id) is not null)
            .GroupBy(placedBar => NodeIdOf(placedBar.Id)!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var handOvers = items
            .Where(part => drawn.Contains(stacked.RowOf[part.BarId]))
            .SelectMany(part => (part.WaitsForParts ?? []).Select(before => new RoadmapLink(before, part.BarId)))
            .Where(link => bars.Any(bar => bar.Id == link.FromId));

        return new RoadmapTimelineModel(groups, bars, markers, [.. Links(plan, placed, segmented), .. handOvers]);
    }

    /// <summary>What separates an item's id from its band in the id of one of its
    /// parts: <c>&lt;item id&gt;@&lt;band&gt;</c>. An item drawn in one band keeps
    /// its bare id.</summary>
    public const char PartSeparator = '@';

    /// <summary>The plan's own id behind a bar or a marker the timeline reported
    /// on. A bar that is one repository's part of an item names the item, so moving
    /// or opening any part moves or opens the item itself.</summary>
    public static Guid? NodeIdOf(string? barId)
    {
        if (barId is null) return null;

        var separator = barId.IndexOf(PartSeparator);
        var node = separator < 0 ? barId : barId[..separator];

        return Guid.TryParse(node, out var id) ? id : null;
    }

    /// <summary>
    /// One item, as the bars it is drawn as: one per band its repositories land in.
    /// <para>
    /// A plan filed in two repositories is two pieces of work to the people doing it —
    /// each repository holds its own share of the tasks — so each band shows its
    /// share, with a progress fill read from the tasks filed there. The window is
    /// still the item's: it is one stored item, so moving any part moves all of them.
    /// </para>
    /// <para>
    /// A gathered task goes to the part of every repository it is filed in. One filed
    /// nowhere, or somewhere none of the parts stands for, goes to the first part, so
    /// no task drops out of the item's progress because of where it was filed.
    /// </para>
    /// </summary>
    private static IEnumerable<ItemPart> PartsOf(
        RoadmapItemDto item,
        List<PlannedRepository> configured,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto>? rollups)
    {
        var bands = item.RepositoryAliases
            .Select(alias => (Alias: alias, GroupId: GroupIdFor([alias], configured)))
            .GroupBy(entry => entry.GroupId, StringComparer.OrdinalIgnoreCase)
            .Select(group => (GroupId: group.Key, Aliases: group.Select(entry => entry.Alias).ToList()))
            .ToList();

        var links = rollups is not null && rollups.TryGetValue(item.Id, out var rollup)
            ? RoadmapRollup.InDependencyOrder(rollup.BacklogEntries)
            : [];

        if (bands.Count <= 1)
        {
            yield return new ItemPart(
                item,
                item.Id.ToString(),
                GroupIdFor(item.RepositoryAliases, configured),
                item.RepositoryAliases,
                Steps(links),
                PartCount: 1);
            yield break;
        }

        var shares = bands.ToDictionary(
            band => band.GroupId,
            _ => new List<RoadmapGatheredLink>(),
            StringComparer.OrdinalIgnoreCase);

        var bandOfTask = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            var filed = link.Repositories
                .Select(id => BandOfRepository(id, configured))
                .Where(shares.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (filed.Count == 0) filed.Add(bands[0].GroupId);

            foreach (var groupId in filed) shares[groupId].Add(link);

            bandOfTask.TryAdd(link.Key, filed[0]);
        }

        // Work whose tasks hand over from one repository to another is drawn as the
        // sequence it is: a segment per repository per phase, rather than one bar per
        // repository all spanning the same window as though the parts ran side by side.
        var segments = Sequence(item, links, bandOfTask, bands);
        if (segments is not null)
        {
            foreach (var segment in segments) yield return segment;
            yield break;
        }

        foreach (var (groupId, aliases) in bands)
        {
            yield return new ItemPart(
                item,
                $"{item.Id}{PartSeparator}{groupId}",
                groupId,
                aliases,
                Steps(shares[groupId]),
                bands.Count);
        }
    }

    /// <summary>
    /// An item's parts as the sequence its tasks hand over in, or null when nothing
    /// hands over — when no task waits on a task filed in another repository, every
    /// repository's part can run the whole window and is drawn that way.
    /// <para>
    /// A task's phase is the number of repository hand-overs on the longest chain of
    /// waits leading to it: waiting on a task in its own repository keeps the phase,
    /// waiting on one elsewhere starts the next. Every repository's tasks in one phase
    /// form one segment. The window is shared out between the phases by effort — each
    /// phase as long as its largest segment, an unestimated task counted as one point
    /// so it still takes some time — and every segment of a phase spans that phase.
    /// </para>
    /// <para>
    /// A repository the item is filed in that holds none of its tasks keeps a part
    /// running the whole window: the item still names it, and dropping it would hide
    /// that.
    /// </para>
    /// </summary>
    private static List<ItemPart>? Sequence(
        RoadmapItemDto item,
        IReadOnlyList<RoadmapGatheredLink> ordered,
        Dictionary<string, string> bandOfTask,
        List<(string GroupId, List<string> Aliases)> bands)
    {
        // Ordered so a task's waits are seen before it; a wait inside a cycle, or on
        // something not gathered, counts as nothing.
        var phaseOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var handsOver = false;

        foreach (var link in ordered)
        {
            var band = bandOfTask[link.Key];
            var phase = 0;

            foreach (var wait in link.Waits)
            {
                if (!phaseOf.TryGetValue(wait, out var before)) continue;

                var crosses = !string.Equals(bandOfTask[wait], band, StringComparison.OrdinalIgnoreCase);
                handsOver |= crosses;
                phase = Math.Max(phase, before + (crosses ? 1 : 0));
            }

            phaseOf[link.Key] = phase;
        }

        if (!handsOver) return null;

        var groups = ordered
            .GroupBy(link => (Phase: phaseOf[link.Key], Band: bandOfTask[link.Key]))
            .OrderBy(group => group.Key.Phase)
            .ThenBy(group => bands.FindIndex(band => string.Equals(band.GroupId, group.Key.Band, StringComparison.OrdinalIgnoreCase)))
            .Select(group => (group.Key.Phase, group.Key.Band, Links: group.ToList()))
            .ToList();

        var phases = groups.Max(group => group.Phase) + 1;
        var weights = Enumerable.Range(0, phases)
            .Select(phase => groups
                .Where(group => group.Phase == phase)
                .Select(group => group.Links.Sum(link => Math.Max(1, link.Effort ?? 1)))
                .DefaultIfEmpty(0)
                .Max())
            .ToList();

        var slices = Slices(item.Start, item.End, weights);

        string BarIdOf(int phase, string band) => $"{item.Id}{PartSeparator}{band}{SegmentSeparator}{phase + 1}";

        var parts = new List<ItemPart>();
        var partCount = groups.Count + bands.Count(band => groups.All(group => !string.Equals(group.Band, band.GroupId, StringComparison.OrdinalIgnoreCase)));

        foreach (var (phase, band, links) in groups)
        {
            var keys = links.Select(link => link.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Only the waits that cross into another repository become arrows: a wait
            // inside one repository is that segment's own business, drawn in its steps.
            var waitsFor = links
                .SelectMany(link => link.Waits)
                .Where(wait => !keys.Contains(wait) && phaseOf.ContainsKey(wait)
                    && !string.Equals(bandOfTask[wait], band, StringComparison.OrdinalIgnoreCase))
                .Select(wait => BarIdOf(phaseOf[wait], bandOfTask[wait]))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            parts.Add(new ItemPart(
                item,
                BarIdOf(phase, band),
                band,
                bands.First(entry => string.Equals(entry.GroupId, band, StringComparison.OrdinalIgnoreCase)).Aliases,
                Steps(links),
                partCount,
                slices[phase].Start,
                slices[phase].End,
                waitsFor));
        }

        foreach (var (groupId, aliases) in bands.Where(band =>
                     groups.All(group => !string.Equals(group.Band, band.GroupId, StringComparison.OrdinalIgnoreCase))))
        {
            parts.Add(new ItemPart(item, $"{item.Id}{PartSeparator}{groupId}", groupId, aliases, [], partCount));
        }

        return parts;
    }

    /// <summary>The item's window cut into consecutive, non-overlapping slices, one
    /// per weight and each as long as its share of the total — at least one day, so a
    /// phase never vanishes on a short window.</summary>
    private static List<(DateOnly Start, DateOnly End)> Slices(DateOnly start, DateOnly end, List<int> weights)
    {
        var days = Math.Max(weights.Count, end.DayNumber - start.DayNumber + 1);
        var total = Math.Max(1, weights.Sum());

        var slices = new List<(DateOnly, DateOnly)>();
        var used = 0;
        var weightSoFar = 0;

        for (var index = 0; index < weights.Count; index++)
        {
            weightSoFar += weights[index];

            var boundary = index == weights.Count - 1
                ? days
                : (int)Math.Round((double)days * weightSoFar / total, MidpointRounding.AwayFromZero);

            // Every slice keeps at least a day, and leaves one for each slice after it.
            boundary = Math.Clamp(boundary, used + 1, days - (weights.Count - index - 1));

            slices.Add((start.AddDays(used), start.AddDays(boundary - 1)));
            used = boundary;
        }

        return slices;
    }

    /// <summary>The item's window after one of its bars was moved or resized to
    /// <paramref name="change"/>: each edge of the item moves as far as that edge of the
    /// bar did. For a bar that is the whole item that is the change itself; for one
    /// segment of a hand-over it moves the whole sequence with it.</summary>
    public static (DateOnly Start, DateOnly End) ItemWindowFor(RoadmapItemDto item, RoadmapBar bar, RoadmapChange change) =>
    (
        item.Start.AddDays(change.Start.DayNumber - bar.Start.DayNumber),
        item.End.AddDays(change.End.DayNumber - bar.End.DayNumber)
    );

    /// <summary>What separates a part's band from its place in the sequence, in the id
    /// of one segment of an item whose tasks hand over between repositories:
    /// <c>&lt;item id&gt;@&lt;band&gt;#&lt;phase&gt;</c>.</summary>
    public const char SegmentSeparator = '#';

    /// <summary>The band a task's stored repository lands in: the configured
    /// repository it names by alias or by full name, else the unfiled band.</summary>
    private static string BandOfRepository(string repositoryId, List<PlannedRepository> configured) =>
        configured.FirstOrDefault(repository =>
                string.Equals(repository.Alias, repositoryId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(repository.Title, repositoryId, StringComparison.OrdinalIgnoreCase))?.Alias
            ?? UnfiledGroupId;

    /// <summary>One bar's worth of an item: the whole of it, one repository's part, or
    /// one segment of the sequence its tasks hand over in — which alone has dates of
    /// its own, and the other segments it waits on.</summary>
    private sealed record ItemPart(
        RoadmapItemDto Item,
        string BarId,
        string GroupId,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<RoadmapStep> Steps,
        int PartCount,
        DateOnly? SegmentStart = null,
        DateOnly? SegmentEnd = null,
        IReadOnlyList<string>? WaitsForParts = null)
    {
        public DateOnly Start => SegmentStart ?? Item.Start;

        public DateOnly End => SegmentEnd ?? Item.End;

        public bool IsSegment => SegmentStart is not null;
    }

    private static List<PlannedRepository> Configured(IReadOnlyList<PlannedRepository>? repositories) =>
        [.. (repositories ?? [])
            .Where(repository => !string.IsNullOrWhiteSpace(repository.Alias))
            .GroupBy(repository => repository.Alias, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];

    /// <summary>
    /// Which band a node belongs in: the first of its aliases that is actually
    /// configured, or the unfiled band.
    /// <para>
    /// Asked once per alias by <see cref="PartsOf"/>, which draws an item in every
    /// band its aliases reach — each band showing that repository's part. The parts
    /// share the item's window, so moving one moves all of them and the plan never
    /// appears to disagree with itself.
    /// </para>
    /// <para>
    /// An alias that no longer matches a configured repository falls to the unfiled
    /// band rather than making a band of its own. The alias itself is never
    /// discarded — it stays in the stored plan and on the bar's facets, so
    /// configuring that repository again puts the work back where it was.
    /// </para>
    /// </summary>
    private static string GroupIdFor(IReadOnlyList<string> aliases, List<PlannedRepository> configured)
    {
        var match = aliases.FirstOrDefault(alias =>
            configured.Any(repository => string.Equals(repository.Alias, alias, StringComparison.OrdinalIgnoreCase)));

        return match is null ? UnfiledGroupId : match;
    }

    /// <summary>
    /// The lane a row belongs to, read back out of its id — for a drop onto a row, which
    /// the timeline reports by id alone. Null for an id the view did not build.
    /// <para>
    /// A stacked row names its lane the same way the lane's first row does, so work
    /// dropped onto the second row of "platform" is still filed under "platform".
    /// </para>
    /// </summary>
    public static string? LaneOf(string rowId)
    {
        var separator = rowId.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0) return null;

        var lane = rowId[(separator + 2)..];

        var stack = lane.LastIndexOf("::", StringComparison.Ordinal);
        if (stack >= 0 && int.TryParse(lane[(stack + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            lane = lane[..stack];
        }

        return lane.Length == 0 ? null : lane;
    }

    /// <summary>
    /// The first row of a lane is <c>group::lane</c>, as it always was; the rows stacked
    /// under it add their position, <c>group::lane::2</c> and on.
    /// </summary>
    private static string LaneRowId(string groupId, string lane, int stack) =>
        stack == 0 ? $"{groupId}::{lane}" : $"{groupId}::{lane}::{stack + 1}";

    /// <summary>
    /// Which row of its lane each item is drawn on, and how many rows each lane needs.
    /// <para>
    /// A row is one line of bars, so two items of one lane whose windows overlap cannot
    /// share one: the later bar is drawn over the earlier and the reader sees one piece
    /// of work where there are two. Each item takes the first row of its lane whose last
    /// bar has ended before it starts, and a new row when none has. Work that follows
    /// on in sequence still reads as one line; only real overlap costs a row.
    /// </para>
    /// <para>
    /// The items arrive ordered by start, which is what makes first-fit a good packing
    /// and keeps the picture the same from one read of the plan to the next.
    /// </para>
    /// </summary>
    private static (Dictionary<string, string> RowOf, Dictionary<(string GroupId, string Lane), int> Rows) Stack(
        List<ItemPart> items)
    {
        var rowOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var ends = new Dictionary<(string GroupId, string Lane), List<DateOnly>>();

        foreach (var part in items)
        {
            var key = (part.GroupId, Lane(part.Item.Lane));
            if (!ends.TryGetValue(key, out var rows)) ends[key] = rows = [];

            var stack = rows.FindIndex(end => end < part.Start);
            if (stack < 0)
            {
                stack = rows.Count;
                rows.Add(part.End);
            }
            else
            {
                rows[stack] = part.End;
            }

            rowOf[part.BarId] = LaneRowId(part.GroupId, key.Item2, stack);
        }

        return (rowOf, ends.ToDictionary(entry => entry.Key, entry => entry.Value.Count));
    }

    private static string Lane(string? lane) => string.IsNullOrWhiteSpace(lane) ? "Planned" : lane.Trim();

    private static List<RoadmapGroup> BuildGroups(
        List<ItemPart> items,
        Dictionary<(string GroupId, string Lane), int> stackedRows,
        bool hasMilestones,
        List<PlannedRepository> configured)
    {
        // Configured order first, so the bands read the way Settings lists them, then
        // the unfiled band last — it is where things end up rather than somewhere
        // they were put.
        //
        // The band is labelled with the repository's alias rather than its full name,
        // and that is a layout decision as much as a naming one. The label is written
        // down the side of the band, so its length is a floor on how short the band
        // can be: "JSdotNet/Backlog" needs 138px of height whatever its rows need,
        // which was enough on its own to put a scrollbar in a band that had room for
        // every row. "backlog" needs 63px. The alias is also the name the person
        // chose and the one the plan stores; the full name is still what the
        // Repository filter offers, where there is room for it.
        var order = configured
            .Select(repository => (Id: repository.Alias, Title: repository.Alias, repository.Colour))
            .Append((Id: UnfiledGroupId, Title: UnfiledGroupTitle, Colour: (int?)null));

        var bands = new List<RoadmapGroup>();

        // The dates band first, and colourless. It is where a reader looks for what
        // everything else is measured against, and it is not a repository.
        if (hasMilestones)
        {
            bands.Add(new RoadmapGroup(
                MilestoneGroupId,
                MilestoneGroupTitle,
                [new RoadmapRow(MilestoneRowId, "Milestones", RoadmapRowKind.Milestones)]));
        }

        foreach (var (id, title, hue) in order)
        {
            var lanes = items
                .Where(entry => entry.GroupId == id)
                .Select(entry => Lane(entry.Item.Lane))
                .Distinct(StringComparer.CurrentCulture)
                .ToList();

            if (lanes.Count == 0) continue;

            // Every row of a stacked lane carries the lane's name, not a blank: the name is
            // how a bar on it describes where it sits to anyone who cannot see the chart.
            List<RoadmapRow> rows =
            [
                .. lanes.SelectMany(lane => Enumerable.Range(0, stackedRows[(id, lane)])
                    .Select(stack => new RoadmapRow(LaneRowId(id, lane, stack), lane)))
            ];

            // The hue the repository wears, as Settings resolved it. The unfiled band
            // arrives with none and stays neutral, which reads as "nobody said" rather
            // than as one more project.
            bands.Add(new RoadmapGroup(id, title, rows, BandColour(hue)));
        }

        return bands;
    }

    /// <summary>
    /// The band's hue, as the token the stylesheet knows it by.
    /// <para>
    /// A number in, a token name out, and nothing in between — which of the sanctioned
    /// hues a repository wears was settled in Settings, because it is a fact about the
    /// repository rather than about this plan and because three other surfaces are
    /// reading the same answer. See
    /// <c>.design/color-scheme.md#band-identity-tokens</c>.
    /// </para>
    /// <para>
    /// It says which repository and nothing else — no status, no severity, no priority
    /// — and it is never the only thing saying it: the band is labelled with its alias
    /// down its own side and every bar names its band in its accessible name. Null is
    /// a neutral band, which is what the unfiled and milestone bands get: a hue here
    /// means "which repository", and neither of those is one.
    /// </para>
    /// </summary>
    private static string? BandColour(int? hue) =>
        hue is null ? null : $"var(--color-band-{hue})";

    /// <summary>
    /// A group is handed no colour, deliberately.
    /// <para>
    /// `.design/color-scheme.md` allows the product exactly one saturated hue and
    /// forbids a second semantic palette, so six separable band colours are not
    /// available to spend here. A colourless group draws neutral, which the library
    /// documents as "nobody said" rather than as another category — and priority is
    /// carried by the shade ramp below, which is an ordinal ramp of the one hue and
    /// is what that file does allow.
    /// </para>
    /// </summary>
    private static RoadmapBar Bar(
        ItemPart part,
        string rowId,
        HashSet<Guid> contradicting,
        List<PlannedRepository> configured) =>
        new(
            part.BarId,
            rowId,
            part.Item.Title,
            part.Start,
            part.End,
            Shade(part.Item.Priority),
            Facets(part.Item, part.Aliases, configured),
            Detail(part.Item, contradicting, part.PartCount, part.IsSegment),
            Steps: part.Steps);

    /// <summary>
    /// The item's gathered tasks as the steps drawn inside its bar (ADR 0013,
    /// ruling 6).
    /// <para>
    /// Tasks only. A knowledge chapter is gathered too, but it is a reference, not a
    /// step of the work: it has no status to colour and no place in an order of
    /// <c>after:</c> edges. So the bar's fill and its "n unestimated" count are the
    /// tasks' figures, and a chapter the item references changes neither.
    /// </para>
    /// <para>
    /// Ordered by the tasks' own dependencies through
    /// <see cref="RoadmapRollup.InDependencyOrder"/>, which considers only edges
    /// between tasks this item gathered. Nothing is stored: order, status and
    /// progress are all read off the rollup each time the plan is drawn.
    /// </para>
    /// </summary>
    private static IReadOnlyList<RoadmapStep> Steps(IReadOnlyList<RoadmapGatheredLink> ordered)
    {
        // TryAdd rather than ToDictionary: the rollup has normally de-duplicated the
        // keys already, and a drawing is not the place to throw if it had not.
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in ordered) titles.TryAdd(link.Key, link.Title);

        return
        [
            .. ordered.Select(link => new RoadmapStep(
                link.Key,
                link.Title,
                link.Effort,
                Tone(link.Progress),
                Word(link.Progress),
                WaitsFor(link, titles)))
        ];
    }

    /// <summary>Roadmap's progress onto the status badge's colours. The four words
    /// are the four the backlog's status badge already paints, so a step and the
    /// entry it stands for are the same colour.</summary>
    public static RoadmapStepTone Tone(RoadmapProgress? progress) => progress switch
    {
        RoadmapProgress.Planned => RoadmapStepTone.Draft,
        RoadmapProgress.Ready => RoadmapStepTone.Ready,
        RoadmapProgress.InProgress => RoadmapStepTone.InProgress,
        RoadmapProgress.Done => RoadmapStepTone.Done,
        _ => RoadmapStepTone.Unknown
    };

    private static string Word(RoadmapProgress? progress) => progress switch
    {
        RoadmapProgress.Planned => "Planned",
        RoadmapProgress.Ready => "Ready",
        RoadmapProgress.InProgress => "In progress",
        RoadmapProgress.Done => "Done",
        _ => "No status"
    };

    private static string? WaitsFor(RoadmapGatheredLink link, Dictionary<string, string> titles)
    {
        var names = link.Waits
            .Select(key => titles.TryGetValue(key, out var title) ? title : null)
            .OfType<string>()
            .ToList();

        return names.Count == 0 ? null : "After " + string.Join(", ", names);
    }

    /// <summary>
    /// Planning priority as a lightness step: critical strongest, low lightest.
    /// <para>
    /// An ordinal ramp of one hue, which is the one multi-value encoding the colour
    /// scheme permits. Shade is never the only carrier: the priority is written into
    /// the bar's accessible name and offered as a filter, so nothing here depends on
    /// telling four tints apart.
    /// </para>
    /// </summary>
    private static int Shade(PlanningPriority priority) => priority switch
    {
        PlanningPriority.Critical => 0,
        PlanningPriority.High => 1,
        PlanningPriority.Medium => 2,
        _ => 3
    };

    private static List<RoadmapFacet> Facets(RoadmapItemDto item, IReadOnlyList<string> aliases, List<PlannedRepository> configured)
    {
        var facets = new List<RoadmapFacet>();

        // The aliases this bar stands for. An item drawn once carries every one, so
        // filtering by a repository finds work that only mentions it second; one
        // repository's part of an item carries its own, so the filter shows that
        // repository's part and not its neighbour's.
        facets.AddRange(aliases.Select(alias => new RoadmapFacet("Repository", TitleFor(alias, configured))));
        facets.Add(new RoadmapFacet("Priority", Word(item.Priority)));
        facets.Add(new RoadmapFacet("Lane", Lane(item.Lane)));

        // The tag the item carries, so the timeline can filter by it — and so work
        // grouped under one tag is found together. Only when there is one to show.
        if (!string.IsNullOrEmpty(item.Tag)) facets.Add(new RoadmapFacet("Tag", item.Tag));

        return facets;
    }

    private static string Detail(RoadmapItemDto item, HashSet<Guid> contradicting, int partCount, bool segment = false)
    {
        var parts = new List<string> { $"{Word(item.Priority)} priority" };

        if (!string.IsNullOrEmpty(item.Tag)) parts.Add($"tagged {item.Tag}");

        // Said in words, because the other parts sit in bands a reader may not reach.
        if (partCount > 1)
        {
            parts.Add(segment
                ? $"one of {partCount} parts, handed over between repositories"
                : $"one of {partCount} repository parts");
        }

        if (item.DependsOn.Count > 0)
        {
            parts.Add(item.DependsOn.Count == 1 ? "waits for 1 thing" : $"waits for {item.DependsOn.Count} things");
        }

        // Said in words as well as drawn as a doubling-back arrow, because the arrow
        // is the one thing on this chart a reader cannot get to with a keyboard.
        if (contradicting.Contains(item.Id)) parts.Add("starts before what it waits for has finished");

        if (item.TaskId is not null) parts.Add("linked to a backlog entry");

        // A count rather than the references themselves: the detail line is a summary,
        // and a chapter path is too long to belong on it.
        if (item.Knowledge.Count > 0)
        {
            parts.Add(item.Knowledge.Count == 1
                ? "references 1 knowledge chapter"
                : $"references {item.Knowledge.Count} knowledge chapters");
        }

        return string.Join(" · ", parts);
    }

    private static RoadmapMilestone Marker(
        RoadmapMilestoneDto milestone,
        string rowId,
        HashSet<Guid> contradicting) =>
        new(
            milestone.Id.ToString(),
            rowId,
            milestone.Title,
            milestone.On,
            Marker(milestone.Kind),
            Detail(milestone, contradicting),
            milestone.IsPlanWide);

    private static string Detail(RoadmapMilestoneDto milestone, HashSet<Guid> contradicting)
    {
        var parts = new List<string> { Word(milestone.Kind) };

        // Said in words, because the line itself is drawn in an aria-hidden layer: a
        // reader who cannot see it still needs to know this date is read against
        // everything rather than against one band.
        if (milestone.IsPlanWide) parts.Add("read against the whole plan");

        if (contradicting.Contains(milestone.Id)) parts.Add("falls before what it waits for has finished");

        return string.Join(" · ", parts);
    }

    /// <summary>Four kinds over three glyphs, with the kind always written into the
    /// accessible name. A shape is a hint here, not the fact.</summary>
    private static RoadmapMarker Marker(MilestoneKind kind) => kind switch
    {
        MilestoneKind.Release => RoadmapMarker.Star,
        MilestoneKind.Freeze => RoadmapMarker.Square,
        _ => RoadmapMarker.Diamond
    };

    /// <summary>
    /// Arrows run from the thing that has to land first to the thing waiting, which
    /// is the direction the library draws. An edge whose end is not on screen is left
    /// out rather than pointing at nothing.
    /// <para>
    /// Every drawn part of the waiting node gets an arrow: from the part of what it
    /// waits for in the same band when there is one, else from that node's first
    /// part — so work split across repositories reads as waiting within each.
    /// </para>
    /// </summary>
    private static List<RoadmapLink> Links(
        RoadmapPlanDto plan,
        Dictionary<Guid, List<(string Id, string GroupId, DateOnly Start, DateOnly End)>> placed,
        HashSet<Guid> segmented) =>
    [
        .. from node in plan.Items
               .Select(item => (item.Id, item.DependsOn))
               .Concat(plan.Milestones.Select(milestone => (milestone.Id, milestone.DependsOn)))
           where placed.ContainsKey(node.Id)
           from dependsOnId in node.DependsOn
           where placed.ContainsKey(dependsOnId)
           from link in Between(placed[dependsOnId], placed[node.Id], segmented.Contains(dependsOnId) || segmented.Contains(node.Id))
           select link
    ];

    /// <summary>
    /// The arrows for one dependency. Between parts that run side by side, one per
    /// band, as <see cref="Links"/> describes. Once either end is a sequence of segments that would be an arrow
    /// into the middle of a hand-over, so it is one arrow instead: from where the work
    /// waited for finishes last to where the waiting work starts first.
    /// </summary>
    private static IEnumerable<RoadmapLink> Between(
        List<(string Id, string GroupId, DateOnly Start, DateOnly End)> before,
        List<(string Id, string GroupId, DateOnly Start, DateOnly End)> waiting,
        bool sequenced)
    {
        if (sequenced)
        {
            var last = before.OrderByDescending(part => part.End).First();
            var first = waiting.OrderBy(part => part.Start).First();
            yield return new RoadmapLink(last.Id, first.Id);
            yield break;
        }

        foreach (var part in waiting)
        {
            var source = before.FirstOrDefault(candidate => candidate.GroupId == part.GroupId).Id ?? before[0].Id;
            yield return new RoadmapLink(source, part.Id);
        }
    }

    private static string TitleFor(string alias, List<PlannedRepository> configured) =>
        configured.FirstOrDefault(repository =>
            string.Equals(repository.Alias, alias, StringComparison.OrdinalIgnoreCase))?.Title ?? alias;

    private static string Word(PlanningPriority priority) => priority switch
    {
        PlanningPriority.Low => "Low",
        PlanningPriority.Medium => "Medium",
        PlanningPriority.High => "High",
        _ => "Critical"
    };

    private static string Word(MilestoneKind kind) => kind switch
    {
        MilestoneKind.Release => "Release",
        MilestoneKind.Freeze => "Freeze",
        MilestoneKind.Review => "Review",
        _ => "Commitment"
    };
}
