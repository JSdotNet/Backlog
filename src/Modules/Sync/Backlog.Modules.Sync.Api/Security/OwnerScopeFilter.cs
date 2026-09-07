using System.Security.Claims;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Endpoints;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// Turns the calling token into an <see cref="OwnerScope"/> once, before the
/// endpoint runs, and refuses the call if it cannot.
/// <para>
/// This is the single point where the sync service decides whose data a request
/// is about, and it reads the answer only out of the validated token. Nothing
/// downstream re-derives it, and no endpoint accepts an owner id from a route,
/// a query string, or a body — that is what makes the service, rather than the
/// store, the thing that keeps a device inside its own owner
/// (.arc42/adr/0005 §Identity).
/// </para>
/// <para>
/// The authorization policy already requires both claims, so a request that
/// reaches here without them is not an expected failure — it is a policy that
/// stopped matching this filter. It still answers 401 rather than throwing,
/// because a request that cannot be scoped must not proceed under any
/// circumstances.
/// </para>
/// </summary>
internal sealed class OwnerScopeFilter : IEndpointFilter
{
    private const string ItemKey = "sync.owner-scope";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!TryResolve(context.HttpContext.User, out var scope))
        {
            return SyncResults.Problem(
                context.HttpContext,
                StatusCodes.Status401Unauthorized,
                SyncErrorCodes.DeviceCredentialInvalid,
                "This call needs a device token naming an owner.");
        }

        context.HttpContext.Items[ItemKey] = scope;

        return await next(context);
    }

    /// <summary>The scope this request was resolved to. Only valid on an
    /// endpoint that went through the filter, which is why it throws rather
    /// than returning a default nobody would notice.</summary>
    internal static OwnerScope Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(ItemKey, out var value) && value is OwnerScope scope
            ? scope
            : throw new InvalidOperationException(
                "No owner scope on this request. The endpoint is missing RequireOwnerScope().");
    }

    private static bool TryResolve(ClaimsPrincipal user, out OwnerScope scope)
    {
        scope = default;

        if (!Guid.TryParse(user.FindFirstValue(SyncClaims.OwnerId), out var ownerId)
            || !Guid.TryParse(user.FindFirstValue(SyncClaims.DeviceId), out var deviceId))
        {
            return false;
        }

        scope = new OwnerScope(new OwnerId(ownerId), new DeviceId(deviceId));
        return true;
    }
}

/// <summary>Where an endpoint group asks for the scope, and where an endpoint
/// reads it back.</summary>
internal static class OwnerScopeExtensions
{
    /// <summary>Every endpoint in this group is scoped to the caller's owner
    /// before it runs.</summary>
    internal static TBuilder RequireOwnerScope<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter<TBuilder, OwnerScopeFilter>();
        return builder;
    }

    /// <inheritdoc cref="OwnerScopeFilter.Of" />
    internal static OwnerScope GetOwnerScope(this HttpContext context) => OwnerScopeFilter.Of(context);
}
