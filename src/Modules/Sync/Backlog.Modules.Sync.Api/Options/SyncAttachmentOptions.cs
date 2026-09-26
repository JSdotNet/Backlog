using System.ComponentModel.DataAnnotations;

namespace Backlog.Modules.Sync.Api.Options;

/// <summary>
/// The two limits local ADR 0014 puts on an upload, both settings rather than
/// constants so "any file" in the owner's sense is served by widening a
/// setting, not by a redeploy of changed code (inherited ADR 0018: bind,
/// validate, fail fast).
/// </summary>
public sealed class SyncAttachmentOptions
{
    /// <summary>The section this binds to, named for the module that owns
    /// it.</summary>
    public const string SectionName = "Modules:Sync:Attachments";

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

    /// <summary>The most one attachment may weigh: 25 MB by default. Bounded
    /// above so a typo cannot open the service to uploads the container app
    /// would hold a connection open for minutes to receive.</summary>
    [Range(1, 256L * 1024 * 1024)]
    public long MaxBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>
    /// The media types an upload may declare, compared without their
    /// parameters and without regard to case. Empty means
    /// <see cref="DefaultContentTypes"/>.
    /// <para>
    /// A list that replaces the default rather than adding to it, and empty
    /// rather than pre-filled for the reason that makes that possible: the
    /// configuration binder appends a configured array to one the property
    /// already holds, so a default written into the initializer could never be
    /// narrowed by configuration, only added to.
    /// </para>
    /// </summary>
    public string[] AllowedContentTypes { get; set; } = [];

    /// <summary>Whether <paramref name="mediaType"/> — the declared type with
    /// its parameters removed — is on the list in force.</summary>
    public bool Allows(string mediaType)
    {
        IReadOnlyList<string> allowed = AllowedContentTypes.Length > 0 ? AllowedContentTypes : DefaultContentTypes;

        return allowed.Any(type => string.Equals(type.Trim(), mediaType, StringComparison.OrdinalIgnoreCase));
    }
}
