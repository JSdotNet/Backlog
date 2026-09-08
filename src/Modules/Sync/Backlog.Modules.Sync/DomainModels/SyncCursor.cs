namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// An owner and a store continuation that have been verified to belong
/// together — what <c>ISyncCursorCodec</c> mints and what it hands back after
/// checking a cursor against the caller.
/// <para>
/// It exists because there are now two feeds and one check. Tasks and session
/// records live in separate containers with separate change feeds
/// (.arc42/adr/0005 §Storage), but the reason a cursor has to be signed is
/// identical for both: a Cosmos continuation embeds the feed range it was minted
/// for, so replaying one belonging to somebody else reads their partition and
/// the store will not object. Minting that check twice — once per feed — would
/// be two places for it to drift, and the second one would drift silently
/// because nothing fails when a signature is merely weaker.
/// </para>
/// <para>
/// It is deliberately not the type the ports accept. <see cref="TaskReplicaCursor"/>
/// and <see cref="SessionReplicaCursor"/> are each constructed from one of
/// these, so a continuation for the task feed cannot be handed to the session
/// replica by a caller who reached for the wrong local variable — a mistake the
/// compiler should catch, because both are an owner and a string and neither
/// would fail at run time until a page of the wrong feed came back.
/// </para>
/// </summary>
public readonly record struct SyncCursor(OwnerId Owner, string Continuation);
