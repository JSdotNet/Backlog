using System.Globalization;

using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Mobile.UI.TalkNotes;

/// <summary>
/// What the phone checks about a file before it will queue it: the cap and the
/// allowlist of local ADR 0014, the same defaults the sync service enforces. A
/// file the service would answer 413 or 415 to is refused on its tile, with the
/// reason, rather than parked in the outbox an hour later with an error code.
/// </summary>
public static class AttachmentRules
{
    /// <summary>The most files one note carries — the service's own bound on a
    /// capture's attachment list.</summary>
    public const int MaximumPerNote = 20;

    /// <summary>What a file with no declared type, or the browser's shrug
    /// <c>application/octet-stream</c>, is taken to be by its extension. Only the
    /// allowlisted types, so anything else stays unknown and is refused.</summary>
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".markdown"] = "text/markdown",
        [".csv"] = "text/csv",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "JPEG image",
        ["image/png"] = "PNG image",
        ["image/heic"] = "HEIC image",
        ["image/heif"] = "HEIF image",
        ["image/webp"] = "WebP image",
        ["image/gif"] = "GIF image",
        ["application/pdf"] = "PDF",
        ["text/plain"] = "Text",
        ["text/markdown"] = "Markdown",
        ["text/csv"] = "CSV",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "Word document",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "Excel workbook",
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "PowerPoint deck",
    };

    /// <summary>The media type a file goes up as: the one it declared, unless it
    /// declared nothing useful, in which case the one its extension names.</summary>
    public static string ContentTypeOf(string name, string? declared)
    {
        var mediaType = declared?.Split(';')[0].Trim() ?? string.Empty;

        if (mediaType.Length > 0 && !mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return mediaType.ToLowerInvariant();
        }

        return ByExtension.TryGetValue(Path.GetExtension(name), out var byExtension)
            ? byExtension
            : "application/octet-stream";
    }

    /// <summary>Why the service would not take this file, in words for its
    /// tile — or null when it would.</summary>
    public static string? RefusalOf(string name, string contentType, long sizeBytes)
    {
        if (!SyncAttachmentLimits.AllowsByDefault(contentType))
        {
            var extension = Path.GetExtension(name);
            var what = extension.Length > 0 ? extension.ToLowerInvariant() : contentType;

            return $"Not a type cloud sync takes ({what}). Pictures, PDF, text and Office files only.";
        }

        if (sizeBytes > SyncAttachmentLimits.DefaultMaxBytes)
        {
            return $"Too large: {DescribeSize(sizeBytes)}, and the limit is {DescribeSize(SyncAttachmentLimits.DefaultMaxBytes)}.";
        }

        return null;
    }

    /// <summary>The reason on a tile past <see cref="MaximumPerNote"/>.</summary>
    public static string TooMany => $"A note takes at most {MaximumPerNote} files.";

    /// <summary>A picture, for the "Photo · date" title and the downscaler.</summary>
    public static bool IsPicture(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The type as a person reads it: "PDF", "JPEG image".</summary>
    public static string Describe(string contentType) =>
        Labels.TryGetValue(contentType, out var label) ? label : contentType;

    /// <summary>"340 KB", "4.2 MB" — binary units, one decimal from a megabyte up.</summary>
    public static string DescribeSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(bytes / 1024.0):0} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
    };
}
