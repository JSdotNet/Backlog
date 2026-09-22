using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Microsoft.AspNetCore.Http.Metadata;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// How much one caller may send in one request.
/// <para>
/// The numbers live together because they answer one question. Registration is
/// anonymous and mints a fresh owner with no gate (.arc42/adr/0005 §Identity), so
/// every write surface here is reachable by anybody who can reach the service,
/// and the store behind them is durable and billed per request. Bounding what a
/// single request can carry is the only thing standing between that and an
/// unbounded write; rate limiting the anonymous registration itself is a separate
/// change and a pre-existing surface.
/// </para>
/// </summary>
internal static class SyncRequestLimits
{
    /// <summary>
    /// The most task changes one push may carry.
    /// <para>
    /// The same number the pull clamps a page to, and for the same reason: a
    /// page is a unit of work against the store, and the replica issues one round
    /// trip per element in a sequential loop. The device batches at 200, so this
    /// is not a limit any client of ours meets — it is the bound on the ones that
    /// are not ours.
    /// </para>
    /// </summary>
    internal const int MaximumPushTasks = 500;

    /// <summary>
    /// The most a push body may weigh: 8 MB, which is <see cref="MaximumPushTasks"/>
    /// tasks of around 16 KB each with room to spare, and far below the 30 MB a
    /// caller can otherwise post at a server whose default Kestrel limit is the
    /// only thing in the way.
    /// <para>
    /// The count cap bounds what reaches the store; this bounds what reaches the
    /// parser, which the count cap cannot because the count is not known until
    /// the body has been read.
    /// </para>
    /// </summary>
    internal const long PushBodyBytes = 8L * 1024 * 1024;

    /// <summary>How long a capture's title may be. Generous for a thought typed
    /// on a phone, and nowhere near Cosmos's two-megabyte document ceiling, which
    /// is the failure this keeps off the store.</summary>
    internal const int MaximumCaptureTitle = 4_000;

    /// <summary>How long a capture's source may be. It names where the capture
    /// came from — <c>phone</c>, <c>vscode</c> — so anything longer is not a
    /// source.</summary>
    internal const int MaximumCaptureSource = 100;

    /// <summary>
    /// The most session records one push may carry.
    /// <para>
    /// The same number the pull clamps a page to, and for the same reason: a page
    /// is a unit of work against the store, and the replica issues one round trip
    /// per element in a sequential loop. It was a far looser bound in practice
    /// than the task cap while a record was ten small scalars; now that a record
    /// may carry a thousand intervals, <see cref="MaximumSessionIntervals"/> and
    /// the body limit are what bound the weight, and this still bounds the count.
    /// </para>
    /// </summary>
    internal const int MaximumPushSessions = 500;

    /// <summary>
    /// The most intervals either list on a session record may carry — the wire
    /// contract's own number, <see cref="SessionRecordLimits.IntervalsPerList"/>,
    /// because this is the one bound the pusher has to match exactly rather than
    /// stay under: it truncates to the cap and this refuses above it.
    /// <para>
    /// The arithmetic. An interval is two ISO-8601 timestamps under two property
    /// names, about 96 bytes as JSON; a record's scalars are about 330 bytes. A
    /// record at the cap in both lists is therefore a thousand intervals plus the
    /// scalars, about 96 KB, and it is the only kind of record that comes near
    /// troubling anything. The pusher weighs a batch at 4,000 intervals, about
    /// 450 KB with the records around them — under half of
    /// <see cref="SessionPushBodyBytes"/> — so no batch a client of ours sends
    /// meets that limit. A caller that is not ours can still post five hundred
    /// capped records, about 48 MB, and the body limit is what refuses that before
    /// the parser sees it.
    /// </para>
    /// <para>
    /// Refused rather than trimmed, like every other bound here: a push that
    /// silently kept the first five hundred intervals and answered 200 would leave
    /// the machine believing the rest were stored.
    /// </para>
    /// </summary>
    internal const int MaximumSessionIntervals = SessionRecordLimits.IntervalsPerList;

