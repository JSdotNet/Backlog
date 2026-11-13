using System.Globalization;
using System.Security.Claims;

using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Mcp;

using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// Agents that grant other agents (local ADR 0052 §5): the person switches <c>agents:grant</c> on
/// for an orchestrator, with Windows Hello, and after that it answers its workers' requests and
/// grants, trims, renews and revokes them without the person — inside rules this desk holds,
/// under the same lock as the person's own answers, so a granter and the person cannot both
/// answer one request.
/// <list type="bullet">
/// <item>Only scopes the granter holds, and never <see cref="McpScopes.NonDelegable"/>.</item>
/// <item>One level deep: a grant another agent made never grants.</item>
/// <item>Never its own grant, and never its granter's.</item>
/// <item>The grants the person or another granter made are read-only to it.</item>
/// <item>A delegated grant lives <see cref="McpGrantClaims.DelegationLifetime"/>; an edit renews
/// it. Its tokens are cut to the granter's scopes at each mint, and revoking the granter
/// revokes it in the same write (<see cref="McpGrantTokens"/>, <see cref="IMcpGrantStore"/>).</item>
/// </list>
/// Each grant, edit and revoke raises a quiet notification — an Info alert, nothing to answer —
/// and writes an audit record naming the granter as caller and the grant it touched as target.
/// </summary>
public sealed partial class McpAccessDesk : IAgentGrants
{
    /// <inheritdoc />
    public AgentGrantListing List(ClaimsPrincipal? caller)
    {
        try
        {
            lock (_gate)
            {
                if (GranterOf(caller, out var granter) is { } refused) return new AgentGrantListing([], [], refused.Code, refused.Message);

                // Only the requests it may answer: new agents, and more for the agents it granted. The
                // person's own agents asking for more are the person's to see.
                var grants = _grants.List();
                var mine = grants
                    .Where(grant => !grant.Revoked && string.Equals(grant.GrantedBy, granter.Id, StringComparison.Ordinal))
                    .ToList();
                var requests = _requests.List()
                    .Where(request => request.IsOpen
                        && request.GrantId != granter.Id
                        && (request.Kind == McpAccessRequestKind.Register || mine.Any(grant => grant.Id == request.GrantId)))
                    .Select(View)
                    .ToList();
                var granted = mine.Select(View).ToList();

                return new AgentGrantListing(
                    requests,
                    granted,
                    null,
                    $"{Count(requests.Count, "request")} waiting; {Count(granted.Count, "agent")} granted by {granter.Name}.");
            }
        }
        catch (IOException)
        {
            return new AgentGrantListing([], [], McpAccessRefusals.StoreUnavailable, StoreUnavailableMessage);
        }
    }

