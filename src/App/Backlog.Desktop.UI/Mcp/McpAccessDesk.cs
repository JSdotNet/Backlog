using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Mcp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// "Approve an agent once" (local ADR 0052 §5): where an agent's request for access is made,
/// held to its limits, shown to the person, and answered — by the person alone.
/// <para>
/// <b>Two ways in for an agent, neither of which grants anything.</b> The broker's
/// <c>register</c> makes a pending grant, which holds no scope and mints no token while its
/// request is open; <c>request_access</c> asks for more scopes on the caller's own grant and
/// leaves that grant as it is. Both open the request window, and opening it again focuses the
/// one already open. Only <see cref="AllowAsync"/> and <see cref="Decline"/> change a grant, and
/// only the desktop's own screens call them: no pipe command and no MCP tool reaches either.
/// </para>
/// <para>
/// <b>The limits</b> are held here, under one lock, so two registrations racing cannot both
/// pass them: one open request per name, <see cref="MaxOpen"/> open at once, no request from a
/// name declined within <see cref="DeclineBlock"/>, and no registration under a name an agent
/// already has.
/// </para>
/// <para>
/// <b>Allow asks for Windows Hello</b> through <see cref="IUserConsent"/> before it writes. A host
/// whose consent answers not available — the web harness — refuses every Allow.
/// </para>
/// <para>
/// <b>The person hears of a request</b> as an ADR 0039 alert, which the phone shows and cannot
/// answer: an alert has no answer, and its link opens the desktop's request window. The alert's
/// id is derived from the request's, so the answer closes it after a restart too.
/// </para>
/// </summary>
public sealed partial class McpAccessDesk : IAgentAccessRequests
{
    /// <summary>The most requests open at once (§5).</summary>
    public const int MaxOpen = 5;

    /// <summary>How long a declined name waits before it may ask again (§5).</summary>
    public static readonly TimeSpan DeclineBlock = TimeSpan.FromDays(1);

    /// <summary>What a grant made by a registration records as its creator.</summary>
    public const string CreatedBySelf = "self-registration";

    /// <summary>The longest name an agent may register under.</summary>
    public const int MaxNameLength = 80;

    /// <summary>The notification source every request's alert names.</summary>
    public const string NotificationContext = "mcp-access";

    private readonly IMcpGrantStore _grants;
    private readonly IMcpAccessRequestStore _requests;
    private readonly IUserConsent _consent;
    private readonly TimeProvider _clock;
    private readonly IAgentRequestWindows? _windows;
    private readonly INotifications? _notifications;
    private readonly IMcpAuditLog? _audit;
    private readonly ILogger _log;
    private readonly Lock _gate = new();

    public McpAccessDesk(
        IMcpGrantStore grants,
        IMcpAccessRequestStore requests,
        IUserConsent consent,
        TimeProvider clock,
        IAgentRequestWindows? windows = null,
        INotifications? notifications = null,
        ILogger<McpAccessDesk>? log = null,
        IMcpAuditLog? audit = null)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(clock);

        _grants = grants;
        _requests = requests;
        _consent = consent;
        _clock = clock;
        _windows = windows;
        _notifications = notifications;
        _audit = audit;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Raised after a request is made or answered.</summary>
    public event Action? Changed
    {
        add => _requests.Changed += value;
        remove => _requests.Changed -= value;
    }

    /// <summary>The requests waiting for the person, oldest first.</summary>
    public IReadOnlyList<McpAccessRequest> Waiting() => [.. _requests.List().Where(request => request.IsOpen)];

    /// <summary>The request with <paramref name="id"/>, or null.</summary>
    public McpAccessRequest? Find(string id) => string.IsNullOrWhiteSpace(id) ? null : _requests.Find(id);

