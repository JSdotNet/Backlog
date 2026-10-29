using System.ComponentModel;

using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// <c>open_window</c>, the tool that shows the person a registered surface (local ADR
/// 0040 §7): in a native window of the desktop app, or as a web link to the page the
/// app serves on its loopback listener.
/// <para>
/// <b>It opens a window only when asked to.</b> <c>target: "web"</c> opens nothing and
/// answers a link; only <c>target: "desktop"</c> puts a window on the person's screen,
/// and the description says so, so a model does not call it that way by default. That
/// keeps the rule <c>open_dashboard</c> keeps — what a window shows changes on the
/// person's click, not on an agent's whim — for every call but the one that names a
/// window as the point.
/// </para>
/// <para>
/// <b>The catalog is the gate.</b> A surface the catalog does not offer — an id nothing
/// registered, a surface whose own flag is off, and <c>proposals</c> until its pane
/// registers — is refused before anything is opened or minted, and the refusal lists the
/// ids it does offer. So is a query key the surface did not register: a link that
/// carried one would open on something other than what was asked.
/// </para>
/// <para>
/// <b>No feature flag</b> (local ADR 0040 §7): its group is ungated, and a surface's own
/// flag still hides that surface here as everywhere the catalog is read.
/// </para>
/// </summary>
/// <param name="catalog">The surfaces on offer, and the query keys each accepts.</param>
/// <param name="host">What this host can open and link to.</param>
[McpServerToolType]
public sealed class WindowTools(ISurfaceCatalog catalog, ISurfaceWindowHost host)
{
    internal const string OpenWindow = "open_window";

    /// <summary>The two targets.</summary>
    internal const string Desktop = "desktop";
    internal const string Web = "web";

    /// <summary>The query keys of local ADR 0040 §6 the tool takes, in the order a link
    /// carries them.</summary>
    internal const string PlanKey = "plan";
    internal const string EntryKey = "entry";
    internal const string ChapterKey = "chapter";
    internal const string ScopeKey = "scope";

    /// <summary>The <c>scope</c> that names the Brain rather than a repository.</summary>
    internal const string BrainScope = "brain";

    [McpServerTool(
        Name = OpenWindow,
        Title = "Open a surface in a window",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Shows the person a Backlog surface — tasks, board, roadmap, devbook and the rest the app offers — on a plan, "
        + "an entry or a chapter. With target \"web\" it opens nothing: it answers a link to the surface's page, valid "
        + "for one load within 10 minutes, for the person or a browser pane to open. Only with target \"desktop\" does "
        + "it open a window on the person's screen; use that only when the person asked for a window. Both answers "
        + "carry a backlog:// link that opens the surface in the installed app and stays valid, for a brief or a "
        + "digest. A surface the app does not offer is refused with the ids it does.")]
    public WindowOpenedPayload Open(
        [Description("The surface's id, such as tasks, board, roadmap or devbook.")]
        string surface,
        [Description("desktop to open a native window now, or web to get a link and open nothing.")]
        string target,
        [Description("Optional. A plan tag, with or without its +, to open the surface on that plan.")]
        string? plan = null,
        [Description("Optional. An entry id, to open that entry in its editor.")]
        string? entry = null,
        [Description("Optional. A devbook chapter's path, repository-relative, or a Brain note's path with scope brain.")]
        string? chapter = null,
        [Description("Optional, with chapter. The repository, owner/name, or brain, that chapter is read in. Left out, the window's own repository scope.")]
        string? scope = null)
    {
        var shown = Surface(surface);
        var kind = Target(target);
        var query = Query(shown, plan, entry, chapter, scope);
        var backlogLink = SurfaceLink.Protocol(shown.Id, query);

        if (kind == Desktop)
        {
            var opened = host.TryOpenWindow(shown.Id, query);

            return new WindowOpenedPayload(
                shown.Id,
                Desktop,
                opened,
                Url: null,
                backlogLink,
                opened
                    ? $"Opened {shown.Title} in a new window."
                    : $"No native window is available here: this host serves {shown.Title} as a web page only. Ask again with target \"web\" for a link.");
        }

        var link = host.WebLink(shown.Id, query);

        return new WindowOpenedPayload(
            shown.Id,
            Web,
            Opened: false,
            link?.AbsoluteUri,
            backlogLink,
            link is null
                ? $"No web page is served now: the app's listener is not listening, so no link was made for {shown.Title}."
                : $"The link opens {shown.Title} once, within 10 minutes. Nothing was opened.");
    }

