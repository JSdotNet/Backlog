namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// The claims a device token carries. There are only two, because a device
/// token asserts only two things: which owner's data the caller may see, and
/// which device is asking.
/// <para>
/// <see cref="DeviceId"/> is <c>sub</c> rather than a name of our own — the
/// device is the subject of the token, and JWT already has a registered claim
/// for that.
/// </para>
/// </summary>
public static class SyncClaims
{
    /// <summary>The only value that decides what a request may read. The
    /// service scopes every query to it; nothing downstream re-checks it.</summary>
    public const string OwnerId = "owner_id";

    /// <summary>Which of the owner's devices is calling. Useful for telling
    /// them apart in a device list; it grants nothing on its own.</summary>
    public const string DeviceId = "sub";
}
