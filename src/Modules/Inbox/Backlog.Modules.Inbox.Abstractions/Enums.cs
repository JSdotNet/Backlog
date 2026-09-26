namespace Backlog.Modules.Inbox.Abstractions;

/// <summary>Lifecycle state of an inbox item (<c>.devbook/domain/inbox/flow.md</c>).
/// "Routed" is deliberately not a member: it is <see cref="Triaged"/> with a
/// routing target on the item, and the persisted status stays <c>triaged</c>.</summary>
public enum InboxStatus
{
    Unprocessed,
    Triaged,
    Deferred,
    Archived
}

/// <summary>What a capture is, as far as the queue can tell. The slugs these map
/// to are the ones the shared <c>CaptureKindMarker</c> draws, so a kind the
/// detector produces is a kind the pane has a glyph for.</summary>
public enum ContentKind
{
    Text,
    Article,
    Link,
    YouTube,
    Image,
    Document,
    Email,
    Code,
    Voice,
    ClaudeArtifact
}

/// <summary>Where a triaged item was sent. <see cref="Devbook"/> is declared
/// because the domain names it and nothing routes there yet — a member the
/// store can already read is cheaper than a value it would throw on later.</summary>
public enum RoutingDomain
{
    Tasks,
    Devbook,
    Archive
}
