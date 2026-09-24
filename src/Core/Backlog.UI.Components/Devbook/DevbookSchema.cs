namespace Backlog.UI.Components.Devbook;

/// <summary>
/// The devbook metadata contract the product reads and writes against — contract
/// 16 of the <c>devbook</c> plugin's <c>devbook-chapter-metadata.md</c>,
/// <c>devbook-domain.md</c> and <c>devbook-annotations.md</c>.
///
/// <para>One place for the facts every reader and writer has to agree on: which
/// folders define a <c>type</c> vocabulary and what it is, which folders rest at
/// <c>active</c> by leaving <c>status</c> out, which fields are decision and review
/// <em>state</em> rather than chapter content, and which keys belong to an
/// extension rather than to the schema. The folders' own classes —
/// <see cref="DevbookStatus"/>, <see cref="DevbookTypeMarkers"/> — draw from here
/// rather than keeping copies, and <c>DevbookRuleTextContractTests</c> pins every
/// list against the rule text itself, so a contract bump that changes a word fails
/// a test instead of quietly disagreeing with CI.</para>
///
/// <para>Nothing here writes a file. It answers questions; the writers ask them
/// before they write.</para>
/// </summary>
public static class DevbookSchema
{
    /// <summary>The contract version these lists were taken from.</summary>
    public const int ContractVersion = 16;

    /// <summary>The value an editorial folder rests at, spelled by omitting the
    /// field.</summary>
    public const string RestingStatus = "active";

    /// <summary>The two decision rungs on top of <c>domain/</c>'s ladder, in the
    /// order they are climbed. No other folder has them.</summary>
    public static IReadOnlyList<string> DecisionRungs { get; } = ["approved", "accepted"];

    /// <summary>The approval record: written with <c>status: approved</c>, deleted
    /// with it.</summary>
    public static IReadOnlyList<string> ApprovalRecordFields { get; } = ["approved-by", "approved-at", "approved-hash"];

    /// <summary>The acceptance record: written with <c>status: accepted</c> beside
    /// the approval record it stands on, deleted with it.</summary>
    public static IReadOnlyList<string> AcceptanceRecordFields { get; } = ["accepted-by", "accepted-at", "accepted-hash"];

    /// <summary>The six record fields, approval first.</summary>
    public static IReadOnlyList<string> DecisionRecordFields { get; } = [.. ApprovalRecordFields, .. AcceptanceRecordFields];

    /// <summary>The review triad: who owes the next move on the way to a decision.
    /// Deleted in the same change that writes approval.</summary>
    public static IReadOnlyList<string> ReviewFields { get; } = ["review", "reviewer", "review-at"];

    /// <summary>The values <c>review</c> may take.</summary>
    public static IReadOnlyList<string> ReviewStates { get; } = ["requested", "changes-requested", "cleared"];

    /// <summary>
    /// The nine fields that are decision or review <em>state</em>, not chapter
    /// content. A reader loading a chapter for context skips them; a view draws
    /// them beside the status and never as rows in the body.
    /// </summary>
    public static IReadOnlyList<string> StateFields { get; } = [.. DecisionRecordFields, .. ReviewFields];

    /// <summary>The two ways a bounded context ships.</summary>
    public static IReadOnlyList<string> DeploymentValues { get; } = ["service", "module"];

    /// <summary>The values <c>index</c> may take on a file-level block.</summary>
    public static IReadOnlyList<string> IndexValues { get; } = ["root", "exclude"];

    /// <summary>The prefix of every extension key, <c>ext.&lt;plugin&gt;.&lt;key&gt;</c>.
    /// Carried through untouched and never validated.</summary>
    public const string ExtensionPrefix = "ext.";

    /// <summary><c>tech/</c>'s old spelling of <c>type</c>. Still read, and
    /// reported.</summary>
    public const string LegacyTechTypeField = "kind";

    private static readonly string[] DomainChapterTypes =
    [
        "bounded-context", "aggregate", "entity", "value-object", "enum",
        "shared-value-objects", "shared-enums", "ubiquitous-language",
        "domain-service", "domain-event", "feature", "sub-feature",
        "requirements", "requirement", "invariants", "invariant",
        "feature-flag", "setting", "user", "organisation", "technical", "term"
    ];

    private static readonly string[] DomainFileTypes =
    [
        "context-map", "context", "domain", "actors", "features", "skills",
        "requirements", "invariants", "model", "flow", "dependencies"
    ];

    private static readonly string[] TechChapterTypes =
    [
        "language", "runtime", "framework", "library", "package",
        "tool", "service", "platform", "protocol", "format"
    ];

    private static readonly string[] AiChapterTypes =
    [
        "practice", "agent", "skill", "plugin", "mcp-server",
        "hook", "workflow", "model", "concept", "guardrail"
    ];

    private static readonly string[] AiFileTypes = ["adoption-map", "stage", "concepts"];

