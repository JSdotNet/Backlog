using Backlog.Modules.Sync.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Services;

/// <summary>
/// Turns a store continuation into a cursor a client may hold, and back again —
/// refusing anything it did not mint and anything minted for somebody else.
/// <para>
/// This is the check that keeps one owner out of another's change feed. In the
/// Cosmos v3 SDK every <c>ChangeFeedStartFrom</c> factory takes a
/// <c>FeedRange</c> except the continuation one, where the range is already
/// embedded in the token; <c>FeedRange.FromPartitionKey</c> therefore cannot
/// constrain a resumed feed, and a raw continuation minted for owner A and
/// replayed by owner B reads A's partition. .arc42/adr/0005 §Identity says
/// storage will not stop that — the service reaches Cosmos under one managed
/// identity that can see every partition — so the service has to, and this is
/// where it does.
/// </para>
/// <para>
/// The port lives in the module and the implementation in the host, exactly as
/// <see cref="IDeviceTokenIssuer"/> does: minting means holding a signing key,
/// and the key belongs with the host that validates what it signed.
/// </para>
/// </summary>
public interface ISyncCursorCodec
{
    /// <summary>Wraps a store continuation for <paramref name="owner"/> in a
    /// signed, opaque string. What comes back is safe to hand a client, because
    /// changing any part of it invalidates it.</summary>
    string Mint(OwnerId owner, string continuation);

    /// <summary>
    /// Unwraps a cursor, or says why not. Two distinct failures, and they are
    /// deliberately not merged: <c>sync.cursor_malformed</c> is a caller holding
    /// something that is not a cursor of ours, and <c>sync.cursor_not_yours</c>
    /// is a caller holding a real cursor belonging to somebody else. The second
    /// is attributable and must be loud.
    /// </summary>
    Result<TaskReplicaCursor> Verify(string cursor, OwnerId caller);
}
