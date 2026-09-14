namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// One row of <c>node</c>: a chapter, a file, or a reference target outside the
/// knowledge folders altogether.
/// </summary>
/// <param name="Id">The chapter reference, <c>&lt;path&gt;#&lt;slug&gt;</c> for a
/// chapter and the path alone for a file — the same opaque string a
/// <c>related</c> or <c>depends-on</c> field carries, so a reference and a node
/// line up without either side parsing the other.</param>
/// <param name="Folder">The folder kind (<c>arc42</c>, <c>domain</c>) or
/// <see langword="null"/> for a node outside the knowledge folders.</param>
/// <param name="OutOfScope">Set by the writer for a node with no folder. It does
/// not mean what a projected graph means by the word — see
/// <see cref="KnowledgeScopeProjection"/>, which decides that per scope.</param>
public sealed record KnowledgeNodeRow(
    string Id,
    string Type,
    string? Label,
    string? Folder,
    string? Path,
    string? Slug,
    int? Level,
    int? Line,
    string? Status,
    bool OutOfScope,
    int? Effort,
    string? Kind,
    string? Version,
    string? Issue);

/// <summary>One row of <c>edge</c>. <c>Id</c> is a label composed from the two
/// endpoints and the type, and nothing joins on it.</summary>
public sealed record KnowledgeEdgeRow(string Id, string Type, string Source, string Target);

/// <summary>One row of <c>node_attribute</c> — a list-valued field of a
/// <c>meta</c> block flattened to one row per value.</summary>
public sealed record KnowledgeNodeAttributeRow(string NodeId, string Name, string Value);

/// <summary>
/// One row of <c>outline_entry</c>: a line of the resolved reading outline, with
/// the authored order already applied.
/// </summary>
/// <param name="ParentId">The row this one sits under, or <see langword="null"/>
/// at the top of its scope.</param>
/// <param name="Kind">The folder kind this entry belongs to, carried on every row
/// so a reader can filter without walking back up to the area above it.</param>
public sealed record KnowledgeOutlineRow(
    long Id,
    string Scope,
    long? ParentId,
    int Ordinal,
    string Type,
    string Name,
    string Path,
    string? Title,
    string? Status,
    string? Kind,
    bool IsRoot);

/// <summary>
/// One row of <c>chapter</c>: a heading and the text belonging to it, plus the
/// file-level facts the drift check compares against.
/// </summary>
/// <param name="ContentHash">Lowercase hex SHA-256 of this chapter's own slice.</param>
/// <param name="SourceHash">Lowercase hex SHA-256 of the whole file's bytes,
/// repeated on every chapter of that file so one <c>stat</c> answers for all of
/// them at once.</param>
/// <param name="Mtime">Milliseconds since the Unix epoch.</param>
public sealed record KnowledgeChapterRow(
    string Path,
    string? Folder,
    string Slug,
    int Level,
    string? Title,
    string? Status,
    int Line,
    string Text,
    string ContentHash,
    string SourceHash,
    long Size,
    long Mtime);

/// <summary>
/// One row of <c>archify_artifact</c>: which rendered diagram belongs to which
/// mermaid fence, keyed by the SHA-256 of the normalised fence.
/// </summary>
/// <param name="ChapterPath">Repository-relative, <c>/</c>-separated.</param>
/// <param name="SpecPath">Repository-relative path of the hand-authored
/// specification, or <see langword="null"/>.</param>
/// <param name="ChecksPassed">Carried because ADR 0004 lists it. Nothing reads it
/// yet, and inventing a use for it is not this reader's business.</param>
public sealed record KnowledgeArchifyArtifactRow(
    string ChapterPath,
    string FenceHash,
    int Ordinal,
    string? Type,
    string? Quality,
    string? Kind,
    string? SpecPath,
    string? ArtifactPath,
    int? ChecksPassed,
    int? CheckCount);

/// <summary>One row of <c>problem</c> — something the generator could not
/// resolve, recorded rather than thrown.</summary>
public sealed record KnowledgeProblemRow(string Scope, string Severity, string? Path, string Message);