    /// <summary>Whether the folder defines a <c>type</c> value set at all.
    /// <c>arc42/</c> and <c>design/</c> deliberately do not, and a <c>type</c>
    /// written there is reported as a warning.</summary>
    public static bool DefinesTypes(DevbookFolder folder) =>
        folder is DevbookFolder.Domain or DevbookFolder.Tech or DevbookFolder.Ai;

    /// <summary>The chapter-level <c>type</c> values, in the rule's order.</summary>
    public static IReadOnlyList<string> ChapterTypes(DevbookFolder folder) => folder switch
    {
        DevbookFolder.Domain => DomainChapterTypes,
        DevbookFolder.Tech => TechChapterTypes,
        DevbookFolder.Ai => AiChapterTypes,
        _ => []
    };

    /// <summary>The file-level <c>type</c> values, in the rule's order. A
    /// <c>domain/</c> context's additional page adds its own filename to this set;
    /// see <see cref="IsKnownType"/>.</summary>
    public static IReadOnlyList<string> FileTypes(DevbookFolder folder) => folder switch
    {
        DevbookFolder.Domain => DomainFileTypes,
        DevbookFolder.Ai => AiFileTypes,
        _ => []
    };

    /// <summary>
    /// Whether <paramref name="value"/> is a <c>type</c> this folder allows at this
    /// level.
    ///
    /// <para>At file level in <c>domain/</c> the set is open by exactly one value:
    /// a page the convention does not name is a file like any other, and its
    /// <c>type</c> is its own filename. So the filename is passed in, and a
    /// <c>regulatory-annex.md</c> stating <c>type: regulatory-annex</c> is known
    /// while one stating <c>type: aggregate</c> is not. A split file carries the
    /// type of the file it came from (<c>domain.order.md</c> is <c>domain</c>),
    /// which the closed set already covers.</para>
    /// </summary>
    public static bool IsKnownType(DevbookFolder folder, DevbookMetadataLevel level, string? value, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalised = value.Trim();
        var set = level == DevbookMetadataLevel.File ? FileTypes(folder) : ChapterTypes(folder);
        if (set.Contains(normalised, StringComparer.OrdinalIgnoreCase)) return true;

        return folder == DevbookFolder.Domain
            && level == DevbookMetadataLevel.File
            && OwnFileType(fileName) is { } own
            && string.Equals(own, normalised, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The file-level <c>type</c> an additional <c>domain/</c> page would
    /// state: its filename without the extension. Null for no name.</summary>
    public static string? OwnFileType(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var name = fileName.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];

        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
    }

    /// <summary>Whether the folder requires <c>status</c> — the two rating folders,
    /// where an absent value is not the bottom rung.</summary>
    public static bool RequiresStatus(DevbookFolder folder) =>
        folder is DevbookFolder.Tech or DevbookFolder.Ai;

    /// <summary>Whether the folder rests at <see cref="RestingStatus"/> by leaving
    /// the field out: <c>arc42/</c>, <c>domain/</c> and <c>design/</c>.</summary>
    public static bool RestsByOmission(DevbookFolder folder) =>
        folder is DevbookFolder.Arc42 or DevbookFolder.Domain or DevbookFolder.Design;

    /// <summary>Whether writing <paramref name="status"/> in this folder means
    /// deleting the line: the resting value, in a folder that rests by
    /// omission.</summary>
    public static bool IsResting(DevbookFolder folder, string? status) =>
        RestsByOmission(folder)
        && status is not null
        && string.Equals(status.Trim(), RestingStatus, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the decision rungs, their six record fields, are legal in
    /// this folder. <c>domain/</c> alone.</summary>
    public static bool AllowsDecisionRungs(DevbookFolder folder) => folder == DevbookFolder.Domain;

    /// <summary>Whether the status is one of the two decision rungs.</summary>
    public static bool IsDecisionRung(string? status) =>
        status is not null && DecisionRungs.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a (lower-cased) key is one of the nine state
    /// fields.</summary>
    public static bool IsStateField(string? key) =>
        key is not null && StateFields.Contains(key.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a key is an extension key, owned by a plugin and opaque
    /// here.</summary>
    public static bool IsExtensionKey(string? key) =>
        key is not null && key.TrimStart().StartsWith(ExtensionPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a bounded context's two <c>deployment</c> statements disagree — the
    /// <c>bounded-context</c> chapter in <c>context-map.md</c> and the file-level
    /// block of its <c>context.md</c>. A value on one side only is a disagreement
    /// too; neither side stating one is not.
    /// </summary>
    public static bool DeploymentDisagrees(string? chapterValue, string? contextValue)
    {
        var chapter = string.IsNullOrWhiteSpace(chapterValue) ? null : chapterValue.Trim();
        var context = string.IsNullOrWhiteSpace(contextValue) ? null : contextValue.Trim();
        if (chapter is null && context is null) return false;

        return !string.Equals(chapter, context, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Which block a metadata record came from: a file's, under its
/// <c>#</c> title, or a chapter's, under any other heading.</summary>
public enum DevbookMetadataLevel
{
    Chapter,
    File
}
