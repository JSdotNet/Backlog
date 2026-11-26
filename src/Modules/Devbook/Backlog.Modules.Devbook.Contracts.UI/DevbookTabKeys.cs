namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The keys the pane's rail names its tabs by: a repository folder's area key and a
/// personal tab's key. The chapter link and the area catalog read them as the menu
/// does, which is why they are published here and the menu delegates to them.
/// </summary>
public static class DevbookTabKeys
{
    /// <summary>What every personal tab key starts with, so a tab of the personal
    /// devbook never shares a key with a repository folder of the same name
    /// (<c>domain</c>, <c>design</c>, <c>ai</c>) and none of the pane's
    /// repository special cases reaches it. The value is stored in the pane's
    /// navigation state and stays <c>brain/</c>.</summary>
    public const string PersonalTabPrefix = "brain/";

    /// <summary>The key of the personal tab over <paramref name="folder"/>:
    /// <c>brain/work</c> for <c>work</c>.</summary>
    public static string PersonalTabKey(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return PersonalTabPrefix + folder.ToLowerInvariant();
    }

    /// <summary>Whether <paramref name="tabKey"/> names a personal tab rather
    /// than a repository folder.</summary>
    public static bool IsPersonalTabKey(string? tabKey) =>
        tabKey is not null
        && tabKey.Length > PersonalTabPrefix.Length
        && tabKey.StartsWith(PersonalTabPrefix, StringComparison.Ordinal);

    /// <summary>The area key a repository folder key names.</summary>
    internal static string AreaKey(string folderKey) => folderKey.ToLowerInvariant() switch
    {
        ".domain" => "domain",
        ".arc42" => "arc42",
        ".tech" => "tech",
        ".design" => "design",
        ".ai" => "ai",
        "instructions" => "instructions",
        _ => folderKey.TrimStart('.').ToLowerInvariant()
    };
}

/// <summary>
/// Where the C4 workspace lives beside the arc42 chapters, as the chapter link tells a
/// workspace path from a chapter; <c>C4DevbookStore</c> reads the same names.
/// </summary>
public static class DevbookC4Paths
{
    /// <summary>The one workspace, beside the chapters. devbook's contract 33 makes it
    /// the only place an architecture element or view is defined.</summary>
    public const string WorkspaceFile = "workspace.dsl";

    /// <summary>The only folder the workspace may <c>!include</c> from.</summary>
    public const string IncludeDirectory = Backlog.Infrastructure.Devbook.Building.DevbookC4Includes.Folder;

    /// <summary>The folder that held the workspaces before contract 33 and still holds
    /// the authored view references. Named to sit next to <c>_meta</c>, which is what
    /// puts it out of the way of the chapter listing.</summary>
    public const string ReferenceDirectory = "_c4";
}
