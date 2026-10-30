using Backlog.Modules.Devbook.Abstractions;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Devbook;

public static class DevbookAreaCatalog
{
    /// <summary>The instructions section's key. Not a devbook folder: it reads
    /// the agent instruction files, so a branch changing only those changes no
    /// devbook section.</summary>
    public const string InstructionsKey = "instructions";

    private static readonly DevbookArea[] Areas =
    [
        new("instructions", "Instructions"),
        new("domain", "Domain"),
        new("arc42", "Architecture"),
        new("tech", "Technology"),
        new("design", "Design"),
        new("ai", "AI")
    ];

    public static IReadOnlyList<DevbookArea> VisibleAreas(IEnumerable<DevbookFolderSetting> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var enabledAreaKeys = DevbookFolderSetting.Normalize(folders)
            .Where(folder => folder.Enabled)
            .Select(AreaKeyFor)
            .ToHashSet(StringComparer.Ordinal);

        return [.. Areas.Where(area => enabledAreaKeys.Contains(area.Key))];
    }

    /// <summary>
    /// The section a repository-relative knowledge path belongs to —
    /// <c>.devbook/domain/tasks/domain.md</c> is the Domain section — or
    /// <see langword="null"/> when the folder it names is not a section this
    /// product reads.
    /// <para>
    /// The leading segment is a devbook folder key, so it goes through the same
    /// folder-key-to-section map the Devbook menu is built from rather than
    /// through a second one written here; the answer is then only handed back when
    /// it names a section on this list. A reference into <c>.github</c> resolves to
    /// nothing, which is the honest answer: there is no section to send a reader
    /// to, and a control that claimed otherwise would be a control that goes
    /// nowhere.
    /// </para>
    /// </summary>
    public static string? AreaKeyForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        // The same leniency DevbookFolders.FromPath allows, and for the same
        // reason: a hand-authored reference picks up a leading `./` or `/` often
        // enough, and those spell the same file. The devbook layout's
        // .devbook/domain/… is the Domain section as much as .domain/… is.
        var trimmed = DevbookLayout.ConventionalPath(path);

        var end = trimmed.IndexOf('/');
        if (end <= 0) return null;

        var areaKey = DevbookMenu.AreaKey(trimmed[..end]);
        return Areas.Any(area => string.Equals(area.Key, areaKey, StringComparison.Ordinal)) ? areaKey : null;
    }

    /// <summary>The folder-source key for a section — the reverse of the mapping
    /// below, for a caller that holds an area key and needs to resolve its
    /// folder. A key this catalog does not know is handed back as it came, which
    /// is what the folder source expects of a folder configured under its own
    /// name.</summary>
    public static string FolderKey(string areaKey) => areaKey.ToLowerInvariant() switch
    {
        "domain" => ".domain",
        "arc42" => ".arc42",
        "tech" => ".tech",
        "design" => ".design",
        "ai" => ".ai",
        "instructions" => "instructions",
        _ => areaKey
    };

    /// <summary>Where a section sits in the tab order, so a list of sections reads
    /// the way the tabs do. A key this catalog does not know goes last.</summary>
    public static int Order(string areaKey)
    {
        var index = Array.FindIndex(Areas, area => string.Equals(area.Key, areaKey, StringComparison.Ordinal));
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>The label a section's tab wears, or the key itself for one this
    /// catalog does not know.</summary>
    public static string Label(string areaKey) =>
        Array.Find(Areas, area => string.Equals(area.Key, areaKey, StringComparison.Ordinal))?.Label ?? areaKey;

    /// <summary>The section a configured folder is.</summary>
    internal static string AreaKeyFor(DevbookFolderSetting folder) => folder.Key switch
    {
        "instructions" => "instructions",
        ".domain" => "domain",
        ".arc42" => "arc42",
        ".tech" => "tech",
        ".design" => "design",
        ".ai" => "ai",
        _ => folder.Key.TrimStart('.').ToLowerInvariant()
    };
}

public sealed record DevbookArea(string Key, string Label);
