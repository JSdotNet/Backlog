using Backlog.Modules.Backlog.Abstractions;
using Backlog.UI.Components.Markdown;

namespace Backlog.Desktop.UI.BacklogManagement;

/// <summary>
/// The domain half of a sub-item's metadata line. The markdown parser lives in
/// the shared component library and must not know what an <see cref="EntryType"/>
/// is, so it hands the line to this reader and carries the result back as an
/// opaque payload.
/// </summary>
public sealed record EntryMarkdownMetadata(EntryType? Type, Priority? Priority, EntryStatus? Status);

internal sealed class EntryMarkdownMetadataReader : IMarkdownMetadataReader
{
    public static EntryMarkdownMetadataReader Instance { get; } = new();

    public bool IsMetadataLine(string line) => EntryTextParser.IsMetadataLine(line);

    public MarkdownMetadata Read(string line)
    {
        // The parser only understands a metadata line under a heading, so give it
        // one; the heading itself is thrown away.
        var parsed = EntryTextParser.Parse("# Metadata\n" + line);
        var status = parsed.Status;

        return new MarkdownMetadata(
            new EntryMarkdownMetadata(parsed.Type, parsed.Priority, status),
            status is EntryStatus.Done,
            parsed.MetadataTags);
    }
}

public static class EntryMarkdownMetadataExtensions
{
    public static EntryMarkdownMetadata? EntryMetadata(this MdSubItem subItem) =>
        subItem.Metadata?.Value as EntryMarkdownMetadata;
}
