namespace Backlog.Infrastructure.Sqlite.Inbox;

/// <summary>
/// The one owned collection of an inbox item that is not a bare string list,
/// as it is written into its JSON column. Repo ids and the routing's two lists
/// are plain <c>string[]</c> and go through <see cref="TaskPayloads.Write{T}"/>
/// directly; tags carry a flag, so they need a shape. Same serializer options
/// as the task columns beside them, so the two read alike in a SQLite browser.
/// </summary>
/// <param name="Name">Bare, no <c>#</c>.</param>
/// <param name="Auto">Whether a capture source suggested it rather than a
/// person typing it. Short on purpose: it is a column payload, not a DTO.</param>
internal sealed record InboxTagPayload(string Name, bool Auto);
