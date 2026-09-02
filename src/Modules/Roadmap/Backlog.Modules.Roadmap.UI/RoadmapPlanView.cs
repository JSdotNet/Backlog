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
/// between them.
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

    public static RoadmapTimelineModel From(
        RoadmapPlanDto? plan,
        IReadOnlyList<PlannedRepository>? repositories)
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
            .Select(item => (Item: item, GroupId: GroupIdFor(item.RepositoryAliases, configured)))
            .ToList();

        // Milestones are not grouped by repository: they all share one band at the top.
        var milestones = plan.Milestones
            .OrderBy(milestone => milestone.On)
            .ThenBy(milestone => milestone.Title, StringComparer.CurrentCulture)
            .ToList();

        var groups = BuildGroups(items, milestones.Count > 0, configured);
        var drawn = groups.SelectMany(group => group.RowList).Select(row => row.Id).ToHashSet();

        var bars = items
            .Select(entry => Bar(entry.Item, LaneRowId(entry.GroupId, entry.Item.Lane), contradicting, configured))
            .Where(bar => drawn.Contains(bar.RowId))
            .ToList();

        var markers = milestones
            .Select(milestone => Marker(milestone, MilestoneRowId, contradicting))
            .Where(marker => drawn.Contains(marker.RowId))
            .ToList();

        // What actually reached the chart, so an arrow is only drawn when both of its
        // ends are on screen.
        var placed = bars.Select(bar => bar.Id)
            .Concat(markers.Select(marker => marker.Id))
            .Select(NodeIdOf)
            .OfType<Guid>()
            .ToHashSet();

        return new RoadmapTimelineModel(groups, bars, markers, Links(plan, placed));
    }

    /// <summary>The plan's own id behind a bar or a marker the timeline reported
    /// on. The two are the same string; this names the fact so a caller does not
    /// have to know that.</summary>
    public static Guid? NodeIdOf(string? barId) => Guid.TryParse(barId, out var id) ? id : null;

    private static List<PlannedRepository> Configured(IReadOnlyList<PlannedRepository>? repositories) =>
        [.. (repositories ?? [])
            .Where(repository => !string.IsNullOrWhiteSpace(repository.Alias))
            .GroupBy(repository => repository.Alias, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];

    /// <summary>
    /// Which band a node belongs in: the first of its aliases that is actually
    /// configured, or the unfiled band.
    /// <para>
    /// One band, not one per alias. Work scoped to two repositories is one piece of
    /// work with one set of dates, and drawing it twice would mean dragging one copy
    /// while the other sat still — a plan that appears to disagree with itself
    /// because of how it was drawn. Every alias is kept as a facet instead, so the
    /// filter still finds it under either repository.
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

    private static string LaneRowId(string groupId, string? lane) => $"{groupId}::{Lane(lane)}";

    private static string Lane(string? lane) => string.IsNullOrWhiteSpace(lane) ? "Planned" : lane.Trim();

    private static List<RoadmapGroup> BuildGroups(
        List<(RoadmapItemDto Item, string GroupId)> items,
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

            List<RoadmapRow> rows = [.. lanes.Select(lane => new RoadmapRow($"{id}::{lane}", lane))];

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
        RoadmapItemDto item,
        string rowId,
        HashSet<Guid> contradicting,
        List<PlannedRepository> configured) =>
        new(
            item.Id.ToString(),
            rowId,
            item.Title,
            item.Start,
            item.End,
            Shade(item.Priority),
            Facets(item, configured),
            Detail(item, contradicting));

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

    private static List<RoadmapFacet> Facets(RoadmapItemDto item, List<PlannedRepository> configured)
    {
        var facets = new List<RoadmapFacet>();

        // Every alias, not just the one whose band it landed in, so filtering by a
        // repository finds work that only mentions it second.
        facets.AddRange(item.RepositoryAliases.Select(alias => new RoadmapFacet("Repository", TitleFor(alias, configured))));
        facets.Add(new RoadmapFacet("Priority", Word(item.Priority)));
        facets.Add(new RoadmapFacet("Lane", Lane(item.Lane)));

        // The tag the item carries, so the timeline can filter by it — and so work
        // grouped under one tag is found together. Only when there is one to show.
        if (!string.IsNullOrEmpty(item.Tag)) facets.Add(new RoadmapFacet("Tag", item.Tag));

        return facets;
    }

    private static string Detail(RoadmapItemDto item, HashSet<Guid> contradicting)
    {
        var parts = new List<string> { $"{Word(item.Priority)} priority" };

        if (!string.IsNullOrEmpty(item.Tag)) parts.Add($"tagged {item.Tag}");

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

    /// <summary>Arrows run from the thing that has to land first to the thing
    /// waiting, which is the direction the library draws. An edge whose end is not
    /// on screen is left out rather than pointing at nothing.</summary>
    private static List<RoadmapLink> Links(RoadmapPlanDto plan, HashSet<Guid> placed) =>
    [
        .. from node in plan.Items
               .Select(item => (item.Id, item.DependsOn))
               .Concat(plan.Milestones.Select(milestone => (milestone.Id, milestone.DependsOn)))
           where placed.Contains(node.Id)
           from dependsOnId in node.DependsOn
           where placed.Contains(dependsOnId)
           select new RoadmapLink(dependsOnId.ToString(), node.Id.ToString())
    ];

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
