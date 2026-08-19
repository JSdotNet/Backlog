namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Serialization DTO for an entry's YAML frontmatter. Captures entry state that
/// belongs in the markdown document; list order lives in the sidecar metadata index.
/// </summary>
internal sealed class EntryFrontmatter
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public List<string>? RepoIds { get; set; }
    public List<string>? Tags { get; set; }
    public string? SourceInboxId { get; set; }
    public string? CreatedAt { get; set; }
    public int? Order { get; set; }
    public string? Area { get; set; }

    // The two dates and the reminder are strings rather than DateOnly/DateTime for
    // the same reason CreatedAt is: YamlDotNet writes a date-shaped .NET type as a
    // nested map of alternative renderings (utc_date_time, local_date_time), which
    // then has to be flattened again on the way back in. A round trip that needs a
    // repair pass is not a round trip, so these carry their own invariant text and
    // the type conversion happens in this assembly where it can be read.
    public string? DueOn { get; set; }
    public string? RemindAt { get; set; }

    // The recurrence is stored as its metadata token ("weekly", "2w",
    // "weekdays") rather than as a nested interval/unit/weekdays map. One line,
    // one grammar, and the same string a person would have typed — and the parser
    // that reads the token is already the shared vocabulary for it.
    public string? Recurrence { get; set; }
    public string? InMyDayOn { get; set; }
    public List<string>? DependsOn { get; set; }
    public string? RecurrenceSourceId { get; set; }
    public List<SubItemDto>? SubItems { get; set; }
    public List<ProjectionRefDto>? Projections { get; set; }
    public List<UsageEventDto>? UsageEvents { get; set; }
}

internal sealed class SubItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int Order { get; set; }
}

internal sealed class ProjectionRefDto
{
    public string RepoId { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
}

internal sealed class UsageEventDto
{
    public DateTimeOffset Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
}