    /// <summary>
    /// The most a session push body may weigh: 1 MB.
    /// <para>
    /// An eighth of the task limit, and deliberately not the same number. It is
    /// sized for the batches a client of ours sends and not for the worst case
    /// the count cap alone would admit: the pusher splits a batch at two hundred
    /// records or 4,000 intervals, whichever comes first, and the heavier of
    /// those is about 450 KB, so a body larger than this is not a batch our
    /// pusher built however it is framed. The count cap bounds what reaches the
    /// store, this bounds what reaches the parser — which the count cap cannot,
    /// because the count is not known until the body has been read — and
    /// <see cref="MaximumSessionIntervals"/> bounds what one record may cost the
    /// store once it is there.
    /// </para>
    /// </summary>
    internal const long SessionPushBodyBytes = 1L * 1024 * 1024;

    /// <summary>How long a session id may be. Generous for the UUIDs and short
    /// tokens the agents issue, and short enough that it cannot be used to smuggle
    /// content past the whitelist in the one field that has to be free
    /// text.</summary>
    internal const int MaximumSessionId = 200;

    /// <summary>How long an agent kind may be. It names the assistant that ran
    /// the session — <c>claude</c>, <c>copilot</c> — so anything longer is not an
    /// agent kind. It stays a string rather than becoming an enum
    /// (.arc42/adr/0005 §Storage: no domain logic runs against the replica), and
    /// this length is what a string costs instead.</summary>
    internal const int MaximumAgentKind = 50;

    /// <summary>How long a machine name may be. Above every operating system's
    /// own hostname limit, so a real machine name always fits and a two-megabyte
    /// one is refused as what it is.</summary>
    internal const int MaximumMachineName = 255;

    /// <summary>How long a repository alias may be. An alias, never a path
    /// (.arc42/adr/0005 §Session records), so it is a short name rather than
    /// something that grows with a directory tree.</summary>
    internal const int MaximumRepositoryAlias = 200;

    /// <summary>How long a branch name may be. Git imposes no limit of its own
    /// worth relying on, and this is well past anything a person types and well
    /// short of anything that would trouble the store.</summary>
    internal const int MaximumBranch = 255;

    /// <summary>
    /// The most annotation changes one push may carry — the same number as the
    /// other two containers, for the same reason: a page is a unit of work
    /// against the store, and the device batches at 200.
    /// </summary>
    internal const int MaximumPushAnnotations = 500;

    /// <summary>
    /// The most an annotation push body may weigh: 4 MB. Every field of an
    /// annotation is bounded below, so <see cref="MaximumPushAnnotations"/>
    /// remarks cannot honestly reach this; the count cap bounds what reaches
    /// the store, and this bounds what reaches the parser.
    /// </summary>
    internal const long AnnotationPushBodyBytes = 4L * 1024 * 1024;

    /// <summary>How long a chapter path may be. A repository-relative path,
    /// never an absolute one, so this is generous for any knowledge folder and
    /// far short of anything that would trouble the store.</summary>
    internal const int MaximumChapterPath = 1_024;

    /// <summary>How long an annotation's author label may be. A machine name
    /// or a person's name, which is what <see cref="MaximumMachineName"/> also
    /// bounds.</summary>
    internal const int MaximumAnnotationAuthor = 255;

    /// <summary>How long a remark's body may be. Generous for a review note
    /// — several paragraphs — and nowhere near the two-megabyte document
    /// ceiling that is the failure this keeps off the store.</summary>
    internal const int MaximumAnnotationBody = 8_000;
}



/// <summary>
/// The size limit a route carries, as endpoint metadata.
/// <para>
/// <c>EndpointRoutingMiddleware</c> is what acts on it, once the route has
/// matched and before the body is read: it puts the number on the server's
/// <c>IHttpMaxRequestBodySizeFeature</c>, and Kestrel answers 413 for anything
/// larger. A server with no such feature logs that it could not, which is what
/// the test host does — so the endpoint test pins the metadata rather than the
/// enforcement.
/// </para>
/// <para>
/// Its own type rather than <c>Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute</c>:
/// that one is also an <c>IFilterFactory</c>, which reads as though the limit
/// were enforced by an MVC filter this pipeline does not have. This says only
/// what it means.
/// </para>
/// </summary>
internal sealed record RequestBodyLimit(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;