    /// <summary>
    /// A new agent registering itself (the broker's <c>register</c>): a pending grant with no
    /// scope, and an open request for <paramref name="scopes"/>. Refused for a blank or overlong
    /// name, no scope, a scope no grant may hold, a name an agent already has or has a request
    /// waiting under, a name declined within a day, and a sixth open request.
    /// </summary>
    public async Task<McpAccessOutcome> RegisterAsync(
        string? name,
        IReadOnlyList<string>? scopes,
        string? executable,
        string? folder,
        CancellationToken cancellationToken = default)
    {
        var clean = CleanName(name);
        if (clean is null) return Refuse(McpAccessRefusals.BadName, $"An agent registers under a name of 1 to {MaxNameLength} characters.");

        if (Scopes(scopes) is not { } asked) return RefuseScopes(scopes);

        McpAccessRequest request;
        McpGrant grant;

        try
        {
            lock (_gate)
            {
                if (RegisterLocked(clean, asked, executable, folder, out grant, out request) is { } refused) return refused;
            }
        }
        catch (IOException)
        {
            return Refuse(McpAccessRefusals.StoreUnavailable, StoreUnavailableMessage);
        }

        _log.LogInformation("Agent {Name} registered itself as grant {GrantId}; request {RequestId} waits for the person.", clean, grant.Id, request.Id);
        await AnnounceAsync(request, cancellationToken).ConfigureAwait(false);

        return new McpAccessOutcome(request, grant, null, $"{clean} is registered as {grant.Id} and pending: it gets no token until you allow it in Backlog.");
    }

    /// <summary>The registration's checks and writes, under the gate: a refusal, or null with the
    /// grant and request made.</summary>
    private McpAccessOutcome? RegisterLocked(
        string clean,
        IReadOnlyList<string> asked,
        string? executable,
        string? folder,
        out McpGrant grant,
        out McpAccessRequest request)
    {
        grant = null!;
        request = null!;

        {
            var all = _requests.List();

            if (Limit(all, clean) is { } refused) return refused;

            if (_grants.List().Any(existing => !existing.Revoked && SameName(existing.Name, clean)))
            {
                return Refuse(McpAccessRefusals.NameTaken, $"An agent is already called {clean}. Register under another name, or ask with request_access from that agent.");
            }

            // Two stores, two writes: a request that cannot be written takes its grant with it,
            // so no grant is left without the request that keeps it pending. A self-registered
            // grant with no allowed registration is pending anyway (McpAccessRules.IsPending).
            grant = _grants.Create(clean, [], CreatedBySelf);
            try
            {
                request = _requests.Add(grant.Id, McpAccessRequestKind.Register, clean, asked, Label(executable), Label(folder));
            }
            catch (IOException)
            {
                _grants.Revoke(grant.Id);
                throw;
            }
        }

        return null;
    }

    /// <summary>
    /// Brings the request window forward for the open request of <paramref name="grantId"/> (the
    /// broker's <c>request</c>), focusing the one already open. Changes nothing. Refused when the
    /// grant has no open request.
    /// </summary>
    public async Task<McpAccessOutcome> ShowAsync(string? grantId)
    {
        var request = string.IsNullOrWhiteSpace(grantId)
            ? null
            : _requests.List().LastOrDefault(candidate => candidate.IsOpen && candidate.GrantId == grantId);

        if (request is null) return Refuse(McpAccessRefusals.NoRequest, "No request is waiting for that grant: register first, or ask with request_access.");

        var shown = await ShowWindowAsync(request).ConfigureAwait(false);

        return new McpAccessOutcome(request, null, null, shown ? "The request window is open." : "The request is waiting on the MCP server page; no window could be opened.", shown);
    }

    /// <inheritdoc />
    public async Task<AgentAccessAnswer> RequestMoreAsync(
        ClaimsPrincipal? caller,
        IReadOnlyList<string> scopes,
        string? folder,
        CancellationToken cancellationToken = default)
    {
        var outcome = await RequestMoreCoreAsync(caller, scopes, folder, cancellationToken).ConfigureAwait(false);

        return new AgentAccessAnswer(outcome.Request?.Id, outcome.WindowOpened, outcome.Refusal, outcome.Message);
    }

