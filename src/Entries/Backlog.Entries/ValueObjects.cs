namespace Backlog.Entries;

/// <summary>
/// Immutable link to a downstream external artifact created from an entry.
/// Equality is by value.
/// </summary>
public sealed record ProjectionRef(string RepoId, string ExternalId, string TargetType);

/// <summary>
/// Immutable audit record of a prompt copy/use. Equality is by value.
/// </summary>
public sealed record UsageEvent(DateTimeOffset Timestamp, string Action);
