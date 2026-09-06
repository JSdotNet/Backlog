using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Roadmap;

/// <summary>
/// The stored shape of the roadmap plan — the whole plan as one document, held in
/// the <c>document</c> column of the single <c>roadmap_plan</c> row.
/// <para>
/// One document rather than a row per item, unlike the tasks beside it. The plan is
/// one consistency boundary: a dependency edge is only meaningful with every other
/// node in view, so there is no half of it worth writing on its own. Keeping the
/// edges in the same document as the nodes also means a plan can never be read in a
/// state where an arrow points at something that has not loaded yet.
/// </para>
/// <para>
/// The cost is real and worth naming: every edit rewrites the whole document, so two
/// processes writing at once would have one of them lose. The write is a single
/// UPSERT and therefore atomic (see <see cref="SqliteRoadmapPlanRepository"/>), and
/// <see cref="Version"/> exists so a later shape change can be read rather than
/// guessed at.
/// </para>
/// <para>
/// This shape crossed the move from <c>_roadmap/plan.json</c> unchanged, on purpose:
/// folding the plan into <c>backlog.db</c> is a change of medium, and changing the
/// content in the same breath would have made one change impossible to review as
/// two.
/// </para>
/// <para>
/// Dates are written as plain <c>yyyy-MM-dd</c> — no time, no offset. A planned day
/// is a day: writing it as an instant would make a plan drawn in Amsterdam shift
/// when it is read in Auckland.
/// </para>
/// </summary>
internal sealed record RoadmapPlanDocument
{
    /// <summary>
    /// How the document is written and read. It lives on the document rather than on
    /// the repository because it is part of this shape's contract, not of the store
    /// that happens to hold it.
    /// <para>
    /// Written compact. The file version indented it so a person could open the plan
    /// in an editor; nothing opens the column that way, and the JSON payloads in the
    /// <c>tasks</c> table beside it are compact for the same reason.
    /// </para>
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // An item that named no lane, no entry and no notes carries no keys for them.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The only shape written so far. A document with a higher version is
    /// read as far as it can be rather than refused — a plan that opens without
    /// the field somebody's newer build added beats a plan that will not open.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public List<RoadmapItemDocument> Items { get; init; } = [];

    public List<RoadmapMilestoneDocument> Milestones { get; init; } = [];

    /// <summary>Which of the sanctioned band colours each repository has been given,
    /// keyed by alias — <c>{ "backlog": 3 }</c>. A number rather than a colour on
    /// purpose: which hue each one is belongs to the stylesheet, and a plan file that
    /// named a hue would be a plan inventing one. A repository absent from here has not
    /// been chosen for, which is different from having been given the first colour.</summary>
    public Dictionary<string, int> Bands { get; init; } = [];

    internal static RoadmapPlanDocument From(RoadmapPlan plan) => new()
    {
        Version = CurrentVersion,
        Items = [.. plan.Items.Select(RoadmapItemDocument.From)],
        Milestones = [.. plan.Milestones.Select(RoadmapMilestoneDocument.From)],
        Bands = new Dictionary<string, int>(plan.BandColours.Chosen, StringComparer.Ordinal)
    };

    internal RoadmapPlan ToPlan() => RoadmapPlan.Rehydrate(
        Items.Select(item => item.ToItem()).OfType<RoadmapItem>(),
        Milestones.Select(milestone => milestone.ToMilestone()).OfType<Milestone>(),
        BandColours.Of(Bands));
}

internal sealed record RoadmapItemDocument
{
    public string? Id { get; init; }

    public string? Title { get; init; }

    public DateOnly Start { get; init; }

    public DateOnly End { get; init; }

    public string? Priority { get; init; }

    /// <summary>Repository aliases, exactly as configured in Settings. Written even
    /// when the alias no longer matches a configured repository: the plan keeps
    /// what the person wrote, and an unresolved alias is a reading outcome rather
    /// than a reason to delete their scope.</summary>
    public List<string> Repositories { get; init; } = [];

    public string? Lane { get; init; }

    /// <summary>The task this item is linked to.
    /// <para>
    /// The JSON name stays <c>backlogEntryId</c>, which is what it was called when the
    /// Backlog bounded context was renamed to Tasks. The property is ours to rename;
    /// the key is not. Under this document's camelCase policy a plain rename would
    /// serialize as <c>taskId</c>, and every stored plan would silently come back with
    /// its links dropped — the plan would still load, which is what makes it
    /// dangerous.
    /// </para>
    /// <para>
    /// The move into <c>backlog.db</c> was the one moment this key was free: nothing
    /// carried over from the JSON file, so a rename would have cost nothing that day.
    /// It stays anyway. From the first save this build makes, the key is written into
    /// somebody's database, and a shape worth keeping stable across a sync replica is
    /// not worth churning for tidiness.
    /// </para></summary>
    [JsonPropertyName("backlogEntryId")]
    public string? TaskId { get; init; }

    /// <summary>The slug this item is known by wherever tags are used. Additive and
    /// safe to omit: a document written before tags existed simply has no <c>tag</c>,
    /// and the item derives one from its title on load — which is why this did not
    /// bump <see cref="RoadmapPlanDocument.Version"/>.</summary>
    public string? Tag { get; init; }

    /// <summary>The knowledge chapters this item points at, as opaque references.
    /// Omitted when empty — written as <c>null</c> rather than <c>[]</c> so the
    /// serializer's default rule drops it, the same habit <c>PlanWide</c> follows —
    /// so an item that points at no chapter costs no line. Additive for the same
    /// reason <see cref="Tag"/> is: an older file with no <c>knowledge</c> loads as an
    /// empty set, so neither field bumped the version.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? Knowledge { get; init; }

