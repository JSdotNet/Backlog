namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>What kind of thing an element is, in the C4 vocabulary.</summary>
/// <remarks>
/// The static model and the deployment model are one enumeration rather than two,
/// because a deployment view draws both: a container instance is a reference to a
/// container, and the node it sits in is drawn around it. Splitting them would
/// mean two element tables and a join wherever a view walks parents.
/// </remarks>
public enum C4ElementKind
{
    Person,
    SoftwareSystem,
    Container,
    Component,
    DeploymentNode,
    InfrastructureNode,
    ContainerInstance,
    SoftwareSystemInstance
}

/// <summary>The six view types Structurizr declares and c4hero authors.</summary>
public enum C4ViewKind
{
    SystemLandscape,
    SystemContext,
    Container,
    Component,
    Dynamic,
    Deployment
}

/// <summary>
/// One element of the model.
/// <para>
/// <c>Id</c> is the identifier the DSL declared, spelled the way a relationship
/// would name it — dotted under <c>!identifiers hierarchical</c> and bare under
/// <c>flat</c>. It is kept as authored rather than normalised, because it is the
/// only thing that ties a relationship statement to an element, and a
/// normalisation applied on one side and not the other loses edges silently.
/// </para>
/// </summary>
/// <param name="ParentId">The element this one is declared inside, or null at the
/// top of the model. What makes a container view able to draw a system as a
/// boundary, and what a view walks up when it has to roll an edge endpoint up to
/// something it is actually drawing.</param>
/// <param name="Group">The <c>group</c> block the declaration sat in, if any.
/// Carried because it is part of what the author said; no view draws it yet, and
/// mermaid C4 has no grouping construct to draw it with.</param>
/// <param name="InstanceOfId">For a deployment instance, the static element it is
/// an instance of. A deployment view draws the instance with the name and
/// technology of the thing it instantiates, so the reference has to survive
/// parsing.</param>
/// <param name="Environment">The <c>deploymentEnvironment</c> this element was
/// declared in. Its own field rather than a value folded into
/// <paramref name="Group"/>, because a <c>group</c> inside an environment would
/// overwrite it and a deployment view selects on exactly this.</param>
/// <param name="Properties">The element's own <c>properties</c> block. Read because
/// it is where c4hero keeps what Structurizr has no field for — the owning team, a
/// lifecycle status — and those are two of the four facets the Highlighter filters
/// on. Keys are matched case-insensitively.</param>
public sealed record C4Element(
    string Id,
    C4ElementKind Kind,
    string Name,
    string? Description,
    string? Technology,
    IReadOnlyList<string> Tags,
    string? ParentId,
    string? Group,
    string? InstanceOfId = null,
    string? Environment = null,
    IReadOnlyDictionary<string, string>? Properties = null)
{
    /// <summary>Whether this element carries the given tag, case-insensitively.
    /// Tags are how Structurizr says "this container is a database", and the only
    /// input to choosing between mermaid's <c>Container</c> and
    /// <c>ContainerDb</c>.</summary>
    public bool HasTag(string tag) =>
        Tags.Any(candidate => string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A property by name, or null.
    /// <para>
    /// Several names are tried for the two facets that matter, because there is no
    /// standard key for either: a team may be written <c>owner</c>, <c>team</c> or
    /// <c>Owner</c>, and asking for one spelling would silently find nothing in a
    /// workspace that used another.
    /// </para>
    /// </summary>
    public string? Property(params string[] names)
    {
        if (Properties is null) return null;

        foreach (var name in names)
        {
            foreach (var pair in Properties)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    return pair.Value;
                }
            }
        }

        return null;
    }

    /// <summary>The owning team, however the workspace spells it.</summary>
    public string? Owner => Property("owner", "team", "owners", "owned-by");

    /// <summary>The lifecycle status, however the workspace spells it.</summary>
    public string? Status => Property("status", "lifecycle", "state");
}

/// <summary>One <c>source -&gt; destination</c> statement.</summary>
public sealed record C4Relationship(
    string SourceId,
    string DestinationId,
    string? Description,
    string? Technology,
    IReadOnlyList<string> Tags);

/// <summary>One numbered step of a dynamic view.</summary>
public sealed record C4DynamicStep(
    int Order,
    string SourceId,
    string DestinationId,
    string? Description);

