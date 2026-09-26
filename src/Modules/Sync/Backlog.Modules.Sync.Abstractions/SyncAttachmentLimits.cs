namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// The defaults local ADR 0014 puts on an upload: the per-file cap and the
/// content-type allowlist. Written down here, beside the routes, because both
/// ends read them — the service enforces them (as the defaults of its
/// <c>SyncAttachmentOptions</c> setting) and the phone refuses a file on its
/// tile before it ever queues one the service would answer 413 or 415 to.
/// </summary>
public static class SyncAttachmentLimits
{
    /// <summary>The most one attachment may weigh by default: 25 MB.</summary>
    public const long DefaultMaxBytes = 25L * 1024 * 1024;

    /// <summary>
    /// The allowlist when the setting names none: images, PDF, plain text,
    /// Markdown and CSV, and the Office Open XML document, spreadsheet and
    /// presentation types. It exists to refuse executables, scripts and HTML,
    /// not to enumerate every useful format.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/heic",
        "image/heif",
        "image/webp",
        "image/gif",
        "application/pdf",
        "text/plain",
        "text/markdown",
        "text/csv",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    ];

    /// <summary>Whether <paramref name="mediaType"/> — without its parameters —
    /// is on <see cref="DefaultContentTypes"/>, ignoring case.</summary>
    public static bool AllowsByDefault(string? mediaType) =>
        !string.IsNullOrWhiteSpace(mediaType)
        && DefaultContentTypes.Contains(mediaType.Split(';')[0].Trim(), StringComparer.OrdinalIgnoreCase);
}