    private async Task<McpAccessOutcome> RequestMoreCoreAsync(
        ClaimsPrincipal? caller,
        IReadOnlyList<string> scopes,
        string? folder,
        CancellationToken cancellationToken)
    {
        if (GrantIdOf(caller) is not { } grantId)
        {
            return Refuse(McpAccessRefusals.NoGrant, "Only an agent with a grant of its own can ask for more. Register it with backlog-mcp-headers.mjs --register first.");
        }

        if (Scopes(scopes) is not { } asked) return RefuseScopes(scopes);

        McpAccessRequest request;
        bool fresh;

        lock (_gate)
        {
            var grant = _grants.Find(grantId);
            if (grant is null || grant.Revoked) return Refuse(McpAccessRefusals.NoGrant, "This agent's grant is gone, so there is nothing to add to.");

            var all = _requests.List();
            if (McpAccessRules.IsPending(all, grant)) return Refuse(McpAccessRefusals.Pending, "This agent's registration is still waiting for the person; it cannot ask for more until it is allowed.");

            if (all.FirstOrDefault(candidate => candidate.IsOpen && candidate.GrantId == grantId) is { } open)
            {
                request = open;
                fresh = false;
            }
            else
            {
                var wanted = asked.Where(scope => !grant.Scopes.Contains(scope, StringComparer.Ordinal)).ToList();
                if (wanted.Count == 0) return Refuse(McpAccessRefusals.NothingNew, "This agent holds every scope it asked for already.");

                if (Limit(all, grant.Name) is { } refused) return refused;

                try
                {
                    request = _requests.Add(grant.Id, McpAccessRequestKind.MoreScopes, grant.Name, wanted, null, Label(folder));
                }
                catch (IOException)
                {
                    return Refuse(McpAccessRefusals.StoreUnavailable, StoreUnavailableMessage);
                }

                fresh = true;
            }
        }

        if (fresh)
        {
            _log.LogInformation("Grant {GrantId} asked for {Scopes}; request {RequestId} waits for the person.", grantId, string.Join(' ', request.Scopes), request.Id);
            await AnnounceAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var shown = await ShowWindowAsync(request).ConfigureAwait(false);
        var said = fresh
            ? $"Asked the person for {string.Join(", ", request.Scopes)}. Nothing changes until they allow it in Backlog."
            : $"A request is already waiting for the person, for {string.Join(", ", request.Scopes)}.";

        return new McpAccessOutcome(request, null, null, shown ? said + " Its window is open." : said, shown);
    }

    /// <summary>
    /// The person allows <paramref name="requestId"/> with <paramref name="scopes"/> — the scopes it
    /// asked for, or fewer — after Windows Hello confirms it. A registration's grant takes those
    /// scopes and leaves pending; a request for more adds them to its grant. Refused for a request
    /// that is not open, a scope it did not ask for, none at all, and a Hello that was cancelled or
    /// is not available.
    /// </summary>
    public async Task<McpAccessOutcome> AllowAsync(string requestId, IReadOnlyList<string> scopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        if (Find(requestId) is not { IsOpen: true } request) return Refuse(McpAccessRefusals.NotOpen, "That request has been answered already.");

        var chosen = McpAccessRules.Ordered(scopes);
        if (chosen.Count == 0) return Refuse(McpAccessRefusals.NothingAllowed, "Switch on at least one scope to allow, or decline the request.");

        if (chosen.FirstOrDefault(scope => !request.Scopes.Contains(scope, StringComparer.Ordinal)) is { } extra)
        {
            return Refuse(McpAccessRefusals.NotAsked, $"{request.Name} did not ask for {extra}, so it cannot be allowed here.");
        }

        // A grant another agent made takes its scopes from its granter, so the person's Allow could
        // add nothing a token would carry (local ADR 0052 §5): revoke it and allow it again instead.
        if (request.Kind == McpAccessRequestKind.MoreScopes && _grants.Find(request.GrantId) is { IsDelegated: true })
        {
            return Refuse(McpAccessRefusals.Delegated, $"{request.Name} was granted by another agent, which sets its scopes. Revoke it and allow it yourself, or trim its granter.");
        }

        var consent = await _consent.VerifyAsync(ConsentMessage(request, chosen), cancellationToken).ConfigureAwait(false);

        switch (consent)
        {
            case UserConsentOutcome.NotAvailable:
                return Refuse(McpAccessRefusals.HelloUnavailable, "Windows Hello is not available here, so nothing can be allowed. Set up Windows Hello, or allow the agent in the installed app.");
            case UserConsentOutcome.Declined:
                return Refuse(McpAccessRefusals.HelloDeclined, "Windows Hello did not confirm it, so nothing was allowed.");
        }

        McpGrant? granted;
        McpAccessRequest? answered;

        try
        {
            lock (_gate)
            {
                if (_requests.Find(request.Id) is not { IsOpen: true }) return Refuse(McpAccessRefusals.NotOpen, "That request has been answered already.");

                var current = _grants.Find(request.GrantId);
                var scopesNow = request.Kind == McpAccessRequestKind.Register || current is null
                    ? chosen
                    : McpAccessRules.Ordered([.. current.Scopes, .. chosen]);

                granted = current is { Revoked: false } ? _grants.SetScopes(request.GrantId, scopesNow) : null;
                if (granted is null)
                {
                    _requests.Answer(request.Id, McpAccessRequestState.Declined, []);
                    return Refuse(McpAccessRefusals.NoGrant, $"{request.Name}'s grant was revoked meanwhile, so the request is closed.");
                }

                // The grant holds its scopes before the request reads allowed; should this second
                // write fail, a self-registered grant with no allowed registration stays pending.
                answered = _requests.Answer(request.Id, McpAccessRequestState.Allowed, chosen);
            }
        }
        catch (IOException)
        {
            return Refuse(McpAccessRefusals.StoreUnavailable, StoreUnavailableMessage);
        }

        _log.LogInformation("The person allowed request {RequestId}: grant {GrantId} holds {Scopes}.", request.Id, granted.Id, string.Join(' ', granted.Scopes));
        await SettleAsync(request, "Allowed in Backlog.", cancellationToken).ConfigureAwait(false);

        return new McpAccessOutcome(answered, granted, null, $"{request.Name} may use {string.Join(", ", chosen)} from its next token.");
    }

    /// <summary>
    /// The person declines <paramref name="requestId"/>: a registration's grant is revoked, a
    /// request for more leaves its grant as it was, and the name cannot ask again for a day.
    /// </summary>
    public async Task<McpAccessOutcome> DeclineAsync(string requestId, CancellationToken cancellationToken = default)
    {
        McpAccessRequest? answered;

        try
        {
            lock (_gate)
            {
                if (Find(requestId) is not { IsOpen: true } request) return Refuse(McpAccessRefusals.NotOpen, "That request has been answered already.");

                // Revoked before the request reads declined, so a failed second write never
                // leaves a registration's grant unrevoked and no longer pending.
                if (request.Kind == McpAccessRequestKind.Register) _grants.Revoke(request.GrantId);

                answered = _requests.Answer(request.Id, McpAccessRequestState.Declined, []);
                if (answered is null) return Refuse(McpAccessRefusals.NotOpen, "That request has been answered already.");
            }
        }
        catch (IOException)
        {
            return Refuse(McpAccessRefusals.StoreUnavailable, StoreUnavailableMessage);
        }

        _log.LogInformation("The person declined request {RequestId} from {Name}.", answered.Id, answered.Name);
        await SettleAsync(answered, "Declined in Backlog.", cancellationToken).ConfigureAwait(false);

        return new McpAccessOutcome(answered, null, null, $"Declined. {answered.Name} cannot ask again until {(answered.AnsweredAt!.Value + DeclineBlock).ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture)}.");
    }

    private const string StoreUnavailableMessage =
        "The agents or their requests could not be read or written. Check mcp-grants.json and mcp-access-requests.json in the app's data folder.";

    /// <summary>The limits every new request meets, for <paramref name="name"/>.</summary>
    private McpAccessOutcome? Limit(IReadOnlyList<McpAccessRequest> all, string name)
    {
        if (all.Any(request => request.IsOpen && SameName(request.Name, name)))
        {
            return Refuse(McpAccessRefusals.AlreadyPending, $"{name} already has a request waiting for the person. Run --request to bring its window forward.");
        }

        if (all.Count(request => request.IsOpen) >= MaxOpen)
        {
            return Refuse(McpAccessRefusals.TooMany, $"{MaxOpen} requests are waiting for the person already. Ask again once they have answered one.");
        }

        var now = _clock.GetUtcNow();
        var declined = all
            .Where(request => request.State == McpAccessRequestState.Declined && SameName(request.Name, name) && request.AnsweredAt is { } at && now - at < DeclineBlock)
            .Select(request => request.AnsweredAt!.Value)
            .DefaultIfEmpty()
            .Max();

        if (declined != default)
        {
            return Refuse(McpAccessRefusals.DeclinedRecently, $"The person declined {name} at {declined.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture)}; it may ask again a day after that.");
        }

        return null;
    }

    /// <summary>The grant a principal holds: a grant token's subject, and nothing for a static
    /// caller — the shared token, the hook tokens, the harness.</summary>
    internal static string? GrantIdOf(ClaimsPrincipal? caller) =>
        caller?.Identities.FirstOrDefault(identity => identity.IsAuthenticated && identity.AuthenticationType == McpGrantTokens.AuthenticationType)
            ?.FindFirst("sub")?.Value is { Length: > 0 } id
            ? id
            : null;

    private static IReadOnlyList<string>? Scopes(IReadOnlyList<string>? scopes)
    {
        if (scopes is null || scopes.Count == 0) return null;
        if (scopes.Any(scope => !McpScopes.IsGrantable(scope))) return null;

        return McpAccessRules.Ordered(scopes);
    }

    private static McpAccessOutcome RefuseScopes(IReadOnlyList<string>? scopes)
    {
        if (scopes is null || scopes.Count == 0) return Refuse(McpAccessRefusals.NoScopes, "Name at least one scope to ask for.");

        var bad = scopes.First(scope => !McpScopes.IsGrantable(scope));
        return Refuse(McpAccessRefusals.BadScope, $"No agent may hold '{bad}'. The scopes are: {string.Join(", ", McpScopes.Grantable)}.");
    }

    private static McpAccessOutcome Refuse(string code, string message) => new(null, null, code, message);

    private static bool SameName(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The name, trimmed, with no control character; null when blank or too long.</summary>
    private static string? CleanName(string? name)
    {
        var clean = new string([.. (name ?? string.Empty).Where(character => !char.IsControl(character))]).Trim();
        return clean.Length is > 0 and <= MaxNameLength ? clean : null;
    }

    /// <summary>What an agent says about itself, as shown: no control characters, at most 260.</summary>
    private static string? Label(string? text)
    {
        var clean = new string([.. (text ?? string.Empty).Where(character => !char.IsControl(character))]).Trim();
        return clean.Length == 0 ? null : clean.Length <= 260 ? clean : clean[..260];
    }

    private static string ConsentMessage(McpAccessRequest request, IReadOnlyList<string> scopes) =>
        $"Allow {request.Name} to use {string.Join(", ", scopes)} in Backlog?";

    private async Task<bool> ShowWindowAsync(McpAccessRequest request)
    {
        if (_windows is null) return false;

        try
        {
            return await _windows.ShowAsync(request.Id).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The request stands on the MCP server page whatever the window does.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogWarning(ex, "The request window for {RequestId} could not be opened.", request.Id);
            return false;
        }
    }

    /// <summary>The alert a new request raises (ADR 0039): the phone shows it, and its link opens
    /// the request window on this desktop, where alone it can be answered.</summary>
    private async Task AnnounceAsync(McpAccessRequest request, CancellationToken cancellationToken)
    {
        if (_notifications is null) return;

        var draft = new NotificationDraft(
            new NotificationSource(NotificationContext, request.Id),
            null,
            NotificationKind.Alert,
            NotificationSeverity.Warning,
            Truncate($"{request.Name} wants access", 80),
            Truncate($"Asks for {string.Join(", ", request.Scopes)}. Allow or decline it on your desktop.", 180),
            $"**{request.Name}** asks Backlog for {string.Join(", ", request.Scopes.Select(scope => $"`{scope}`"))}.\n\n"
                + "A grant is decided on the desktop, with Windows Hello: open Backlog there to allow it, trim it, or decline it.",
            DeepLink: AgentRequestSurface.Link(request.Id),
            Origin: new NotificationOrigin("Backlog", Via: "Agent access request"),
            Id: NotificationIdFor(request.Id));

        try
        {
            var published = await _notifications.PublishAsync(draft, cancellationToken).ConfigureAwait(false);
            if (published.IsFailure) _log.LogInformation("No notification for request {RequestId}: {Error}.", request.Id, published.Error.Code);
        }
#pragma warning disable CA1031 // A request stands without its alert; the page and the window still show it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogWarning(ex, "The notification for request {RequestId} could not be published.", request.Id);
        }
    }

    private async Task SettleAsync(McpAccessRequest request, string reason, CancellationToken cancellationToken)
    {
        if (_notifications is null) return;

        try
        {
            await _notifications.CloseAsync(NotificationIdFor(request.Id), reason, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // An alert left open expires on its own; the answer stands.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogWarning(ex, "The notification for request {RequestId} could not be closed.", request.Id);
        }
    }

    /// <summary>The alert's id, derived from the request's, so the answer can close it whenever
    /// it comes.</summary>
    internal static Guid NotificationIdFor(string requestId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(NotificationContext + ":" + requestId)).AsSpan(0, 16));

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}

/// <summary>What the desk answered: the request and grant it touched, or a refusal's code and
/// sentence.</summary>
public sealed record McpAccessOutcome(
    McpAccessRequest? Request,
    McpGrant? Grant,
    string? Refusal,
    string Message,
    bool WindowOpened = false)
{
    /// <summary>Whether it was refused.</summary>
    public bool Refused => Refusal is not null;
}

/// <summary>The refusal codes, as the pipe and the tool carry them.</summary>
public static class McpAccessRefusals
{
    public const string BadName = "bad-name";
    public const string NoScopes = "no-scopes";
    public const string BadScope = "bad-scope";
    public const string NameTaken = "name-taken";
    public const string AlreadyPending = "already-pending";
    public const string TooMany = "too-many";
    public const string DeclinedRecently = "declined-recently";
    public const string NoRequest = "no-request";
    public const string NoGrant = "no-grant";
    public const string Pending = "pending";
    public const string NothingNew = "nothing-new";
    public const string NotOpen = "not-open";
    public const string NothingAllowed = "nothing-allowed";
    public const string NotAsked = "not-asked";
    public const string HelloUnavailable = "hello-unavailable";
    public const string HelloDeclined = "hello-declined";
    public const string StoreUnavailable = "store-unavailable";