/// <summary>
/// One view: which kind of picture, of what, and which elements the author asked
/// to see in it.
/// </summary>
/// <param name="Key">The view's own identifier. This is what a chapter reference
/// addresses — <c>.arc42/_c4/backlog.dsl#container-backlog</c> names the view with
/// key <c>container-backlog</c> — so a view the DSL left unkeyed is given a
/// synthesised one rather than being left unaddressable.</param>
/// <param name="ScopeId">The element the view is of: the software system of a
/// context or container view, the container of a component view. Null for a system
/// landscape, which is of the whole model.</param>
/// <param name="IncludesAll">Whether the view said <c>include *</c>. Held apart
/// from <c>Includes</c> because it means "whatever the default rule for this kind
/// of view says", not "every element in the model".</param>
/// <param name="Environment">The deployment environment name, for a deployment
/// view only.</param>
public sealed record C4View(
    string Key,
    C4ViewKind Kind,
    string? ScopeId,
    string? Title,
    string? Description,
    IReadOnlyList<string> Includes,
    IReadOnlyList<string> Excludes,
    bool IncludesAll,
    string? AutoLayout,
    IReadOnlyList<C4DynamicStep> Steps,
    string? Environment = null);

/// <summary>
/// Something in the file the reader did not understand.
/// <para>
/// A problem is reported and the rest of the file is still read. That is the
/// deliberate choice: this reader is a second implementation of a dialect whose
/// first implementation lives in another repository, in another language, where no
/// test here can see it. The failure it has to avoid is not refusing a file — it
/// is accepting one and drawing a picture that quietly omits what it could not
/// parse. So every construct outside the supported subset arrives here, with the
/// line it was on, and the panel shows the list.
/// </para>
/// </summary>
/// <param name="Line">One-based line number in the <c>.dsl</c> file.</param>
/// <param name="Construct">The keyword or token that was not understood, as
/// written.</param>
public sealed record C4Problem(int Line, string Construct, string Message);

/// <summary>
/// A parsed Structurizr workspace: the model, the views, and everything the reader
/// could not make sense of.
/// </summary>
/// <param name="ElementStyles">The <c>styles</c> block, read so that legitimate
/// syntax does not land in the problem report, and applied by the writer where
/// mermaid has something to apply it to.</param>
public sealed record C4Workspace(
    string? Name,
    string? Description,
    IReadOnlyList<C4Element> Elements,
    IReadOnlyList<C4Relationship> Relationships,
    IReadOnlyList<C4View> Views,
    IReadOnlyList<C4Problem> Problems,
    IReadOnlyList<C4ElementStyle>? ElementStyles = null)
{
    /// <summary>An empty workspace, for a file that could not be read at all.</summary>
    public static C4Workspace Empty { get; } = new(null, null, [], [], [], []);

    private Dictionary<string, C4Element>? _byId;

    /// <summary>The element with this identifier, or null. Case-insensitive,
    /// because the DSL's identifiers are.</summary>
    public C4Element? Element(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        // First declaration wins on a duplicated identifier. The DSL forbids one,
        // and the reader reports it as a problem, but a lookup still has to answer
        // something rather than throw while a reader is looking at the report.
        _byId ??= Elements
            .GroupBy(element => element.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return _byId.TryGetValue(id, out var found) ? found : null;
    }

    /// <summary>
    /// The view with this key, or null. The lookup a chapter reference's anchor
    /// resolves through.
    /// <para>
    /// The slugged form of the key is accepted as well as the authored one, because
    /// a Structurizr view key may be a quoted string with spaces in it and the
    /// thing doing the asking is an anchor out of a <c>related:</c> entry. A
    /// reference is allowed to spell <c>Container Backlog</c> as
    /// <c>container-backlog</c> rather than being the one place in the repository
    /// where a reference carries a space.
    /// </para>
    /// </summary>
    public C4View? View(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        return Views.FirstOrDefault(view => string.Equals(view.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? Views.FirstOrDefault(view => string.Equals(C4Slug.Of(view.Key), C4Slug.Of(key), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The elements declared directly inside this one.</summary>
    public IEnumerable<C4Element> Children(string? parentId) =>
        Elements.Where(element => string.Equals(element.ParentId, parentId, StringComparison.OrdinalIgnoreCase));

    /// <summary>This element and everything beneath it.</summary>
    public IEnumerable<C4Element> Subtree(string? id)
    {
        if (Element(id) is not { } root) return [];

        var found = new List<C4Element> { root };
        for (var index = 0; index < found.Count; index++)
        {
            found.AddRange(Children(found[index].Id));
        }

        return found;
    }

    /// <summary>The chain from this element up to the top of the model, starting
    /// with the element itself. What a view walks when an edge endpoint is not
    /// something it draws and has to be rolled up to something it does.</summary>
    public IEnumerable<C4Element> Ancestry(string? id)
    {
        var current = Element(id);
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            yield return current;
            current = Element(current.ParentId);
        }
    }
}
