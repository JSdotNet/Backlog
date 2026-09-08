namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// Where the sync surface lives. The service maps these and a client calls
/// them, so they are written down once rather than as two string literals that
/// agree until one of them is edited.
/// <para>
/// Each route is relative to <see cref="Base"/>, which is what
/// <c>MapGroup</c> wants; <see cref="Absolute"/> puts the two back together for
/// a client holding a bare <c>HttpClient</c>.
/// </para>
/// </summary>
public static class SyncRoutes
{
    /// <summary>The group every sync endpoint hangs off.</summary>
    public const string Base = "/api/sync";

    /// <summary>First device in: mints a new owner. Anonymous, because there is
    /// nothing yet to authenticate as.</summary>
    public const string RegisterDevice = "/devices/register";

    /// <summary>A paired device asks for a short code to read out. Bearer.</summary>
    public const string PairingCodes = "/devices/codes";

    /// <summary>Second device in: trades the short code for a registration
    /// credential under the same owner. Anonymous for the same reason
    /// <see cref="RegisterDevice"/> is — the code is the only thing it has.</summary>
    public const string RedeemPairingCode = "/devices/pair";

    /// <summary>Registration credential in, short-lived token out. Anonymous:
    /// the credential in the body is what authenticates the call.</summary>
    public const string DeviceToken = "/devices/token";

    /// <summary>Who the bearer of this token turned out to be. Bearer.</summary>
    public const string DeviceStatus = "/devices/me";

    /// <summary>The captures waiting for this owner. Bearer.</summary>
    public const string Inbox = "/inbox";

    /// <summary>The task replica: POST pushes a batch, GET pulls the owner's
    /// change feed from a cursor. Bearer, and one route for both halves because
    /// they are the two directions of one exchange over one collection.</summary>
    public const string Tasks = "/tasks";

    /// <summary>The session replica: POST appends a batch of this machine's
    /// records, GET pulls the owner's session feed from a cursor. Bearer, and
    /// one route for both halves because they are the two directions of one
    /// exchange over one collection — the same shape as <see cref="Tasks"/>,
    /// over the second container .arc42/adr/0005 §Storage declares.</summary>
    public const string Sessions = "/sessions";

    /// <summary>Route template for acknowledging one capture. Bearer. Use
    /// <see cref="AcknowledgeInboxItemFor"/> to build a concrete URL.</summary>
    public const string AcknowledgeInboxItem = "/inbox/{id:guid}/ack";

    /// <summary>The full path a client posts to.</summary>
    public static string Absolute(string route) => Base + route;

    /// <summary>The full path for acknowledging one capture.</summary>
    public static string AcknowledgeInboxItemFor(Guid id) => $"{Base}/inbox/{id:D}/ack";
}