    /// <summary>The surface the catalog offers under <paramref name="id"/>, or the refusal
    /// that names the ids it does offer.</summary>
    private ShellSurface Surface(string? id)
    {
        var trimmed = id?.Trim() ?? string.Empty;
        if (trimmed.Length > 0 && catalog.TryGet(trimmed, out var surface)) return surface;

        var offered = catalog.Surfaces.Select(candidate => candidate.Id).ToList();

        throw RepositoryScope.Failure(Error.NotFound(
            "window.surface_not_available",
            $"The surface '{trimmed}' is not available. Available: {(offered.Count == 0 ? "none" : string.Join(", ", offered))}."));
    }

    private static string Target(string? target) => target?.Trim().ToLowerInvariant() switch
    {
        Desktop => Desktop,
        Web => Web,
        _ => throw RepositoryScope.Failure(Error.Validation(
            "window.target_unknown",
            $"target must be \"{Desktop}\" or \"{Web}\"; it was '{target}'."))
    };

    /// <summary>The query a link carries, in local ADR 0040 §6's order, each key one the
    /// surface registered. A plan tag loses its <c>+</c>, which the surface reads either
    /// way, so one plan has one link.</summary>
    private static Dictionary<string, string> Query(
        ShellSurface surface,
        string? plan,
        string? entry,
        string? chapter,
        string? scope)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);

        var tag = plan?.Trim().TrimStart('+');
        if (!string.IsNullOrWhiteSpace(plan) && string.IsNullOrWhiteSpace(tag))
        {
            throw RepositoryScope.Failure(Error.Validation(
                "window.plan_empty",
                $"plan must name a plan tag; '{plan.Trim()}' names none."));
        }

        Add(surface, query, PlanKey, tag);
        Add(surface, query, EntryKey, entry);
        Add(surface, query, ChapterKey, chapter?.Trim().Replace('\\', '/'));

        if (!string.IsNullOrWhiteSpace(scope))
        {
            if (!query.ContainsKey(ChapterKey))
            {
                throw RepositoryScope.Failure(Error.Validation(
                    "window.scope_without_chapter",
                    "scope names where chapter is read, so it is given with chapter or not at all."));
            }

            var trimmed = scope.Trim();
            if (!IsScope(trimmed))
            {
                throw RepositoryScope.Failure(Error.Validation(
                    "window.scope_invalid",
                    $"scope must be a repository as owner/name, or {BrainScope}; it was '{trimmed}'."));
            }

            Add(surface, query, ScopeKey, trimmed);
        }

        return query;
    }

    private static void Add(ShellSurface surface, Dictionary<string, string> query, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var accepted = surface.QueryKeys ?? [];
        if (!accepted.Contains(key, StringComparer.Ordinal))
        {
            throw RepositoryScope.Failure(Error.Validation(
                $"window.{key}_not_accepted",
                $"{surface.Title} does not take {key}. It takes: {(accepted.Count == 0 ? "nothing" : string.Join(", ", accepted))}."));
        }

        query[key] = value.Trim();
    }

    private static bool IsScope(string scope)
    {
        if (string.Equals(scope, BrainScope, StringComparison.Ordinal)) return true;

        var parts = scope.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0 && !part.Any(char.IsWhiteSpace));
    }
}
