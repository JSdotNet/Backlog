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
/// <c>.devbook/design/color-scheme.md#band-identity-tokens</c>.
/// </para></param>
public sealed record PlannedRepository(string Alias, string Title, int? Colour = null);

/// <summary>
/// What a plan's work in flight is forecast against: the day it is, and the pace each
/// repository's work goes at.
/// </summary>
/// <param name="Today">The day the forecast starts from — the first day open work
/// can be drawn on.</param>
/// <param name="Paces">The pace in use, globally and per repository, that the work
/// left in an item is divided by — the same pace its window was placed at.</param>
public sealed record RoadmapForecast(DateOnly Today, PacesInUseDto Paces);

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
    /// <summary>
    /// The band for work filed under no configured repository — a project not started
    /// yet, or one whose repository is no longer configured.
    /// <para>
    /// Not an error state and not a bin: it is the place to plan what has no repository
    /// yet, so it is drawn on every chart, last and colourless, with an empty lane to
    /// place new work in when nothing is filed there.
    /// </para>
    /// </summary>
    public const string NoRepositoryGroupId = "no-repository";

    public const string NoRepositoryGroupTitle = "No repository";

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
    /// It takes no colour: a hue here means "which repository", and this band is not
    /// one.
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
    /// <param name="forecast">Today and the paces in use, to lay the plan out the way the
    /// keep-up projection does (<see cref="Layouts"/>): work sized by its effort part by
    /// part from today, work in flight from the work itself. Left unset, only finished
    /// items are drawn from their work and everything else where it was planned.</param>
    public static RoadmapTimelineModel From(
        RoadmapPlanDto? plan,
        IReadOnlyList<PlannedRepository>? repositories,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto>? rollups = null,
        RoadmapForecast? forecast = null)
    {
        if (plan is null || plan.IsEmpty) return RoadmapTimelineModel.Empty;

        var configured = Configured(repositories);
        var contradicting = plan.Contradictions.Select(contradiction => contradiction.NodeId).ToHashSet();

        // Each item is read under the configured aliases its repositories resolve to
        // first, so the band it is drawn in and the pace it is placed at are asked of
        // the same repositories — a full name finds its repository's pace, and a name
        // matching none files the work under no repository, at the default pace.
        List<RoadmapItemDto> filed =
        [
            .. plan.Items.Select(item => item with { RepositoryAliases = FiledUnder(item.RepositoryAliases, configured) })
        ];

        var layouts = Layouts(filed, plan.Milestones, configured, rollups, forecast);

        // Ordered before anything is grouped, so lanes appear in the order the work
        // actually starts rather than in whatever order the file happened to list
        // it. Two runs over the same plan must draw the same picture.
        var items = filed
            .Select(item => (Item: item, Layout: layouts[item.Id]))
            .OrderBy(entry => entry.Layout.Start)
            .ThenBy(entry => entry.Item.Title, StringComparer.CurrentCulture)
            .SelectMany(entry => PartsOf(entry.Item, entry.Layout))
            // Again by start once split, so a part late in its item's window is
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
            .Select(part => Bar(
                part,
                stacked.RowOf[part.BarId],
                contradicting,
                configured,
                HasNoTask(part.Item, rollups)))
            .Where(bar => drawn.Contains(bar.RowId))
            .ToList();

        var markers = milestones
            .Select(milestone => Marker(milestone, MilestoneRowId, contradicting, configured))
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
    /// Every item's parts, as the plan is laid out on <see cref="RoadmapForecast.Today"/>
    /// (ADR 0013, rulings 4 and 5 as amended on 2026-10-07).
    /// <para>
    /// With a forecast, through the keep-up projection (<see cref="RoadmapProjection.Lay"/>),
    /// the one rule the keep-up writer stores by: an item the import sized by its effort is
    /// laid out part by part from today — or from the day after what it waits on ends — each
    /// part at its own repository's pace; work in flight is drawn from the work; a finished
    /// item where it ran; a window a due date or a person placed over that window. So a pace
    /// change, which stores nothing, redraws the bars the way the next opening will store
    /// them.
    /// </para>
    /// <para>
    /// Without one, nothing is placed from a day: a finished item is drawn where it ran and
    /// every other part over its item's stored window (<see cref="RoadmapItemParts.Of"/>).
    /// </para>
    /// <para>
    /// The bands are the view's own: a stored repository name lands in the configured
    /// repository it names by alias or by full name, so a task filed by <c>owner/name</c> is
    /// drawn and paced in the band its repository has.
    /// </para>
    /// </summary>
    private static Dictionary<Guid, RoadmapItemLayout> Layouts(
        IReadOnlyList<RoadmapItemDto> items,
        IReadOnlyList<RoadmapMilestoneDto> milestones,
        List<PlannedRepository> configured,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto>? rollups,
        RoadmapForecast? forecast)
    {
        string? BandOf(string repository) => BandOfRepository(repository, configured);

        var gathered = rollups ?? new Dictionary<Guid, RoadmapItemRollupDto>();

        if (forecast is not null)
        {
            var laid = RoadmapProjection.Lay(items, gathered, forecast.Paces, forecast.Today, milestones, BandOf);
            return new Dictionary<Guid, RoadmapItemLayout>(laid.Layouts);
        }

        var layouts = new Dictionary<Guid, RoadmapItemLayout>();
        foreach (var item in items)
        {
            layouts.TryAdd(item.Id, RoadmapItemParts.Of(item, gathered.GetValueOrDefault(item.Id), NoPace, today: null, BandOf));
        }

        return layouts;
    }

    /// <summary>The paces handed to a layout placed from no day, which never divides by
    /// one.</summary>
    private static readonly PacesInUseDto NoPace = new(0m, new Dictionary<string, decimal>());

    /// <summary>
    /// One item, as the bars it is drawn as: one per part its layout holds — one per band
    /// its repositories land in, or one in the no-repository band when they land in none,
    /// and one per phase when its work hands over between repositories.
    /// <para>
    /// A repository lands in a band when it names a configured repository, by alias or
    /// by full name — an imported plan may carry either. Work naming no configured
    /// repository is drawn under <see cref="NoRepositoryGroupTitle"/>. Nothing is
    /// discarded — the stored item keeps every name it carries, so configuring that
    /// repository again puts the work back in its band.
    /// </para>
    /// <para>
    /// A plan filed in two repositories is two pieces of work to the people doing it —
    /// each repository holds its own share of the tasks — so each band draws only its
    /// part, with a progress fill read from the tasks filed there and the dates its own
    /// pace gives it. It is still one stored item: moving any part moves all of them.
    /// </para>
    /// <para>
    /// The bar ids keep their grammar: an item drawn as one bar keeps its bare id, one
    /// repository's part is <c>item@band</c>, and one phase of work that hands over is
    /// <c>item@band#phase</c>. A hand-over draws an arrow from each part to the parts in
    /// other repositories it waits on; a wait inside one repository is that part's own
    /// business, drawn in its steps.
    /// </para>
    /// </summary>
    private static IEnumerable<ItemPart> PartsOf(RoadmapItemDto item, RoadmapItemLayout layout)
    {
        var parts = layout.Parts;
        var finished = layout.Placement is PartsPlacement.WhereItRan;
        var inFlight = layout.Placement is PartsPlacement.FromWork;

        string GroupOf(RoadmapItemPart part) => part.Band ?? NoRepositoryGroupId;

        string BarIdOf(RoadmapItemPart part) =>
            parts.Count == 1
                ? item.Id.ToString()
                : layout.HandsOver && !part.Empty
                    ? $"{item.Id}{PartSeparator}{GroupOf(part)}{SegmentSeparator}{part.Phase + 1}"
                    : $"{item.Id}{PartSeparator}{GroupOf(part)}";

        foreach (var part in parts)
        {
            var waitsFor = part.WaitsOn
                .Select(index => parts[index])
                .Where(before => !string.Equals(GroupOf(before), GroupOf(part), StringComparison.OrdinalIgnoreCase))
                .Select(BarIdOf)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            yield return new ItemPart(
                item,
                BarIdOf(part),
                GroupOf(part),
                part.Aliases,
                Steps(part.Links),
                parts.Count,
                part.Start,
                part.End,
                layout.HandsOver,
                waitsFor,
                finished
                    ? new DrawnFrom(Finished: true)
                    : inFlight
                        ? new DrawnFrom(Finished: false, Ran: part.Ran, Pinned: Pinned(item, part), part.Forecast, part.Pace, Begun: part.Begun, Empty: part.Empty)
                        : null);
        }
    }

    /// <summary>Whether a person's pinned end is what draws this part's end: the item's end
    /// is pinned and the pin moved this part off its own forecast — or, for a part whose
    /// work has not begun, gave it one to keep (<see cref="RoadmapItemParts"/>).</summary>
    private static bool Pinned(RoadmapItemDto item, RoadmapItemPart part) =>
        item.EndPinned && part.Forecast is { } forecast && (!part.Begun || forecast != part.End);

    /// <summary>The item's window after one of its bars was moved or resized to
    /// <paramref name="change"/>: each edge of the window the item is drawn over — the
    /// earliest start and the latest end among its bars — moves as far as that edge of the
    /// bar did. For a bar that is the whole item that is the change itself; for one part it
    /// moves the whole item with it. Taken from what is drawn rather than what is stored,
    /// because a window laid out again from today is drawn before it is stored, and a drag
    /// measured from the stored one would change the item's length.</summary>
    public static (DateOnly Start, DateOnly End) ItemWindowFor(IEnumerable<RoadmapBar> bars, RoadmapBar bar, RoadmapChange change)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(change);

        var node = NodeIdOf(bar.Id);
        var drawn = bars.Where(candidate => NodeIdOf(candidate.Id) == node).Append(bar).ToList();

        return
        (
            drawn.Min(candidate => candidate.Start).AddDays(change.Start.DayNumber - bar.Start.DayNumber),
            drawn.Max(candidate => candidate.End).AddDays(change.End.DayNumber - bar.End.DayNumber)
        );
    }

    /// <summary>
    /// The end to pin for started work after the end of one of its bars was dragged to
    /// <paramref name="change"/>: the item's drawn end — the latest end of any of its bars
    /// — moved as far as that bar's end did. For the bar that reaches the item's end that
    /// is the dropped date itself. For an earlier part it is not where that part lands: the
    /// pin draws the part that ends latest to it, and every other part keeps its forecast
    /// (Q2 of the 2026-10-07 amendment).
    /// </summary>
    public static DateOnly PinnedEndFor(IEnumerable<RoadmapBar> bars, RoadmapBar bar, RoadmapChange change)
    {
        var node = NodeIdOf(bar.Id);
        var drawnEnd = bars
            .Where(candidate => NodeIdOf(candidate.Id) == node)
            .Select(candidate => candidate.End)
            .Append(bar.End)
            .Max();

        return drawnEnd.AddDays(change.End.DayNumber - bar.End.DayNumber);
    }

    /// <summary>What separates a part's band from its place in the sequence, in the id
    /// of one segment of an item whose tasks hand over between repositories:
    /// <c>&lt;item id&gt;@&lt;band&gt;#&lt;phase&gt;</c>.</summary>
    public const char SegmentSeparator = '#';

    /// <summary>The band a stored repository name lands in — an item's or a task's: the
    /// configured repository it names by alias or by full name, else none.</summary>
    private static string? BandOfRepository(string repositoryId, List<PlannedRepository> configured) =>
        Matching(repositoryId, configured)?.Alias;

    /// <summary>The configured aliases a node's stored repository names resolve to, in
    /// order and once each; a name matching no configured repository is left out.</summary>
    private static List<string> FiledUnder(IReadOnlyList<string> repositoryIds, List<PlannedRepository> configured) =>
        [.. repositoryIds
            .Select(id => BandOfRepository(id, configured))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static PlannedRepository? Matching(string repositoryId, List<PlannedRepository> configured) =>
        configured.FirstOrDefault(repository =>
            string.Equals(repository.Alias, repositoryId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(repository.Title, repositoryId, StringComparison.OrdinalIgnoreCase));

    /// <summary>One bar's worth of an item: the whole of it, one repository's part, or one
    /// phase of the sequence its tasks hand over in, with the dates its layout gave it and
    /// the parts in other repositories it waits on.</summary>
    /// <param name="IsSegment">The item's work hands over between repositories, so this is
    /// one phase of it.</param>
    /// <param name="DrawnFrom">How the work drew it, or <c>null</c> for a part drawn where it
    /// was planned.</param>
    private sealed record ItemPart(
        RoadmapItemDto Item,
        string BarId,
        string GroupId,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<RoadmapStep> Steps,
        int PartCount,
        DateOnly Start,
        DateOnly End,
        bool IsSegment,
        IReadOnlyList<string> WaitsForParts,
        DrawnFrom? DrawnFrom);

    private static List<PlannedRepository> Configured(IReadOnlyList<PlannedRepository>? repositories) =>
        [.. (repositories ?? [])
            .Where(repository => !string.IsNullOrWhiteSpace(repository.Alias))
            .GroupBy(repository => repository.Alias, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];

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

    /// <summary>The lane work sits in when it names none. Its rows carry no title of
    /// their own: they are the repository's lane, and the band already says which.</summary>
    private const string DefaultLane = "Planned";

    private static string Lane(string? lane) => string.IsNullOrWhiteSpace(lane) ? DefaultLane : lane.Trim();

    /// <summary>
    /// How many rows a band carrying a pace keeps for its name and the pace under it,
    /// before its first named lane. The pace starts under the name, about 2.1rem down,
    /// and the rows are the chart's 2.75rem. Up to three measured stretches, each with
    /// its figure, wrap onto at most two lines within the sidebar's 11rem — about
    /// 2.7rem; with none measured, the one line saying the reader's own pace is used
    /// is about 1rem. Either way two rows. The typed pace itself is in the heading.
    /// </summary>
    public const int PaceRows = 2;

    private static List<RoadmapGroup> BuildGroups(
        List<ItemPart> items,
        Dictionary<(string GroupId, string Lane), int> stackedRows,
        bool hasMilestones,
        List<PlannedRepository> configured)
    {
        // Configured order, so the bands read the way Settings lists them, then the
        // no-repository band last — it holds what is not any repository's yet.
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
            .Append((Id: NoRepositoryGroupId, Title: NoRepositoryGroupTitle, Colour: (int?)null));

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

            if (lanes.Count == 0)
            {
                if (id != NoRepositoryGroupId) continue;

                // The no-repository band stands even when empty: its one lane is where
                // the next project is planned before it has a repository.
                lanes.Add(DefaultLane);
                stackedRows[(id, DefaultLane)] = 1;
            }

            // Every row of a stacked lane carries the lane's name, not a blank: the name is
            // how a bar on it describes where it sits to anyone who cannot see the chart.
            // The default lane is the exception — it is the repository's own lane, so its
            // rows are titled by the band alone rather than repeating "Planned" under it.
            List<RoadmapRow> rows =
            [
                .. lanes.SelectMany(lane => Enumerable.Range(0, stackedRows[(id, lane)])
                    .Select(stack => new RoadmapRow(LaneRowId(id, lane, stack), lane == DefaultLane ? string.Empty : lane)))
            ];

            // The hue the repository wears, as Settings resolved it; the no-repository
            // band has none and stays neutral. Every band here makes room under its name
            // for the pace its plans are placed at — its repository's own, or, under no
            // repository, the default pace.
            // The no-repository band is told apart by being last and colourless, so it
            // shows no name; its title still names it to a screen reader.
            bands.Add(new RoadmapGroup(id, title, rows, BandColour(hue), PaceRows, Unnamed: id == NoRepositoryGroupId));
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
    /// <c>.devbook/design/color-scheme.md#band-identity-tokens</c>.
    /// </para>
    /// <para>
    /// It says which repository and nothing else — no status, no severity, no priority
    /// — and it is never the only thing saying it: the band is labelled with its alias
    /// down its own side and every bar names its band in its accessible name. Null is
    /// a neutral band, which is what the milestone and no-repository bands get: a hue
    /// here means "which repository", and neither of those is one.
    /// </para>
    /// </summary>
    private static string? BandColour(int? hue) =>
        hue is null ? null : $"var(--color-band-{hue})";

    /// <summary>
    /// A group is handed no colour, deliberately.
    /// <para>
    /// `.devbook/design/color-scheme.md` allows the product exactly one saturated hue and
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
        List<PlannedRepository> configured,
        bool noTask) =>
        new(
            part.BarId,
            rowId,
            part.Item.Title,
            part.Start,
            part.End,
            Shade(part.Item.Priority),
            Facets(part.Item, part.Aliases, configured),
            Detail(part.Item, contradicting, part.PartCount, part.IsSegment, part.DrawnFrom, noTask),
            Locked: part.DrawnFrom is not null,
            Steps: part.Steps,
            // Work in flight has a start that is a fact and an end that is a forecast, so
            // the end alone may be dragged to pin it. Every part still open offers it — an
            // earlier part too, whose end moves the item's by as much (PinnedEndFor). A part
            // drawn where its work ran is history, and stays put.
            EndResizable: part.DrawnFrom is { Finished: false, Ran: false },
            Tentative: noTask);

    /// <summary>
    /// Whether nothing in the backlog carries an item out yet: no task linked to it
    /// that still exists, and none gathered by its tag. The whole item's answer, not a
    /// part's — work filed in one repository makes every band's part of the item real.
    /// <para>
    /// Unanswerable without the gathered work, and then it is not claimed: a plan read
    /// with no rollups at all would otherwise draw every bar as untouched intent.
    /// </para>
    /// </summary>
    private static bool HasNoTask(RoadmapItemDto item, IReadOnlyDictionary<Guid, RoadmapItemRollupDto>? rollups) =>
        rollups is not null
        && (rollups.GetValueOrDefault(item.Id)?.BacklogEntries.Count ?? 0) == 0;

    /// <summary>
    /// How a part drawn from its work was drawn, where the layout drew it there
    /// (<see cref="RoadmapItemParts"/>, <see cref="Layouts"/>).
    /// <para>
    /// A finished item is drawn where its work ran: each part from the day its first task
    /// was started to the day its last was ticked off. A finished plan is history, and a
    /// planned window is only what somebody hoped: drawing it where it really ran is what
    /// lets a reader scroll back and see how long the work took.
    /// </para>
    /// <para>
    /// An item in flight — something started or done, something still open — is drawn part
    /// by part: a begun part from the day its work began to a forecast of its open points
    /// at its own repository's pace, a done part where it ran, a part not begun after what
    /// it waits on. A pinned end draws the part that ends latest to the pin and keeps that
    /// part's forecast for its detail.
    /// </para>
    /// <para>
    /// Either way the bar is locked: its dates are read off the work, so dragging them
    /// would change nothing the next draw keeps. A part nobody has started that the import
    /// sized by its effort is not drawn from work: it is not locked, and moving it places
    /// the whole item by hand.
    /// </para>
    /// </summary>
    /// <param name="Finished">Every task of the item is done.</param>
    /// <param name="Ran">This part's tasks are all done, in an item still in flight.</param>
    /// <param name="Pinned">A person's pinned end draws this part's end.</param>
    /// <param name="Forecast">Where this part's open work is forecast to be done.</param>
    /// <param name="Pace">The pace this part's work goes at.</param>
    /// <param name="Begun">Some of this part's own tasks are in progress or done. Said per
    /// part, not per item: in an item in flight, a part nobody has started is placed after
    /// what it waits on, and is not drawn from work that began in another part.</param>
    /// <param name="Empty">A repository the item names that holds none of its tasks, drawn
    /// over the window the other parts make.</param>
    private sealed record DrawnFrom(
        bool Finished,
        bool Ran = false,
        bool Pinned = false,
        DateOnly? Forecast = null,
        decimal? Pace = null,
        bool Begun = false,
        bool Empty = false);

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

    private static string Detail(
        RoadmapItemDto item,
        HashSet<Guid> contradicting,
        int partCount,
        bool segment = false,
        DrawnFrom? drawnFrom = null,
        bool noTask = false)
    {
        var parts = new List<string> { $"{Word(item.Priority)} priority" };

        // Said, because the outline a task-less bar is drawn with is not heard.
        if (noTask) parts.Add("no task yet");

        // Said, because a locked bar otherwise only says it cannot be moved, not why.
        if (drawnFrom is not null)
        {
            // A part of work in flight whose own tasks are all done is history too. Whether
            // the work began is the part's own answer: another part's begun work does not
            // make this one in progress (features.md#forecasting-work-in-flight).
            parts.Add(drawnFrom switch
            {
                { Finished: true } or { Ran: true } => "finished, drawn where the work ran",
                { Begun: true, Pinned: true } => "in progress, drawn from when the work began to the end you pinned",
                { Begun: true } => "in progress, drawn from when the work began to when what is left should be done",
                { Empty: true } => "no task in this repository yet, drawn over the window the other parts make",
                { Pinned: true } => "not started yet, placed after what it waits on to the end you pinned",
                _ => "not started yet, placed after what it waits on"
            });

            if (drawnFrom is { Pinned: true, Forecast: { } forecastEnd, Pace: { } pace })
            {
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Forecast: {forecastEnd:d MMM yyyy} at {Math.Round(pace, MidpointRounding.AwayFromZero):0} pt/wk"));
            }
        }

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

        // A count rather than the references themselves: the detail is a summary,
        // and a chapter path is too long to belong on it.
        if (item.Knowledge.Count > 0)
        {
            parts.Add(item.Knowledge.Count == 1
                ? "references 1 knowledge chapter"
                : $"references {item.Knowledge.Count} knowledge chapters");
        }

        // One fact to a line: the timeline lists them in the tooltip and reads them
        // out as one sentence.
        return string.Join('\n', parts);
    }

    private static RoadmapMilestone Marker(
        RoadmapMilestoneDto milestone,
        string rowId,
        HashSet<Guid> contradicting,
        List<PlannedRepository> configured) =>
        new(
            milestone.Id.ToString(),
            rowId,
            milestone.Title,
            milestone.On,
            Marker(milestone.Kind),
            Detail(milestone, contradicting),
            milestone.IsPlanWide,
            // The marker stays in the dates band; the bands of the repositories it is
            // filed under are ruled at its date, so their work reads against it there.
            FiledUnder(milestone.RepositoryAliases, configured));

    private static string Detail(RoadmapMilestoneDto milestone, HashSet<Guid> contradicting)
    {
        var parts = new List<string> { Word(milestone.Kind) };

        // Said in words, because the line itself is drawn in an aria-hidden layer: a
        // reader who cannot see it still needs to know this date is read against
        // everything rather than against one band.
        if (milestone.IsPlanWide) parts.Add("read against the whole plan");

        if (contradicting.Contains(milestone.Id)) parts.Add("falls before what it waits for has finished");

        return string.Join('\n', parts);
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
        Matching(alias, configured)?.Title ?? alias;

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
