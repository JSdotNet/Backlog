using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.ImportPlanItems;

/// <summary>
/// Lays the plan-level entries of an imported document out on the roadmap: one
/// Roadmap Item per entry, found again by its tag (ADR 0013, rulings 2, 4 and 5).
/// <para>
/// A person pressing Import is a person changing the plan, so this is a command like
/// every other edit — not a reaction to anything Tasks published. The caller has
/// already parsed the document and already written its tasks; it hands over the
/// entries and, per tag, the effort those tasks registered, and nothing of the
/// parser's crosses.
/// </para>
/// </summary>
/// <param name="Entries">The document's plan-level entries, in document order.</param>
/// <param name="GatheredEffort">Per plan tag, what the tasks under it registered. A
/// tag absent from this gathered nothing.</param>
public sealed record ImportPlanItemsCommand(
    IReadOnlyList<PlanImportEntryDto> Entries,
    IReadOnlyList<PlanTagEffortDto>? GatheredEffort = null);

/// <summary>
/// Upserts by tag, wires dependencies in two passes, and places every window that is
/// still the importer's.
/// <para>
/// All or nothing. The plan is loaded whole, edited in memory, and saved whole only
/// when every edit was allowed; a refusal returns before the save, so a document with
/// one circular <c>after:</c> leaves the stored plan exactly as it was rather than
/// half imported.
/// </para>
/// <para>
/// Nothing is ever deleted. A plan the document stopped describing stays planned:
/// the roadmap is one source of items among several, and taking work off it is a
/// person's own gesture.
/// </para>
/// </summary>
public sealed class ImportPlanItemsCommandHandler(
    IRoadmapPlanRepository plans,
    IPlanningVelocity velocity,
    TimeProvider clock) : ICommandHandler<ImportPlanItemsCommand, Result<PlanImportResultDto>>
{
    public async Task<Result<PlanImportResultDto>> Handle(
        ImportPlanItemsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        var skipped = new List<string>();
        var ambiguous = new List<AmbiguousPlanTagDto>();
        var unresolved = new List<UnresolvedPlanDependencyDto>();
        var scheduled = new List<RoadmapItemScheduledDto>();

        // Pass one: match every entry to its item, or create it, and learn every local
        // id before any after: is read — an entry may wait on one written below it.
        var touched = new List<Touched>();
        var byLocalId = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var entry in command.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Tag))
            {
                skipped.Add(entry.Title);
                continue;
            }

            var tag = PlanningTag.Of(entry.Tag);
            var matched = Match(plan, entry, tag, ambiguous);
            if (matched.IsFailure) return Result.Failure<PlanImportResultDto>(matched.Error);

            // Two entries of one document sharing a tag speak for one item, and the
            // later one wins — but an item this import created stays created.
            var current = matched.Value;
            var earlier = touched.Find(other => other.Item.Id == current.Item.Id);
            if (earlier is not null)
            {
                touched.Remove(earlier);
                current = current with { Previous = earlier.Previous };
            }

            touched.Add(current);

            byLocalId.TryAdd(LocalIdOf(entry, tag), current.Item.Id);
        }

        // Pass two: the document's dependencies replace each item's set. Cleared
        // first, all of them, so a re-import that reverses an edge is not mistaken
        // for a cycle half-way through.
        foreach (var current in touched) plan.ClearDependencies(current.Item.Id);

        foreach (var current in touched)
        {
            foreach (var after in current.Entry.After ?? [])
            {
                var dependsOn = Resolve(plan, byLocalId, after);
                if (dependsOn is null)
                {
                    unresolved.Add(new UnresolvedPlanDependencyDto(current.Item.Tag.Value, after));
                    continue;
                }

                // The aggregate's own invariant decides. A refusal ends the import
                // before anything is saved: the whole batch, not the one edge.
                var added = plan.AddDependency(current.Item.Id, dependsOn.Value);
                if (added.IsFailure) return Result.Failure<PlanImportResultDto>(added.Error);
            }
        }

        // Placement, predecessors first, so an item starts after what it waits on as
        // that now stands.
        var effort = EffortByTag(command.GatheredEffort);

        foreach (var current in InDependencyOrder(touched))
        {
            var item = current.Item;
            if (item.PlacedByImport is null) continue; // moved by hand: kept, dates and all

            var closes = plan.Nodes().ToDictionary(node => node.Id, node => node.Closes);
            var start = ImportedPlanPlacement.StartAfter(item.Dependencies.All.Select(id => closes[id]), today);
            var (window, placement) = ImportedPlanPlacement.Place(
                start,
                current.Entry.Due,
                effort.GetValueOrDefault(item.Tag.Value),
                velocity.StoryPointsPerDay);

            var placed = plan.PlaceByImport(item.Id, window, placement);
            if (placed.IsFailure) return Result.Failure<PlanImportResultDto>(placed.Error);

            if (current.Previous is null || current.Previous != item.Window)
            {
                scheduled.Add(item.Scheduled(current.Previous));
            }
        }

        await plans.SaveAsync(plan, cancellationToken);

        return Result.Success(new PlanImportResultDto(
            [.. touched.Where(current => current.Previous is null).Select(current => current.Item.ToDto())],
            [.. touched.Where(current => current.Previous is not null).Select(current => current.Item.ToDto())],
            skipped,
            ambiguous,
            unresolved,
            scheduled));
    }

    /// <summary>
    /// The item an entry speaks for: the first by creation order carrying its tag, or a
    /// new one. When several carry it the first is updated and the rest reported, so a
    /// person sees the ambiguity rather than finding the wrong item quietly revised.
    /// </summary>
    private static Result<Touched> Match(
        RoadmapPlan plan,
        PlanImportEntryDto entry,
        PlanningTag tag,
        List<AmbiguousPlanTagDto> ambiguous)
    {
        var scope = RepositoryScope.Of(entry.RepositoryAliases);
        var carrying = plan.ItemsTagged(tag);

        if (carrying.Count == 0)
        {
            // A provisional window: the placement pass sets the real one once every
            // predecessor is known.
            var added = plan.AddImportedItem(
                entry.Title,
                tag,
                PlannedWindow.Of(DateOnly.MinValue, DateOnly.MinValue),
                ImportPlacement.Effort,
                entry.Priority,
                scope,
                entry.Notes);

            return added.IsFailure
                ? Result.Failure<Touched>(added.Error)
                : Result.Success(new Touched(entry, added.Value, Previous: null));
        }

        var first = carrying[0];
        if (carrying.Count > 1)
        {
            ambiguous.Add(new AmbiguousPlanTagDto(tag.Value, first.Id, [.. carrying.Skip(1).Select(item => item.Id)]));
        }

        var previous = first.Window;
        var revised = plan.ReviseFromImport(first.Id, entry.Title, entry.Priority, scope, entry.Notes);

        return revised.IsFailure
            ? Result.Failure<Touched>(revised.Error)
            : Result.Success(new Touched(entry, revised.Value, previous));
    }

    /// <summary>An entry's <c>id:</c>, defaulting to its tag so a roadmap document need
    /// not write both.</summary>
    private static string LocalIdOf(PlanImportEntryDto entry, PlanningTag tag) =>
        string.IsNullOrWhiteSpace(entry.LocalId) ? tag.Value : entry.LocalId.Trim();

    /// <summary>
    /// An <c>after:</c> value, in a fixed order of confidence: a sibling entry's local
    /// id; then the tag of an item already on the plan, when exactly one carries it;
    /// then a node id. Null when it names nothing — it is then dropped and reported
    /// rather than stored, because the plan refuses an edge to an unknown node.
    /// </summary>
    private static Guid? Resolve(RoadmapPlan plan, Dictionary<string, Guid> byLocalId, string after)
    {
        var value = after.Trim();
        if (value.Length == 0) return null;

        if (byLocalId.TryGetValue(value, out var sibling)) return sibling;

        var tagged = plan.ItemsTagged(PlanningTag.Of(value));
        if (tagged.Count == 1) return tagged[0].Id;

        return Guid.TryParse(value, out var id) && plan.Nodes().Any(node => node.Id == id) ? id : null;
    }

    private static Dictionary<string, int> EffortByTag(IReadOnlyList<PlanTagEffortDto>? gathered)
    {
        var effort = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tag in gathered ?? [])
        {
            if (string.IsNullOrWhiteSpace(tag.Tag)) continue;
            effort.TryAdd(PlanningTag.Of(tag.Tag).Value, Math.Max(0, tag.TotalEffort));
        }

        return effort;
    }

    /// <summary>
    /// The touched items with every predecessor among them first, ties kept in
    /// document order. Acyclic by the time this runs — the dependency pass refused
    /// any cycle — so every item is reached.
    /// </summary>
    private static IEnumerable<Touched> InDependencyOrder(List<Touched> touched)
    {
        var ids = touched.Select(current => current.Item.Id).ToHashSet();
        var placed = new HashSet<Guid>();
        var waiting = new List<Touched>(touched);

        while (waiting.Count > 0)
        {
            var next = waiting.First(current =>
                current.Item.Dependencies.All.All(id => !ids.Contains(id) || placed.Contains(id)));

            waiting.Remove(next);
            placed.Add(next.Item.Id);
            yield return next;
        }
    }

    /// <param name="Previous">The window the item had before this import; null when
    /// the import created it.</param>
    private sealed record Touched(PlanImportEntryDto Entry, RoadmapItem Item, PlannedWindow? Previous);
}