    /// <inheritdoc />
    public async Task<AgentGrantAnswer> GrantAsync(
        ClaimsPrincipal? caller,
        string requestId,
        IReadOnlyList<string>? scopes,
        CancellationToken cancellationToken = default)
    {
        McpGrant granter;
        McpGrant granted;
        McpAccessRequest request;

        try
        {
            lock (_gate)
            {
                if (GranterOf(caller, out granter) is { } notGranter) return Refused(notGranter);

                if (Find(requestId) is not { IsOpen: true } open) return Refused(McpAccessRefusals.NotOpen, "No request with that id is waiting: it was answered already, or never made.");
                request = open;

                var target = _grants.Find(request.GrantId);
                if (Target(granter, target, request.GrantId, request.Kind == McpAccessRequestKind.Register) is { } notTarget) return Refused(notTarget);

                var asked = request.Scopes;
                IReadOnlyList<string> chosen;

                if (scopes is null)
                {
                    chosen = McpAccessRules.Ordered(asked.Where(scope => McpScopes.IsDelegable(scope) && granter.Scopes.Contains(scope, StringComparer.Ordinal)));
                }
                else
                {
                    if (Delegable(granter, scopes) is { } notDelegable) return Refused(notDelegable);

                    if (scopes.FirstOrDefault(scope => !asked.Contains(scope, StringComparer.Ordinal)) is { } extra)
                    {
                        return Refused(McpAccessRefusals.NotAsked, $"{request.Name} did not ask for {extra}, so it cannot be granted here.");
                    }
                    chosen = McpAccessRules.Ordered(scopes);
                }

                if (chosen.Count == 0)
                {
                    return Refused(McpAccessRefusals.NothingAllowed, $"None of the scopes {request.Name} asked for is one {granter.Name} may hand on; the person can answer it in Backlog.");
                }

                // A request for more adds to what the grant holds; a registration starts from nothing.
                var scopesNow = request.Kind == McpAccessRequestKind.Register
                    ? chosen
                    : McpAccessRules.Ordered([.. target!.Scopes.Where(scope => McpScopes.IsDelegable(scope) && granter.Scopes.Contains(scope, StringComparer.Ordinal)), .. chosen]);

                if (_grants.Delegate(request.GrantId, scopesNow, granter.Id, _clock.GetUtcNow() + McpGrantClaims.DelegationLifetime) is not { } written)
                {
                    return Refused(McpAccessRefusals.UnknownGrant, $"{request.Name}'s grant was revoked meanwhile.");
                }

                granted = written;
                _requests.Answer(request.Id, McpAccessRequestState.Allowed, chosen);
            }
        }
        catch (IOException)
        {
            return Refused(McpAccessRefusals.StoreUnavailable, StoreUnavailableMessage);
        }

        _log.LogInformation("Granter {GranterId} granted request {RequestId}: grant {GrantId} holds {Scopes} until {ExpiresAt}.", granter.Id, request.Id, granted.Id, string.Join(' ', granted.Scopes), granted.ExpiresAt);
        await SettleAsync(request, $"Granted by {granter.Name}.", cancellationToken).ConfigureAwait(false);
        await RecordDelegationAsync(McpAudit.DelegatedGrant, granter, granted, $"{granter.Name} granted {granted.Name}", $"{Scopes(granted)} until {Expiry(granted)}.", cancellationToken).ConfigureAwait(false);

        return new AgentGrantAnswer(View(granted), null, $"{granted.Name} may use {Scopes(granted)} from its next token, until {Expiry(granted)} unless renewed.");
    }

    /// <inheritdoc />
    public async Task<AgentGrantAnswer> EditScopesAsync(
        ClaimsPrincipal? caller,
        string grantId,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        McpGrant granter;
        McpGrant edited;

        try
        {
            lock (_gate)
            {
                if (GranterOf(caller, out granter) is { } notGranter) return Refused(notGranter);

                var target = string.IsNullOrWhiteSpace(grantId) ? null : _grants.Find(grantId);
                if (Target(granter, target, grantId, registering: false) is { } notTarget) return Refused(notTarget);

                if (target!.IsExpired(_clock.GetUtcNow()))
                {
                    return Refused(McpAccessRefusals.Expired, $"{target.Name}'s grant expired without a renewal. Revoke it; the agent can ask again with --register.");
                }

                if (scopes.Count == 0) return Refused(McpAccessRefusals.NoScopes, "Name at least one scope. To take every scope away, revoke the agent.");
                if (Delegable(granter, scopes) is { } notDelegable) return Refused(notDelegable);

                // Renewed with every edit: an edit with the scopes it has is the renewal.
                if (_grants.Delegate(grantId, McpAccessRules.Ordered(scopes), granter.Id, _clock.GetUtcNow() + McpGrantClaims.DelegationLifetime) is not { } written)
                {
                    return Refused(McpAccessRefusals.UnknownGrant, "That agent was revoked meanwhile.");
                }

                edited = written;
            }
        }
        catch (IOException)
        {
            return Refused(McpAccessRefusals.StoreUnavailable, StoreUnavailableMessage);
        }

        _log.LogInformation("Granter {GranterId} set grant {GrantId} to {Scopes} until {ExpiresAt}.", granter.Id, edited.Id, string.Join(' ', edited.Scopes), edited.ExpiresAt);
        await RecordDelegationAsync(McpAudit.DelegatedScopes, granter, edited, $"{granter.Name} changed {edited.Name}", $"{Scopes(edited)} until {Expiry(edited)}.", cancellationToken).ConfigureAwait(false);

        return new AgentGrantAnswer(View(edited), null, $"{edited.Name} holds {Scopes(edited)} from its next token, renewed until {Expiry(edited)}.");
    }

