namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Serialization DTO for an entry's YAML frontmatter. Captures the full aggregate
/// state so the markdown file is a self-sufficient canonical source of truth.
/// </summary>
internal sealed class EntryFrontmatter
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public List<string> RepoIds { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? SourceInboxId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Order { get; set; }
    public string? Area { get; set; }
    public List<SubItemDto> SubItems { get; set; } = new();
    public List<ProjectionRefDto> Projections { get; set; } = new();
    public List<UsageEventDto> UsageEvents { get; set; } = new();
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
