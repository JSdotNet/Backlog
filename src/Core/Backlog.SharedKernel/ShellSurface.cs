namespace Backlog.SharedKernel;

/// <summary>
/// A screen of the desktop shell that a module brings rather than the shell
/// (local ADR 0040 §1).
/// <para>
/// A module registers one in the container from its <c>.UI</c> project's
/// registration extension, the way it registers a <see cref="SettingsSection"/>,
/// and <see cref="ISurfaceCatalog"/> holds every one registered. The shell's header
/// and the window a surface is rendered in read the catalog, so a module's new
/// screen is the module's change alone. Here rather than in the component library
/// for the reason <see cref="SettingsSection"/> is: it is a contract between the
/// shell and the modules, not a control.
/// </para>
/// <para>
/// The shell renders <paramref name="Component"/> through a <c>DynamicComponent</c>
/// and hands it two parameters, named by <see cref="QueryParameter"/> and
/// <see cref="CloseParameter"/>: the query it was shown with, and the callback that
/// closes it. The component declares both, as
/// <c>IReadOnlyDictionary&lt;string, string&gt;</c> and <c>EventCallback</c>:
/// <c>DynamicComponent</c> refuses a parameter its component does not declare.
/// Everything else it takes from the container, and a jump to
/// another surface goes through <see cref="ISurfaceNavigator"/>.
/// </para>
/// </summary>
/// <param name="Id">The surface's stable, lower-case id, such as <c>devbook</c>: what
/// a route, a tool and a link name. Unique in the catalog, which refuses a second
/// registration of one.</param>
/// <param name="Title">The surface's name in the header and in its window's title.</param>
/// <param name="Group">Whether the surface is a main view or a takeover.</param>
/// <param name="Order">Its place within its <paramref name="Group"/> in the header,
/// lowest first.</param>
/// <param name="Component">The Razor component that renders it.</param>
/// <param name="FeatureKey">The feature the surface is offered behind, or null for
/// one that is always offered. While it is off the catalog leaves the surface out
/// everywhere it is read.</param>
/// <param name="QueryKeys">The query keys the surface accepts, such as
/// <c>chapter</c> and <c>scope</c> (local ADR 0040 §6), or null for none.</param>
public sealed record ShellSurface(
    string Id,
    string Title,
    ShellSurfaceGroup Group,
    int Order,
    Type Component,
    string? FeatureKey = null,
    IReadOnlyList<string>? QueryKeys = null)
{
    /// <summary>The id reserved for the Review pane of the management bots'
    /// proposal queue (local ADR 0040 §1). Until that pane registers its surface the
    /// catalog answers it as not available, the same as an unknown id.</summary>
    public const string ProposalsId = "proposals";

    /// <summary>The name of the component parameter the query is handed in.</summary>
    public const string QueryParameter = "Query";

    /// <summary>The name of the component parameter the close callback is handed in.</summary>
    public const string CloseParameter = "OnClose";
}

/// <summary>
/// The two kinds of surface the shell header lists (local ADR 0040 §1), in the
/// order the catalog lists them.
/// </summary>
public enum ShellSurfaceGroup
{
    /// <summary>A main view the view switch picks — Tasks, Roadmap, Calendar, Board,
    /// Goals. Exactly one is on screen while no takeover is.</summary>
    View,

    /// <summary>A surface that takes the whole window in place of the main view,
    /// which waits underneath.</summary>
    Takeover,
}
