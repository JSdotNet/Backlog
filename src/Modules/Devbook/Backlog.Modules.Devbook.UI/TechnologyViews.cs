using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The five views the Technology area switches between from one tab bar
/// (<c>.devbook/domain/devbook/features.md#technology-view</c>). The ids are the
/// tabs' ids, so a view that links to another names it by one of these.
/// </summary>
public static class TechnologyViews
{
    public const string Overview = "overview";
    public const string Dependencies = "dependencies";
    public const string Complexity = "complexity";
    public const string Clusters = "clusters";
    public const string TypeLenses = "type-lenses";

    /// <summary>A layer's title without the word every layer title ends in, so the
    /// table's Layer column reads <em>Shared</em> rather than <em>Shared
    /// Technologies</em> on every row.</summary>
    public static string LayerLabel(string layer) =>
        layer.EndsWith(" Technologies", StringComparison.OrdinalIgnoreCase) ? layer[..^" Technologies".Length]
        : layer.EndsWith(" Stack", StringComparison.OrdinalIgnoreCase) ? layer[..^" Stack".Length]
        : layer;
}

/// <summary>
/// The short code a technology's type is printed as beside its name, and the
/// type's name as a label. A type the convention does not name is printed by its
/// own first letters, so a new word in a chapter still reads as itself.
/// </summary>
public static class TechnologyTypeCodes
{
    public static string Code(string? type) => Normalize(type) switch
    {
        "framework" => "FWK",
        "platform" => "PLAT",
        "runtime" => "RT",
        "language" => "LANG",
        "package" => "PKG",
        "library" => "LIB",
        "tool" => "TOOL",
        "plugin" => "PLG",
        "skill" => "SKILL",
        "agent" => "AGENT",
        "service" => "SVC",
        "format" => "FMT",
        "protocol" => "PROTO",
        "" => "—",
        var other => new string([.. other.Where(char.IsLetterOrDigit).Take(4)]).ToUpperInvariant(),
    };

    /// <summary>The type as a word with a capital, the way a chip names it.</summary>
    public static string Label(string? type)
    {
        var normalized = Normalize(type);
        return normalized.Length == 0 ? "Unstated" : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static string Normalize(string? type) => type?.Trim().ToLowerInvariant() ?? string.Empty;
}

/// <summary>
/// What every Technology view is handed: the folder as read, the one shared
/// selection, and the ways back to the panel. A view inherits this rather than
/// declaring its own copies, so the five stay one shape and a view filled in later
/// touches only its own file.
/// </summary>
public abstract class TechnologyViewBase : ComponentBase
{
    /// <summary>The folder as the panel last read it.</summary>
    [Parameter, EditorRequired]
    public TechnologyDevbookView View { get; set; } = null!;

    /// <summary>The selected technology's id, shared by every view; null when none is.</summary>
    [Parameter]
    public string? SelectedId { get; set; }

    /// <summary>Raised when the view picks a technology. The panel holds the
    /// selection, so switching views keeps it.</summary>
    [Parameter]
    public EventCallback<string?> SelectedIdChanged { get; set; }

    /// <summary>Raised to switch to another view, by its <see cref="TechnologyViews"/> id.</summary>
    [Parameter]
    public EventCallback<string> OnOpenView { get; set; }

    /// <summary>Raised to open a chapter: the technology's own, or one in another folder.</summary>
    [Parameter]
    public EventCallback<DevbookChapterLink> OnNavigateToChapter { get; set; }

    /// <summary>Raised when the reader picks another state for a technology. The
    /// panel writes it, because it owns the folder.</summary>
    [Parameter]
    public EventCallback<TechnologyStatusChange> OnStatusChanged { get; set; }

    /// <summary>The message of the last failed status write, shown beside the control.</summary>
    [Parameter]
    public string? StatusError { get; set; }

    /// <summary>The selected node, or null when nothing is selected or the id is gone.</summary>
    protected TechnologyGraphNode? Selected => SelectedId is null ? null : NodeById(SelectedId);

    /// <summary>The graph's node with this id, boundary nodes included; null when there is none.</summary>
    protected TechnologyGraphNode? NodeById(string id) =>
        View.Graph.Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    /// <summary>This folder's own technologies, in reading order; boundary nodes
    /// are chapters in other folders and are not counted as technologies.</summary>
    protected IEnumerable<TechnologyGraphNode> Technologies => View.Graph.Nodes.Where(node => !node.IsBoundary);

    protected Task SelectAsync(string? id) => SelectedIdChanged.InvokeAsync(id);
}
