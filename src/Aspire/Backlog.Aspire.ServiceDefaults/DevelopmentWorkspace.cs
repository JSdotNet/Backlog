using System.Text.Json;

namespace Backlog.Aspire.ServiceDefaults;

/// <summary>
/// Which checkout this process was started from, so a development window or
/// browser tab says which worktree it belongs to.
/// <para>
/// Several worktrees of this repository are routinely run side by side, and
/// every one of them names itself the same thing: the desktop window says
/// "Backlog.Desktop" and the harness tabs say "Backlog", "Backlog Mobile
/// (browser harness)" and "Backlog UI Storybook". The marker is what tells
/// them apart, and it is derived rather than configured because there is
/// nothing for somebody to set: the folder the process runs from already
/// answers the question.
/// </para>
/// <para>
/// It lives here because <c>Backlog.Aspire.ServiceDefaults</c> is the only
/// project the MAUI head and all three harnesses share — the storybook is
/// allowed no other reference besides the component library.
/// </para>
/// </summary>
public static class DevelopmentWorkspace
{
    private const string GitDirectoryPrefix = "gitdir:";
    private const string HeadReferencePrefix = "ref:";
    private const string BranchReferencePrefix = "refs/heads/";

    // Reading the folder chain and HEAD is cheap, but a title is asked for on
    // every render of a harness page, so the answer is worked out once.
    private static readonly Lazy<string?> Derived = new(() => Describe(AppContext.BaseDirectory));

    /// <summary>
    /// The marker for the checkout this process runs from — a worktree folder
    /// name, with the branch beside it when the branch does not already say the
    /// same thing — or <c>null</c> when the process is not running from one.
    /// </summary>
    public static string? Current => Derived.Value;

    /// <summary>
    /// <paramref name="title"/> with the marker in front of it, or unchanged
    /// when there is no marker. The marker leads rather than trails because a
    /// browser tab and a taskbar button both truncate the end, which is exactly
    /// where a trailing marker would sit.
    /// </summary>
    public static string DecorateTitle(string title) => DecorateTitle(title, Current);

    /// <summary>
    /// The script a browser host puts in its head so the marker survives a page
    /// setting its own <c>PageTitle</c> — which the storybook does on every page
    /// and the mobile inbox does on its one. Empty when there is no marker.
    /// </summary>
    public static string TitleScript => BuildTitleScript(Current);

    internal static string DecorateTitle(string title, string? marker) =>
        marker is null ? title : $"[{marker}] {title}";

    /// <summary>
    /// Derives the marker for the checkout <paramref name="startDirectory"/>
    /// sits in, by walking up to the nearest <c>.git</c>.
    /// </summary>
    internal static string? Describe(string? startDirectory)
    {
        var root = FindCheckoutRoot(startDirectory);
        if (root is null)
        {
            return null;
        }

        var branch = ReadBranch(root);
        if (branch is null)
        {
            return root.Name;
        }

        // A worktree is normally created from the branch it holds, so the two
        // usually repeat each other. Saying it twice would only make the title
        // longer than the tab that has to show it.
        var lastSegment = branch[(branch.LastIndexOf('/') + 1)..];
        return string.Equals(lastSegment, root.Name, StringComparison.OrdinalIgnoreCase)
            ? root.Name
            : $"{root.Name} ({branch})";
    }

    internal static string BuildTitleScript(string? marker)
    {
        if (marker is null)
        {
            return string.Empty;
        }

        // The marker is a folder and a branch name, so it reaches the page as a
        // JSON literal rather than as Razor interpolation: escaping it once here
        // is safer than trusting three .razor files with the same string.
        var literal = JsonSerializer.Serialize(marker);

        return $$"""
            (function () {
                const prefix = "[" + {{literal}} + "] ";
                const apply = () => {
                    if (!document.title.startsWith(prefix)) {
                        document.title = prefix + document.title;
                    }
                };
                apply();
                // Blazor renders the page's own PageTitle into the head, so the
                // title this runs against is replaced rather than kept. Watching
                // the whole head catches both a new title element and an edit to
                // the one already there.
                new MutationObserver(apply).observe(document.head, { childList: true, characterData: true, subtree: true });
            })();
            """;
    }

    private static DirectoryInfo? FindCheckoutRoot(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        try
        {
            var current = new DirectoryInfo(startDirectory);
            while (current is not null)
            {
                var git = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                {
                    return current;
                }

                current = current.Parent;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A title is not worth failing a startup over.
        }

        return null;
    }

    private static string? ReadBranch(DirectoryInfo root)
    {
        try
        {
            var gitDirectory = ResolveGitDirectory(root);
            if (gitDirectory is null)
            {
                return null;
            }

            var head = Path.Combine(gitDirectory, "HEAD");
            if (!File.Exists(head))
            {
                return null;
            }

            var reference = File.ReadLines(head).FirstOrDefault()?.Trim();
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            if (!reference.StartsWith(HeadReferencePrefix, StringComparison.Ordinal))
            {
                // Detached HEAD: the commit is all there is to name it by.
                return $"detached {reference[..Math.Min(7, reference.Length)]}";
            }

            var name = reference[HeadReferencePrefix.Length..].Trim();
            return name.StartsWith(BranchReferencePrefix, StringComparison.Ordinal)
                ? name[BranchReferencePrefix.Length..]
                : name;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The folder holding HEAD: <c>.git</c> itself in a clone, and the path the
    /// <c>.git</c> file points at in a worktree.
    /// </summary>
    private static string? ResolveGitDirectory(DirectoryInfo root)
    {
        var git = Path.Combine(root.FullName, ".git");
        if (Directory.Exists(git))
        {
            return git;
        }

        if (!File.Exists(git))
        {
            return null;
        }

        var pointer = File.ReadLines(git).FirstOrDefault()?.Trim();
        if (pointer is null || !pointer.StartsWith(GitDirectoryPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var path = pointer[GitDirectoryPrefix.Length..].Trim();
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path));
    }
}
