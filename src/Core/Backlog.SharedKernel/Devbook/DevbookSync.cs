namespace Backlog.SharedKernel.Devbook;

/// <summary>
/// The <c>sync</c> field of devbook contract 25 — which way changes flow between a
/// chapter and the code that implements it, for the sweeps that keep the two
/// aligned. The rule is the "Sync direction" section of
/// <c>.agents/rules/devbook-chapter-metadata.md</c>; the checker it mirrors is
/// <c>syncLevel</c>, <c>syncSources</c> and <c>syncIssues</c> in the vendored
/// <c>.devbook/_tools/devbook-meta/metadata.mjs</c>.
///
/// <para>A direction is stated on one of four levels — a folder overview, a
/// <c>context.md</c>, a context page, a unit's root chapter — and a unit resolves
/// it <b>nearest wins</b>: its own chapter, its page, its context, its folder, and
/// <see cref="DefaultDirection"/> when none of them states one. Every other block
/// refuses the field, the chapters a unit owns most of all: a direction of their
/// own would let half a unit go one way and half the other.</para>
///
/// <para>Nothing here reads a file. <see cref="Place"/> answers where a block sits
/// from facts the caller already has, and <see cref="Resolve"/> picks the winning
/// statement out of the ones the caller gathered.</para>
/// </summary>
public static class DevbookSync
{
    /// <summary>The values <c>sync</c> may take, in the rule's order.</summary>
    public static IReadOnlyList<string> Directions { get; } = ["push", "pull", "sync", "report", "off"];

    /// <summary>The direction of a unit nothing above it sets: report drift,
    /// write nothing.</summary>
    public const string DefaultDirection = "report";

    /// <summary>The <c>domain/</c> chapter types that root a unit.</summary>
    public static IReadOnlyList<string> UnitTypes { get; } = ["aggregate", "domain-service", "feature", "feature-flag", "setting"];

    /// <summary>The chapter types a unit owns. They follow its direction and
    /// refuse one of their own.</summary>
    public static IReadOnlyList<string> OwnedTypes { get; } =
        ["entity", "value-object", "enum", "domain-event", "invariant", "requirement", "sub-feature", "term"];

    /// <summary>The context pages whose file-level block sets a default for the
    /// units on them, by the filename's first segment. <c>actors</c> holds no unit
    /// yet; a value there is allowed and inherited by nothing.</summary>
    public static IReadOnlyList<string> PageBases { get; } = ["domain", "features", "skills", "actors"];

    /// <summary>The pages that hold only chapters a unit on another page owns.</summary>
    public static IReadOnlyList<string> OwnedPageBases { get; } = ["requirements", "invariants"];

    /// <summary>The folder overview whose file-level block sets a folder's
    /// default. Null for a folder that takes no direction at all.</summary>
    public static string? FolderOverview(DevbookFolder folder) => folder switch
    {
        DevbookFolder.Domain => "context-map.md",
        DevbookFolder.Arc42 => "05-building-block-view.md",
        DevbookFolder.Design => "component-libraries.md",
        _ => null
    };

    /// <summary>Whether <paramref name="value"/> is one of <see cref="Directions"/>.</summary>
    public static bool IsDirection(string? value) =>
        value is not null && Directions.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where a <c>sync</c> value on this block would sit: a level, or why the block
    /// refuses one.
    /// </summary>
    /// <param name="folder">The devbook folder the block was read from.</param>
    /// <param name="path">The file, relative to the folder's own root —
    /// <c>tasks/domain.md</c>, <c>building-blocks/sync.md</c>. A repository-relative
    /// path is accepted too: everything up to and including the folder's name is
    /// dropped.</param>
    /// <param name="level">The file's own block, or a chapter's.</param>
    /// <param name="type">The block's resolved <c>type</c>, or null.</param>
    /// <param name="headingLevel">The chapter's heading depth; <c>design/</c>
    /// needs it, since only a <c>##</c> chapter of <c>component-libraries.md</c> is
    /// a component.</param>
    /// <param name="indexRoot">Whether the block states <c>index: root</c>, which
    /// makes an <c>arc42/building-blocks/</c> file its directory's entry point
    /// rather than a building block.</param>
    public static DevbookSyncPlace Place(
        DevbookFolder folder,
        string path,
        DevbookMetadataLevel level,
        string? type,
        int headingLevel,
        bool indexRoot = false)
    {
        if (FolderOverview(folder) is not { } overview) return DevbookSyncPlace.Refused(DevbookSyncRefusal.NoLevel);

        var subject = FolderRelative(folder, path);
        var file = level == DevbookMetadataLevel.File;
        var kind = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();

        switch (folder)
        {
            case DevbookFolder.Domain:
            {
                if (subject == overview) return file ? DevbookSyncPlace.At(DevbookSyncLevel.Folder) : DevbookSyncPlace.Refused(DevbookSyncRefusal.NoLevel);
                if (subject.Split('/').Length != 2) return DevbookSyncPlace.Refused(DevbookSyncRefusal.NoLevel);

                var pageBase = PageBase(subject);
                if (OwnedPageBases.Contains(pageBase)) return DevbookSyncPlace.Refused(DevbookSyncRefusal.OwnedPage);
                if (file)
                {
                    if (pageBase == "context") return DevbookSyncPlace.At(DevbookSyncLevel.Context);
                    return PageBases.Contains(pageBase) ? DevbookSyncPlace.At(DevbookSyncLevel.Page) : DevbookSyncPlace.Refused(DevbookSyncRefusal.NoLevel);
                }

                if (kind is not null && UnitTypes.Contains(kind)) return DevbookSyncPlace.At(DevbookSyncLevel.Unit);
                return kind is not null && OwnedTypes.Contains(kind)
                    ? DevbookSyncPlace.Refused(DevbookSyncRefusal.Owned)
                    : DevbookSyncPlace.Refused(DevbookSyncRefusal.NoLevel);
            }

            case DevbookFolder.Arc42:
            {
                if (!file) return DevbookSyncPlace.Refused(DevbookSyncRefusal.NoLevel);
                if (subject == overview) return DevbookSyncPlace.At(DevbookSyncLevel.Folder);

                var parts = subject.Split('/');
                var isBlock = parts.Length == 2 && parts[0] == "building-blocks" && parts[1].EndsWith(".md", StringComparison.Ordinal) && !indexRoot;
                return isBlock ? DevbookSyncPlace.At(DevbookSyncLevel.Unit) : DevbookSyncPlace.Refused(DevbookSyncRefusal.NoLevel);
            }

            default:
            {
                if (subject != overview) return DevbookSyncPlace.Refused(DevbookSyncRefusal.NoLevel);
                if (file) return DevbookSyncPlace.At(DevbookSyncLevel.Folder);
                if (kind == "requirement") return DevbookSyncPlace.Refused(DevbookSyncRefusal.Owned);

                return headingLevel == 2 && kind is null
                    ? DevbookSyncPlace.At(DevbookSyncLevel.Unit)
                    : DevbookSyncPlace.Refused(DevbookSyncRefusal.NoLevel);
            }
        }
    }