    /// <inheritdoc />
    public async Task<AgentGrantAnswer> RevokeAsync(
        ClaimsPrincipal? caller,
        string grantId,
        CancellationToken cancellationToken = default)
    {
        McpGrant granter;
        McpGrant revoked;

        try
        {
            lock (_gate)
            {
                if (GranterOf(caller, out granter) is { } notGranter) return Refused(notGranter);

                var target = string.IsNullOrWhiteSpace(grantId) ? null : _grants.Find(grantId);
                if (Target(granter, target, grantId, registering: false) is { } notTarget) return Refused(notTarget);

                if (_grants.Revoke(grantId) is not { } written) return Refused(McpAccessRefusals.UnknownGrant, "No agent has that grant id.");
                revoked = written;
            }
        }
        catch (IOException)
        {
            return Refused(McpAccessRefusals.StoreUnavailable, StoreUnavailableMessage);
        }

        _log.LogInformation("Granter {GranterId} revoked grant {GrantId}.", granter.Id, revoked.Id);
        await RecordDelegationAsync(McpAudit.DelegatedRevoke, granter, revoked, $"{granter.Name} revoked {revoked.Name}", "Its sessions lost access.", cancellationToken).ConfigureAwait(false);

        return new AgentGrantAnswer(View(revoked), null, $"{revoked.Name} is revoked: its sessions lose access now.");
    }

    /// <summary>A delegation refusal: its code and sentence.</summary>
    private sealed record DelegationRefusal(string Code, string Message);

    /// <summary>The grant <paramref name="caller"/> holds, if it may grant: a grant token's, not
    /// revoked, not pending, holding <c>agents:grant</c> now, and not delegated itself.</summary>
    private DelegationRefusal? GranterOf(ClaimsPrincipal? caller, out McpGrant granter)
    {
        granter = null!;

        if (GrantIdOf(caller) is not { } id || _grants.Find(id) is not { Revoked: false } found)
        {
            return new(McpAccessRefusals.NotGranter, "Only an agent with a grant of its own, holding agents:grant, can grant other agents.");
        }

        if (found.IsDelegated)
        {
            return new(McpAccessRefusals.OneLevel, $"{found.Name} was granted by another agent, and delegation is one level deep: it cannot grant further.");
        }

        if (McpAccessRules.IsPending(_requests.List(), found) || !found.Scopes.Contains(McpScopes.AgentsGrant, StringComparer.Ordinal))
        {
            return new(McpAccessRefusals.NotGranter, $"{found.Name} does not hold agents:grant. The person switches it on in Backlog, with Windows Hello.");
        }

        granter = found;
        return null;
    }

    /// <summary>Whether <paramref name="granter"/> may touch <paramref name="target"/>: a grant
    /// that exists and is not revoked, is neither its own nor its granter's, and is one it made —
    /// or, for a registration being answered, a pending one nobody made yet.</summary>
    private static DelegationRefusal? Target(McpGrant granter, McpGrant? target, string targetId, bool registering)
    {
        if (string.Equals(targetId, granter.Id, StringComparison.Ordinal))
        {
            return new(McpAccessRefusals.OwnGrant, "A granter never changes its own grant: only the person does, in Backlog.");
        }

        if (granter.GrantedBy is { } above && string.Equals(targetId, above, StringComparison.Ordinal))
        {
            return new(McpAccessRefusals.GrantersGrant, "A granter never changes the grant of the agent that granted it.");
        }

        if (target is null || target.Revoked) return new(McpAccessRefusals.UnknownGrant, "No agent has that grant id, or it is revoked.");

        if (registering && !target.IsDelegated) return null;

        if (!string.Equals(target.GrantedBy, granter.Id, StringComparison.Ordinal))
        {
            return new(
                McpAccessRefusals.ReadOnly,
                target.IsDelegated
                    ? $"{target.Name} was granted by another granter, so it is read-only to {granter.Name}."
                    : $"{target.Name} was granted by the person, so it is read-only to {granter.Name}.");
        }

        return null;
    }

