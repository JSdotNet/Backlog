using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Services;

/// <summary>A minted access token and when it stops being accepted.</summary>
public readonly record struct DeviceToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Turns an owner scope into the short-lived token the rest of the API reads it
/// back out of.
/// <para>
/// A port rather than an implementation, because signing a JWT means a signing
/// key and a validation configuration, and both of those belong to the host
/// that also validates them. The module states what it needs; the sync service
/// registers the JWT implementation beside its own
/// <c>TokenValidationParameters</c>, so the two cannot drift.
/// </para>
/// </summary>
public interface IDeviceTokenIssuer
{
    DeviceToken Issue(OwnerScope scope);
}
