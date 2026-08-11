using System.Text;
using System.Text.Json;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Local-first <see cref="IBacklogRepository"/> that stores each entry as a
/// canonical markdown file with YAML frontmatter, plus a derived JSON index for
/// fast listing. Fully offline; no cloud dependency.
/// </summary>
public sealed class FileBacklogRepository : IBacklogRepository
{
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _rootDir;
    private readonly string _entriesDir;
    private readonly string _indexPath;

    /// <summary>Creates a repository rooted at the given folder, or the default
    /// per-user app-data folder (<c>%LOCALAPPDATA%\Backlog</c>) when null.</summary>
    public FileBacklogRepository(string? rootDir = null)
    {
        _rootDir = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");
        _entriesDir = Path.Combine(_rootDir, "entries");
        _indexPath = Path.Combine(_rootDir, "index.json");
        Directory.CreateDirectory(_entriesDir);
    }

    /// <summary>The folder where canonical markdown files live.</summary>
    public string RootDirectory => _rootDir;

    public async Task SaveAsync(BacklogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var markdown = ToMarkdown(entry);
        var path = EntryPath(entry.Id);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(path, markdown, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            await RebuildIndexAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BacklogEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = EntryPath(id);
        if (!File.Exists(path)) return null;

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return FromMarkdown(text);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = EntryPath(id);
            if (File.Exists(path)) File.Delete(path);
            await RebuildIndexAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BacklogEntrySummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_indexPath))
            return Array.Empty<BacklogEntrySummary>();

        await using var stream = File.OpenRead(_indexPath);
        var summaries = await JsonSerializer
            .DeserializeAsync<List<BacklogEntrySummary>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return summaries ?? new List<BacklogEntrySummary>();
    }

    // --- Index --------------------------------------------------------------

    // Rebuilds the JSON index by scanning canonical markdown (the source of truth).
    private async Task RebuildIndexAsync(CancellationToken cancellationToken)
    {
        var summaries = new List<BacklogEntrySummary>();
        foreach (var file in Directory.EnumerateFiles(_entriesDir, "*.md"))
        {
            var text = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var entry = FromMarkdown(text);
            if (entry is null) continue;
            summaries.Add(new BacklogEntrySummary(
                entry.Id,
                entry.Title,
                EnumMap.ToWire(entry.Type),
                EnumMap.ToWire(entry.Status),
                EnumMap.ToWire(entry.Priority),
                entry.CompletedSubItemCount,
                entry.TotalSubItemCount,
                entry.CreatedAt,
                entry.Order,
                entry.Area));
        }

        // Hand-ranked order wins; entries that have never been ranked share the
        // default rank and fall back to newest-first.
        summaries.Sort((a, b) => a.Order != b.Order
            ? a.Order.CompareTo(b.Order)
            : b.CreatedAt.CompareTo(a.CreatedAt));
        var json = JsonSerializer.Serialize(summaries, JsonOptions);
        await File.WriteAllTextAsync(_indexPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    // --- Markdown (canonical) ----------------------------------------------

    private static string ToMarkdown(BacklogEntry entry)
    {
        var fm = new EntryFrontmatter
        {
            Id = entry.Id.ToString(),
            Title = entry.Title,
            Type = EnumMap.ToWire(entry.Type),
            Status = EnumMap.ToWire(entry.Status),
            Priority = EnumMap.ToWire(entry.Priority),
            RepoIds = entry.RepoIds.ToList(),
            Tags = entry.Tags.ToList(),
            SourceInboxId = entry.SourceInboxId,
            CreatedAt = entry.CreatedAt,
            Order = entry.Order,
            Area = entry.Area,
            SubItems = entry.SubItems.Select(s => new SubItemDto
            {
                Id = s.Id.ToString(),
                Title = s.Title,
                Status = EnumMap.ToWire(s.Status),
                Notes = s.Notes,
                Order = s.Order
            }).ToList(),
            Projections = entry.ProjectionRefs.Select(p => new ProjectionRefDto
            {
                RepoId = p.RepoId,
                ExternalId = p.ExternalId,
                TargetType = p.TargetType
            }).ToList(),
            UsageEvents = entry.UsageEvents.Select(u => new UsageEventDto
            {
                Timestamp = u.Timestamp,
                Action = u.Action
            }).ToList()
        };

        var yaml = YamlSerializer.Serialize(fm).TrimEnd();
        var sb = new StringBuilder();
        sb.Append("---\n").Append(yaml).Append("\n---\n\n");
        sb.Append(entry.ContentMd);
        if (!entry.ContentMd.EndsWith('\n')) sb.Append('\n');
        return sb.ToString();
    }

    private static BacklogEntry? FromMarkdown(string text)
    {
        var (yaml, body) = SplitFrontmatter(text);
        if (yaml is null) return null;

        var fm = YamlDeserializer.Deserialize<EntryFrontmatter>(yaml);
        if (fm is null || string.IsNullOrWhiteSpace(fm.Id)) return null;

        var entry = new BacklogEntry(
            Guid.Parse(fm.Id),
            fm.Title,
            body,
            EnumMap.ParseType(fm.Type),
            EnumMap.ParseStatus(fm.Status),
            EnumMap.ParsePriority(fm.Priority),
            fm.RepoIds,
            fm.Tags,
            string.IsNullOrWhiteSpace(fm.SourceInboxId) ? null : fm.SourceInboxId,
            fm.CreatedAt);

        entry.SetOrder(fm.Order);
        entry.SetArea(fm.Area);
        foreach (var s in fm.SubItems.OrderBy(s => s.Order))
        {
            var subItem = entry.CreateSubItemForLoad(
                Guid.Parse(s.Id),
                s.Title,
                EnumMap.ParseSubItemStatus(s.Status),
                s.Notes,
                s.Order);
            entry.LoadSubItem(subItem);
        }

        foreach (var p in fm.Projections)
            entry.AddProjectionRef(new ProjectionRef(p.RepoId, p.ExternalId, p.TargetType));

        foreach (var u in fm.UsageEvents)
            entry.LoadUsageEvent(new UsageEvent(u.Timestamp, u.Action));

        return entry;
    }

    private static (string? Yaml, string Body) SplitFrontmatter(string text)
    {
        text = text.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n")) return (null, text);

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return (null, text);

        var yaml = text.Substring(4, end - 4);
        var afterMarker = end + "\n---".Length;
        var body = afterMarker < text.Length ? text[afterMarker..] : string.Empty;
        return (yaml, body.TrimStart('\n'));
    }

    private string EntryPath(Guid id) => Path.Combine(_entriesDir, $"{id}.md");
}
