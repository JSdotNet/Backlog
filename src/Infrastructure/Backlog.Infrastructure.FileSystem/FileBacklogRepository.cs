using Backlog.Modules.Backlog.Abstractions;
using System.Globalization;
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

    public const string BacklogFolderName = "_backlog";
    public const string InboxFolderName = "_inbox";
    private const string LegacyEntriesFolderName = "entries";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions MetaJsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _rootDir;
    private readonly string _entriesDir;
    private readonly string _indexPath;
    private readonly string _entryMetaDir;
    private readonly string _entryOrderIndexPath;

    /// <summary>Creates a repository rooted at the given folder, or the default
    /// per-user app-data folder (<c>%LOCALAPPDATA%\Backlog</c>) when null.</summary>
    public FileBacklogRepository(string? rootDir = null)
    {
        _rootDir = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");
        _entriesDir = Path.Combine(_rootDir, BacklogFolderName);
        _indexPath = Path.Combine(_rootDir, "index.json");
        _entryMetaDir = Path.Combine(_entriesDir, "_meta");
        _entryOrderIndexPath = Path.Combine(_entryMetaDir, "index.json");
        EnsureStorageFolders(_rootDir);
        Directory.CreateDirectory(_entryMetaDir);
    }

    public static void EnsureStorageFolders(string rootDir)
    {
        var backlogDir = Path.Combine(rootDir, BacklogFolderName);
        var legacyEntriesDir = Path.Combine(rootDir, LegacyEntriesFolderName);
        if (!Directory.Exists(backlogDir) && Directory.Exists(legacyEntriesDir))
        {
            Directory.Move(legacyEntriesDir, backlogDir);
        }

        Directory.CreateDirectory(backlogDir);
        Directory.CreateDirectory(Path.Combine(rootDir, InboxFolderName));
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
            await SaveEntryOrderAsync(entry.Id, entry.Order, cancellationToken).ConfigureAwait(false);
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
        var order = await ReadEntryOrderAsync(id, cancellationToken).ConfigureAwait(false);
        return FromMarkdown(text, order);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = EntryPath(id);
            if (File.Exists(path)) File.Delete(path);
            await RemoveEntryOrderAsync(id, cancellationToken).ConfigureAwait(false);
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
        var orders = await ReadEntryOrdersAsync(cancellationToken).ConfigureAwait(false);
        var summaries = new List<BacklogEntrySummary>();
        foreach (var file in Directory.EnumerateFiles(_entriesDir, "*.md"))
        {
            var text = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var entry = FromMarkdown(text);
            if (entry is null) continue;
            if (orders.TryGetValue(entry.Id, out var order)) entry.SetOrder(order);

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

    private async Task SaveEntryOrderAsync(Guid id, int order, CancellationToken cancellationToken)
    {
        var orders = await ReadEntryOrdersAsync(cancellationToken).ConfigureAwait(false);
        orders[id] = order;
        await WriteEntryOrdersAsync(orders, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int?> ReadEntryOrderAsync(Guid id, CancellationToken cancellationToken)
    {
        var orders = await ReadEntryOrdersAsync(cancellationToken).ConfigureAwait(false);
        return orders.TryGetValue(id, out var order) ? order : null;
    }

    private async Task RemoveEntryOrderAsync(Guid id, CancellationToken cancellationToken)
    {
        var orders = await ReadEntryOrdersAsync(cancellationToken).ConfigureAwait(false);
        if (!orders.Remove(id)) return;
        await WriteEntryOrdersAsync(orders, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<Guid, int>> ReadEntryOrdersAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_entryOrderIndexPath)) return new Dictionary<Guid, int>();

        await using var stream = File.OpenRead(_entryOrderIndexPath);
        var index = await JsonSerializer
            .DeserializeAsync<EntryOrderIndex>(stream, MetaJsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return index?.Entries?
            .Where(e => Guid.TryParse(e.Id, out _))
            .ToDictionary(e => Guid.Parse(e.Id), e => e.Order)
            ?? new Dictionary<Guid, int>();
    }

    private async Task WriteEntryOrdersAsync(Dictionary<Guid, int> orders, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_entryMetaDir);
        var index = new EntryOrderIndex
        {
            Entries = orders
                .OrderBy(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Select(pair => new EntryOrderIndexItem(pair.Key.ToString(), pair.Value))
                .ToList()
        };
        var json = JsonSerializer.Serialize(index, MetaJsonOptions);
        await File.WriteAllTextAsync(_entryOrderIndexPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
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
            RepoIds = entry.RepoIds.Count > 0 ? entry.RepoIds.ToList() : null,
            Tags = entry.Tags.Count > 0 ? entry.Tags.ToList() : null,
            SourceInboxId = entry.SourceInboxId,
            CreatedAt = entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            Area = entry.Area,
            DueOn = entry.DueOn?.ToString("O", CultureInfo.InvariantCulture),
            RemindAt = entry.RemindAt?.ToString("O", CultureInfo.InvariantCulture),
            Recurrence = entry.Recurrence is { } recurrence ? EntryTextParser.RepeatToken(recurrence) : null,
            InMyDayOn = entry.InMyDayOn?.ToString("O", CultureInfo.InvariantCulture),
            DependsOn = entry.DependsOn.Count > 0 ? entry.DependsOn.ToList() : null,
            RecurrenceSourceId = entry.RecurrenceSourceId?.ToString(),
            SubItems = entry.SubItems.Count > 0 ? entry.SubItems.Select(s => new SubItemDto
            {
                Id = s.Id.ToString(),
                Title = s.Title,
                Status = EnumMap.ToWire(s.Status),
                Notes = s.Notes,
                Order = s.Order
            }).ToList() : null,
            Projections = entry.ProjectionRefs.Count > 0 ? entry.ProjectionRefs.Select(p => new ProjectionRefDto
            {
                RepoId = p.RepoId,
                ExternalId = p.ExternalId,
                TargetType = p.TargetType
            }).ToList() : null,
            UsageEvents = entry.UsageEvents.Count > 0 ? entry.UsageEvents.Select(u => new UsageEventDto
            {
                Timestamp = u.Timestamp,
                Action = u.Action
            }).ToList() : null
        };

        var yaml = YamlSerializer.Serialize(fm).TrimEnd();
        var sb = new StringBuilder();
        sb.Append("---\n").Append(yaml).Append("\n---\n\n");
        sb.Append(entry.ContentMd);
        if (!entry.ContentMd.EndsWith('\n')) sb.Append('\n');
        return sb.ToString();
    }

    private static BacklogEntry? FromMarkdown(string text, int? orderOverride = null)
    {
        var (yaml, body) = SplitFrontmatter(text);
        if (yaml is null) return null;

        var fm = YamlDeserializer.Deserialize<EntryFrontmatter>(NormalizeCreatedAt(yaml));
        if (fm is null || string.IsNullOrWhiteSpace(fm.Id)) return null;

        var entry = new BacklogEntry(
            Guid.Parse(fm.Id),
            fm.Title,
            body,
            EnumMap.ParseType(fm.Type),
            EnumMap.ParseStatus(fm.Status),
            EnumMap.ParsePriority(fm.Priority),
            fm.RepoIds ?? [],
            fm.Tags ?? [],
            string.IsNullOrWhiteSpace(fm.SourceInboxId) ? null : fm.SourceInboxId,
            ParseCreatedAt(fm.CreatedAt),
            ParseGuid(fm.RecurrenceSourceId));

        entry.SetOrder(orderOverride ?? fm.Order ?? 0);
        entry.SetArea(fm.Area);
        entry.SetDueOn(ParseDate(fm.DueOn));
        entry.SetReminder(ParseLocalDateTime(fm.RemindAt));
        entry.SetRecurrence(EntryTextParser.ParseRepeat(fm.Recurrence));
        entry.SetInMyDayOn(ParseDate(fm.InMyDayOn));
        entry.SetDependsOn(fm.DependsOn ?? []);
        foreach (var s in (fm.SubItems ?? []).OrderBy(s => s.Order))
        {
            var subItem = entry.CreateSubItemForLoad(
                Guid.Parse(s.Id),
                s.Title,
                EnumMap.ParseSubItemStatus(s.Status),
                s.Notes,
                s.Order);
            entry.LoadSubItem(subItem);
        }

        foreach (var p in fm.Projections ?? [])
            entry.AddProjectionRef(new ProjectionRef(p.RepoId, p.ExternalId, p.TargetType));

        foreach (var u in fm.UsageEvents ?? [])
            entry.LoadUsageEvent(new UsageEvent(u.Timestamp, u.Action));

        return entry;
    }

    private static DateTimeOffset ParseCreatedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    /// <summary>Reads a stored calendar date. Invariant, because the file may have
    /// been written on another machine and a date is not the place to find out
    /// what culture that machine was set to.</summary>
    private static DateOnly? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>Reads a stored wall-clock date and time. <c>RoundtripKind</c> keeps
    /// the value Unspecified rather than assuming the local zone, which is what
    /// makes a reminder mean the same clock reading on the next device to open the
    /// file.</summary>
    private static DateTime? ParseLocalDateTime(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment)
                ? moment
                : null;

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;

    private static string NormalizeCreatedAt(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var output = new List<string>(lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!string.Equals(lines[i], "created_at:", StringComparison.Ordinal))
            {
                output.Add(lines[i]);
                continue;
            }

            var value = string.Empty;
            var j = i + 1;
            for (; j < lines.Length && lines[j].StartsWith("  ", StringComparison.Ordinal); j++)
            {
                var trimmed = lines[j].Trim();
                if (trimmed.StartsWith("utc_date_time:", StringComparison.Ordinal))
                {
                    value = trimmed["utc_date_time:".Length..].Trim();
                }
                else if (value.Length == 0 && trimmed.StartsWith("local_date_time:", StringComparison.Ordinal))
                {
                    value = trimmed["local_date_time:".Length..].Trim();
                }
                else if (value.Length == 0 && trimmed.StartsWith("date_time:", StringComparison.Ordinal))
                {
                    value = trimmed["date_time:".Length..].Trim();
                }
            }

            output.Add(value.Length > 0 ? "created_at: " + value : lines[i]);
            i = j - 1;
        }

        return string.Join('\n', output);
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

    private sealed class EntryOrderIndex
    {
        public List<EntryOrderIndexItem>? Entries { get; set; }
    }

    private sealed record EntryOrderIndexItem(string Id, int Order);
}