    /// <summary>The first scope a granter may not hand on: unknown, never delegable, or one it
    /// does not hold itself.</summary>
    private static DelegationRefusal? Delegable(McpGrant granter, IReadOnlyList<string> scopes)
    {
        if (scopes.FirstOrDefault(scope => McpScopes.NonDelegable.Contains(scope, StringComparer.Ordinal)) is { } powerful)
        {
            return new(McpAccessRefusals.NotDelegable, $"{powerful} is never granted by an agent: {string.Join(", ", McpScopes.NonDelegable)} stay the person's to give.");
        }

        if (scopes.FirstOrDefault(scope => !McpScopes.IsGrantable(scope)) is { } bad)
        {
            return new(McpAccessRefusals.BadScope, $"No agent may hold '{bad}'.");
        }

        if (scopes.FirstOrDefault(scope => !granter.Scopes.Contains(scope, StringComparer.Ordinal)) is { } lacking)
        {
            return new(McpAccessRefusals.NotHeld, $"{granter.Name} does not hold {lacking}, so it cannot grant it.");
        }

        return null;
    }

    /// <summary>The audit record naming both — the granter as caller, the grant it touched as
    /// target — and the quiet notification that tells the person, who need do nothing.</summary>
    private async Task RecordDelegationAsync(string what, McpGrant granter, McpGrant target, string title, string summary, CancellationToken cancellationToken)
    {
        if (_audit is not null)
        {
            try
            {
                await _audit.AppendAsync(
                    new McpAuditRecord(
                        _clock.GetUtcNow(),
                        McpScopePolicies.GrantCaller(granter),
                        what,
                        McpScopePolicies.GrantCaller(target),
                        McpCallOutcome.Succeeded,
                        McpCallAudit.CorrelationId()),
                    CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // The grant stands; the log line says the record is missing.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _log.LogWarning(ex, "The audit record of {What} by {GranterId} on {GrantId} could not be written.", what, granter.Id, target.Id);
            }
        }

        if (_notifications is null) return;

        var draft = new NotificationDraft(
            new NotificationSource(NotificationContext, target.Id),
            null,
            NotificationKind.Alert,
            NotificationSeverity.Info,
            Truncate(title, 80),
            Truncate(summary, 180),
            $"**{granter.Name}** holds agents:grant and {what.Replace("delegated ", string.Empty, StringComparison.Ordinal)} **{target.Name}**: {summary}\n\n"
                + "Nothing is needed from you. You can revoke either of them on the MCP server page.",
            Origin: new NotificationOrigin("Backlog", Via: "Agent granted by agent"));

        try
        {
            var published = await _notifications.PublishAsync(draft, cancellationToken).ConfigureAwait(false);
            if (published.IsFailure) _log.LogInformation("No notification for {What} on {GrantId}: {Error}.", what, target.Id, published.Error.Code);
        }
#pragma warning disable CA1031 // The grant stands without its notification; the page shows it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogWarning(ex, "The notification for {What} on {GrantId} could not be published.", what, target.Id);
        }
    }

    private static AgentGrantAnswer Refused(DelegationRefusal refusal) => new(null, refusal.Code, refusal.Message);

    private static AgentGrantAnswer Refused(string code, string message) => new(null, code, message);

    private static AgentRequestView View(McpAccessRequest request) => new(
        request.Id,
        request.GrantId,
        request.Kind == McpAccessRequestKind.Register ? "register" : "more-scopes",
        request.Name,
        request.Scopes,
        request.Executable,
        request.Folder,
        request.RequestedAt);

    private static AgentGrantView View(McpGrant grant) => new(grant.Id, grant.Name, grant.Scopes, grant.ExpiresAt);

    private static string Scopes(McpGrant grant) => grant.Scopes.Count == 0 ? "no scope" : string.Join(", ", grant.Scopes);

    private static string Expiry(McpGrant grant) =>
        grant.ExpiresAt is { } at ? at.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture) : "never";

    private static string Count(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count.ToString(CultureInfo.InvariantCulture)} {noun}s";
}
