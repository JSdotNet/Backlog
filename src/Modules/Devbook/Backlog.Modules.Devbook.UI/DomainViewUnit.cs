namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The page a Domain menu row opens in the new domain view: one per kind of file a
/// row can be (local ADR 0040). A file shown on another page —
/// <c>dependencies.md</c>, a <c>domain.&lt;name&gt;.invariants.md</c> — answers the page
/// it shows on, so a path that reaches the view some other way still lands on a page.
/// </summary>
public enum DomainViewPage
{
    /// <summary>No row is selected.</summary>
    None,

    /// <summary><c>context-map.md</c>: Map and List.</summary>
    ContextMap,

    /// <summary>A context's folder or its <c>context.md</c>: its neighbours, and its Overview below.</summary>
    Context,

    /// <summary><c>domain.md</c> and its splits: a unit picker, About and Always true.</summary>
    Domain,

    /// <summary><c>features.md</c> and its splits.</summary>
    Features,

    /// <summary><c>requirements.md</c> and its splits.</summary>
    Requirements,

    /// <summary><c>model.md</c>: Diagram, Fields and Events.</summary>
    Model,

    /// <summary><c>flow.md</c>: the context's lifecycles.</summary>
    Flow,

    /// <summary>Any other file the menu lists: a page the convention does not name.</summary>
    Other
}

/// <summary>
/// Which <see cref="DomainViewPage"/> a selected Domain menu path opens. The path is
/// the one the Devbook pane's menu holds — relative to the Domain folder, so
/// <c>context-map.md</c>, <c>tasks</c> for a context's folder, <c>tasks/domain.md</c>
/// for a file in it. Derived from the file name alone, as ADR 0040 decides: no
/// chapter carries a field for the view.
/// </summary>
public static class DomainViewUnit
{
    public static DomainViewPage PageFor(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) return DomainViewPage.None;

        var path = selectedPath.Replace('\\', '/').Trim('/');
        if (path.Length == 0) return DomainViewPage.None;

        // A row without a file name is a context's folder, which opens the context.
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return DomainViewPage.Context;

        return DomainDevbookStore.KindFromFile(path) switch
        {
            DomainDevbookDocumentKind.ContextMap => DomainViewPage.ContextMap,
            DomainDevbookDocumentKind.Context => DomainViewPage.Context,
            DomainDevbookDocumentKind.Dependencies => DomainViewPage.Context,
            DomainDevbookDocumentKind.Domain => DomainViewPage.Domain,
            DomainDevbookDocumentKind.Invariants => DomainViewPage.Domain,
            DomainDevbookDocumentKind.Features => DomainViewPage.Features,
            DomainDevbookDocumentKind.Requirements => DomainViewPage.Requirements,
            DomainDevbookDocumentKind.Model => DomainViewPage.Model,
            DomainDevbookDocumentKind.Flow => DomainViewPage.Flow,
            _ => DomainViewPage.Other
        };
    }

    /// <summary>The page as markup and tests key on it: <c>context-map</c>, <c>domain</c>.</summary>
    public static string Key(DomainViewPage page) => page switch
    {
        DomainViewPage.ContextMap => "context-map",
        _ => page.ToString().ToLowerInvariant()
    };

    /// <summary>The page's name as its placeholder heads it.</summary>
    public static string Title(DomainViewPage page) => page switch
    {
        DomainViewPage.None => "Pick a unit",
        DomainViewPage.ContextMap => "Context map",
        DomainViewPage.Context => "Context",
        DomainViewPage.Domain => "Domain",
        DomainViewPage.Features => "Features",
        DomainViewPage.Requirements => "Requirements",
        DomainViewPage.Model => "Model",
        DomainViewPage.Flow => "Flow",
        _ => "Page"
    };

    /// <summary>What the placeholder says when no row is selected or the file is of no known kind, in the feature chapter's words.</summary>
    public static string Description(DomainViewPage page) => page switch
    {
        DomainViewPage.None => "Choose the context map, a context, or a file inside one from the menu.",
        DomainViewPage.ContextMap => "Map draws the contexts and their relations; List names them in reading order.",
        DomainViewPage.Context => "The context's neighbours, and below them its boundary.",
        DomainViewPage.Domain => "A unit picker. An aggregate opens About and Always true; a domain service opens one page.",
        DomainViewPage.Features => "The features and their sub-features.",
        DomainViewPage.Requirements => "Each requirement and its scenarios.",
        DomainViewPage.Model => "Diagram, Fields and Events.",
        DomainViewPage.Flow => "The context's lifecycles.",
        _ => "The file as written."
    };
}