    // A granter agent's refusals (local ADR 0052 §5, agents that grant other agents).
    public const string NotGranter = "not-granter";
    public const string OneLevel = "one-level-deep";
    public const string NotDelegable = "not-delegable";
    public const string NotHeld = "not-held";
    public const string OwnGrant = "own-grant";
    public const string GrantersGrant = "granters-grant";
    public const string ReadOnly = "read-only";
    public const string UnknownGrant = "unknown-grant";
    public const string Expired = "expired";
    public const string Delegated = "delegated";
}

/// <summary>The rules a grant and its requests are read by, wherever they are read.</summary>
public static class McpAccessRules
{
    /// <summary>
    /// Whether <paramref name="grant"/> is pending, so it mints no token: a registration with its
    /// request still open — or a grant an agent registered itself whose registration was never
    /// allowed, which is how a lost or unreadable requests file reads. That fails closed.
    /// </summary>
    public static bool IsPending(IEnumerable<McpAccessRequest> requests, McpGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var registrations = requests.Where(request => request.Kind == McpAccessRequestKind.Register && request.GrantId == grant.Id).ToList();

        if (registrations.Any(request => request.IsOpen)) return true;

        return string.Equals(grant.CreatedBy, McpAccessDesk.CreatedBySelf, StringComparison.Ordinal)
            && !registrations.Any(request => request.State == McpAccessRequestState.Allowed);
    }

    /// <summary>The scopes, without repeats, in <see cref="McpScopes.All"/>'s order.</summary>
    public static IReadOnlyList<string> Ordered(IEnumerable<string> scopes)
    {
        var on = scopes.ToHashSet(StringComparer.Ordinal);
        return [.. McpScopes.All.Where(on.Contains)];
    }
}

/// <summary>
/// Brings the request window forward (local ADR 0052 §5, ADR 0043): a pop-out on the request,
/// or the one already open on it, focused. The desktop head answers it over its windows.
/// </summary>
public interface IAgentRequestWindows
{
    /// <summary>Opens or focuses the window on <paramref name="requestId"/>; false when nothing
    /// could be shown.</summary>
    Task<bool> ShowAsync(string requestId);
}
