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
    /// per element in a sequential loop. It is a far looser bound in practice
    /// than the task cap is, because a record is ten small fields rather than a
    /// whole task document — the count is what has to be bounded here, not the
    /// weight.
    /// </para>
    /// </summary>
    internal const int MaximumPushSessions = 500;

    /// <summary>
    /// The most a session push body may weigh: 1 MB.
    /// <para>
    /// An eighth of the task limit, and deliberately not the same number. Every
    /// field of a session record is bounded below, so
    /// <see cref="MaximumPushSessions"/> records cannot honestly exceed about
    /// half a megabyte; a body larger than this is not a batch of session records
    /// however it is framed. The count cap bounds what reaches the store, and
    /// this bounds what reaches the parser — which the count cap cannot, because
    /// the count is not known until the body has been read.
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