    public List<string> DependsOn { get; init; } = [];

    public string? Notes { get; init; }

    internal static RoadmapItemDocument From(RoadmapItem item) => new()
    {
        Id = item.Id.ToString(),
        Title = item.Title,
        Start = item.Window.Start,
        End = item.Window.End,
        Priority = RoadmapWire.ToWire(item.Priority),
        Repositories = [.. item.Scope.Aliases],
        Lane = item.Lane.IsDefault ? null : item.Lane.Name,
        TaskId = item.TaskId?.ToString(),
        Tag = item.Tag.Value,
        Knowledge = item.KnowledgeRefs.IsEmpty ? null : [.. item.KnowledgeRefs.Refs],
        DependsOn = [.. item.Dependencies.All.Select(id => id.ToString())],
        Notes = item.Notes
    };

    /// <summary>The item, or null when the block does not describe one. A block
    /// without a usable id or title is skipped rather than thrown on: one unreadable
    /// block should cost its own entry and nothing else, rather than the whole plan.
    /// <para>
    /// This outlived the reason it was written for. The document was a file somebody
    /// could hand-edit, so a half-typed block was the expected way to break one; the
    /// tolerance is worth keeping anyway, because a plan that opens missing one item
    /// still beats a plan that will not open.
    /// </para></summary>
    internal RoadmapItem? ToItem()
    {
        if (!Guid.TryParse(Id, out var id) || string.IsNullOrWhiteSpace(Title)) return null;

        return new RoadmapItem(
            id,
            Title.Trim(),
            PlannedWindow.Of(Start, End),
            RoadmapWire.ParsePriority(Priority),
            RepositoryScope.Of(Repositories),
            PlanningLane.Of(Lane),
            Dependencies.Of(RoadmapWire.ParseIds(DependsOn)),
            Guid.TryParse(TaskId, out var entryId) ? entryId : null,
            Notes,
            // No tag means a document from before tags existed; the item
            // derives one from its title when handed null.
            string.IsNullOrWhiteSpace(Tag) ? null : PlanningTag.Of(Tag),
            KnowledgeReferences.Of(Knowledge));
    }
}

internal sealed record RoadmapMilestoneDocument
{
    public string? Id { get; init; }

    public string? Title { get; init; }

    public DateOnly On { get; init; }

    public string? Kind { get; init; }

    public List<string> Repositories { get; init; } = [];

    public string? Lane { get; init; }

    public List<string> DependsOn { get; init; } = [];

    /// <summary>Whether the whole plan is read against this date. Omitted when false,
    /// so an ordinary milestone costs no key in the stored document —
    /// which needs the explicit condition, because the serializer's null rule has
    /// nothing to say about a bool.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PlanWide { get; init; }

    internal static RoadmapMilestoneDocument From(Milestone milestone) => new()
    {
        Id = milestone.Id.ToString(),
        Title = milestone.Title,
        On = milestone.On,
        Kind = RoadmapWire.ToWire(milestone.Kind),
        Repositories = [.. milestone.Scope.Aliases],
        Lane = milestone.Lane.IsDefault ? null : milestone.Lane.Name,
        DependsOn = [.. milestone.Dependencies.All.Select(id => id.ToString())],
        PlanWide = milestone.IsPlanWide
    };

    internal Milestone? ToMilestone()
    {
        if (!Guid.TryParse(Id, out var id) || string.IsNullOrWhiteSpace(Title)) return null;

        return new Milestone(
            id,
            Title.Trim(),
            On,
            RoadmapWire.ParseKind(Kind),
            RepositoryScope.Of(Repositories),
            PlanningLane.Of(Lane),
            Dependencies.Of(RoadmapWire.ParseIds(DependsOn)),
            PlanWide);
    }
}

/// <summary>
/// Maps the Roadmap module's enums to and from the strings written into the stored
/// document, the same way <c>EnumMap</c> does for the tasks in the table beside it:
/// the document says <c>high</c> and <c>release</c> rather than a number, so it stays
/// readable and a reordered enum cannot silently reinterpret a stored plan.
/// </summary>
internal static class RoadmapWire
{
    internal static string ToWire(PlanningPriority value) => value switch
    {
        PlanningPriority.Low => "low",
        PlanningPriority.Medium => "medium",
        PlanningPriority.High => "high",
        PlanningPriority.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    internal static string ToWire(MilestoneKind value) => value switch
    {
        MilestoneKind.Release => "release",
        MilestoneKind.Freeze => "freeze",
        MilestoneKind.Review => "review",
        MilestoneKind.Commitment => "commitment",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    /// <summary>An unreadable or missing priority falls back to medium rather than
    /// failing the load. A document carrying <c>urgent</c> has said something about
    /// one item; refusing to open the whole plan over it would be a poor trade.</summary>
    internal static PlanningPriority ParsePriority(string? value) => Normalize(value) switch
    {
        "low" => PlanningPriority.Low,
        "high" => PlanningPriority.High,
        "critical" => PlanningPriority.Critical,
        _ => PlanningPriority.Medium
    };

    internal static MilestoneKind ParseKind(string? value) => Normalize(value) switch
    {
        "freeze" => MilestoneKind.Freeze,
        "review" => MilestoneKind.Review,
        "commitment" => MilestoneKind.Commitment,
        _ => MilestoneKind.Release
    };

    internal static IEnumerable<Guid> ParseIds(IEnumerable<string>? ids) =>
        (ids ?? []).Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty);

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
