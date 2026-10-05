using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How the two roadmap documents are written on the task feed (local ADR 0018):
/// the kind token each carries, the one id each is stored under, and the title the
/// activity log shows. The wire half of the arrangement only — what a document
/// says, and which copy wins, is Roadmap's, behind <see cref="IRoadmapReplication"/>.
/// <para>
/// The kind tokens are literals duplicated where they are read, for the reason
/// <see cref="TaskReplicaMerge"/> gives for <c>capture</c>: the phone's
/// <c>TaskFold</c> skips the same two, and neither side may see the other.
/// </para>
/// </summary>
public static class RoadmapReplicaDocuments
{
    /// <summary>The kind token the plan document carries.</summary>
    public const string PlanType = "roadmap-plan";

    /// <summary>The kind token the pace document carries.</summary>
    public const string PaceType = "planning-pace";

    /// <summary>
    /// The plan document's id — the same literal on every device, chosen once and
    /// <b>never to be changed</b>. The replica keys a document by owner and id, so a
    /// build writing the plan under another id would fork it: two plans on the
    /// replica, each device taking whichever it pulled last. The owner partition is
    /// what keeps two people's plans apart, not the id.
    /// </summary>
    public static readonly Guid PlanId = Guid.Parse("7c1a0b52-3d4e-4f6a-9b8c-0d1e2f3a4b01");

    /// <summary>The pace document's id, on the same terms as <see cref="PlanId"/>:
    /// never to be changed.</summary>
    public static readonly Guid PaceId = Guid.Parse("7c1a0b52-3d4e-4f6a-9b8c-0d1e2f3a4b02");

    /// <summary>Both documents, in the order a push sends them.</summary>
    public static IReadOnlyList<RoadmapReplicaDocument> All { get; } =
        [RoadmapReplicaDocument.Plan, RoadmapReplicaDocument.Pace];

    /// <summary>The document a kind token names, or null for any other kind.
    /// Ordinal, as every other token on the feed is compared.</summary>
    public static RoadmapReplicaDocument? KindOf(string? type) => type switch
    {
        PlanType => RoadmapReplicaDocument.Plan,
        PaceType => RoadmapReplicaDocument.Pace,
        _ => null,
    };

    public static string TypeOf(RoadmapReplicaDocument document) => document switch
    {
        RoadmapReplicaDocument.Plan => PlanType,
        RoadmapReplicaDocument.Pace => PaceType,
        _ => throw new ArgumentOutOfRangeException(nameof(document)),
    };

    public static Guid IdOf(RoadmapReplicaDocument document) => document switch
    {
        RoadmapReplicaDocument.Plan => PlanId,
        RoadmapReplicaDocument.Pace => PaceId,
        _ => throw new ArgumentOutOfRangeException(nameof(document)),
    };

    /// <summary>What the activity log, and any listing of the feed, calls it.</summary>
    public static string TitleOf(RoadmapReplicaDocument document) => document switch
    {
        RoadmapReplicaDocument.Plan => "Roadmap plan",
        RoadmapReplicaDocument.Pace => "Planning pace",
        _ => throw new ArgumentOutOfRangeException(nameof(document)),
    };

    /// <summary>
    /// The document as a task-shaped change (ADR 0018, Decision §1): its constant id,
    /// its stamp, its kind token, the stored text verbatim as <c>ContentMd</c>, the
    /// Tasks defaults for status and priority so no field but the type is unusual,
    /// and nothing else. Never a tombstone — a cleared plan is an empty plan, sent
    /// like any other.
    /// </summary>
    public static TaskChange ToChange(RoadmapReplicaDocument document, string content, DateTimeOffset updatedAt) =>
        WholeDocumentChange.Of(IdOf(document), TitleOf(document), TypeOf(document), content, updatedAt);
}
