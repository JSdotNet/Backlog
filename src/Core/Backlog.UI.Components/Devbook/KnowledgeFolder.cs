namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// Which knowledge folder a document belongs to. The folder is not decoration:
/// it decides which <c>status</c> vocabulary applies and which metadata fields
/// are meaningful, so a component that knows the folder can flag a typo that a
/// folder-blind one has to render as if it were fine.
/// </summary>
public enum KnowledgeFolder
{
    /// <summary>Not one of the knowledge folders, or not known yet. Everything
    /// still renders — only the vocabulary-aware behaviour stands down.</summary>
    Unknown,
    Arc42,
    Domain,
    Backlog,
    Tech,
    Design
}

/// <summary>
/// Derives <see cref="KnowledgeFolder"/> from a reference path. The folder is
/// always the first segment of a repository-relative path (<c>.tech/shared.md</c>),
/// so nothing has to be told which folder it is looking at as long as it has the
/// path a reference already carries.
/// </summary>
public static class KnowledgeFolders
{
    /// <summary>
    /// Reads the folder out of a path's first segment. Case-insensitive, and
    /// tolerant of the leading <c>/</c> or <c>./</c> that a hand-authored
    /// reference sometimes picks up — those are the same path, and rejecting
    /// them would silently drop a folder that is plainly there.
    /// </summary>
    public static KnowledgeFolder FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return KnowledgeFolder.Unknown;

        var trimmed = path.Trim().Replace('\\', '/');
        if (trimmed.StartsWith("./", StringComparison.Ordinal)) trimmed = trimmed[2..];
        trimmed = trimmed.TrimStart('/');

        var end = trimmed.IndexOf('/');
        var segment = end < 0 ? trimmed : trimmed[..end];

        return segment.ToLowerInvariant() switch
        {
            ".arc42" => KnowledgeFolder.Arc42,
            ".domain" => KnowledgeFolder.Domain,
            ".backlog" => KnowledgeFolder.Backlog,
            ".tech" => KnowledgeFolder.Tech,
            ".design" => KnowledgeFolder.Design,
            _ => KnowledgeFolder.Unknown
        };
    }
}