    /// <summary>
    /// A unit's direction, nearest wins: the first statement that is one of
    /// <see cref="Directions"/>, and <see cref="DefaultDirection"/> from no level
    /// when none is. A value outside the set is skipped, as the sweeps skip it —
    /// the checker reports it, and a typo on a page must not stop the context's
    /// direction reaching the units under it.
    /// </summary>
    /// <param name="nearestFirst">What each level states, the unit's own chapter
    /// first and the folder overview last; a level that states nothing is null or
    /// left out.</param>
    public static DevbookSyncDirection Resolve(IEnumerable<(DevbookSyncLevel Level, string? Value)> nearestFirst)
    {
        foreach (var (level, value) in nearestFirst)
        {
            if (IsDirection(value)) return new DevbookSyncDirection(value!.Trim().ToLowerInvariant(), level);
        }

        return new DevbookSyncDirection(DefaultDirection, null);
    }

    /// <summary>The file's path below the folder's own root, with forward
    /// slashes: <c>.devbook/domain/tasks/domain.md</c> and <c>tasks/domain.md</c>
    /// both give <c>tasks/domain.md</c>.</summary>
    public static string FolderRelative(DevbookFolder folder, string path)
    {
        var normalised = (path ?? string.Empty).Replace('\\', '/').Split('#', 2)[0].TrimStart('/');
        var name = folder.ToString().ToLowerInvariant();
        foreach (var prefix in (string[])[$".devbook/{name}/", $".{name}/", $"{name}/"])
        {
            if (normalised.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return normalised[prefix.Length..];
        }

        return normalised;
    }

    /// <summary>A <c>domain/</c> file's base: its name's first dot-segment, and
    /// <c>invariants</c> for any <c>*.invariants.md</c>.</summary>
    private static string PageBase(string subject)
    {
        var name = subject[(subject.LastIndexOf('/') + 1)..];
        if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) name = name[..^3];

        var segments = name.Split('.');
        return (segments.Length > 1 && segments[^1] == "invariants" ? "invariants" : segments[0]).ToLowerInvariant();
    }
}

/// <summary>The four levels a direction is stated on, nearest first.</summary>
public enum DevbookSyncLevel
{
    Unit,
    Page,
    Context,
    Folder
}

/// <summary>Why a block refuses <c>sync</c>.</summary>
public enum DevbookSyncRefusal
{
    /// <summary>The block is no level: not a folder overview, a <c>context.md</c>,
    /// a context page, or a unit's root chapter.</summary>
    NoLevel,

    /// <summary>A chapter a unit owns — it follows the unit.</summary>
    Owned,

    /// <summary>A page that holds only chapters a unit on another page owns.</summary>
    OwnedPage
}

/// <summary>Where a <c>sync</c> value on a block would sit: exactly one of
/// <see cref="Level"/> and <see cref="Refusal"/> is set.</summary>
public readonly record struct DevbookSyncPlace(DevbookSyncLevel? Level, DevbookSyncRefusal? Refusal)
{
    /// <summary>Whether the block may state <c>sync</c>.</summary>
    public bool IsAllowed => Level is not null;

    public static DevbookSyncPlace At(DevbookSyncLevel level) => new(level, null);

    public static DevbookSyncPlace Refused(DevbookSyncRefusal refusal) => new(null, refusal);
}

/// <summary>A resolved direction and the level it was read from — null when no
/// level states one and the default applies.</summary>
public sealed record DevbookSyncDirection(string Direction, DevbookSyncLevel? From)
{
    /// <summary>Whether no level stated a direction.</summary>
    public bool IsDefault => From is null;
}

/// <summary>
/// What a block that may state <c>sync</c> shows: where it sits, what it states
/// itself, and the direction in force there — its own, or what it inherits.
/// </summary>
/// <param name="Level">The level the block is.</param>
/// <param name="Stated">The block's own value, as written, or null.</param>
/// <param name="Effective">The direction in force on this block. For a unit it is
/// the unit's direction; for a page, a context or a folder overview, the default it
/// hands down.</param>
public sealed record DevbookSyncState(DevbookSyncLevel Level, string? Stated, DevbookSyncDirection Effective);
