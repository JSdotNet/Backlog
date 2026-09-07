using System.Reflection;
using Backlog.UI.Components.Diagrams;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// The storybook's answer to <see cref="IDiagramArtifactSource"/>: one committed
/// Archify artifact, for one known fence, and nothing for anything else.
/// </summary>
/// <remarks>
/// <para>
/// A fixture, not a port adapter. The application answers this interface out of a
/// repository clone, a feature flag and an agent CLI, and the storybook has none
/// of those on purpose — it references the component library and the service
/// defaults and nothing else. Without something registered, <c>DiagramView</c>'s
/// artifact mode has no review surface: the Archify/Mermaid switch, full screen,
/// the out-of-date notice and the render offer are all gated on an artifact
/// existing, and nothing in this host could make one exist. This does, from an
/// artifact embedded in the assembly and a specification path it names but never
/// reads.
/// </para>
/// <para>
/// Two sources are known. <see cref="ComponentMap"/> is the mermaid the artifact
/// was authored from, matched the way the app matches — on
/// <see cref="DiagramSourceHash.Normalize"/> — so the story proves the same
/// lookup. <see cref="ComponentMapEdited"/> is that fence with one label changed:
/// the artifact exists but was authored from earlier text, which is the drift case
/// the design exists to say out loud rather than hide.
/// </para>
/// <para>
/// Every other source gets <c>null</c>, and that is what keeps every other
/// <c>DiagramView</c> in the storybook exactly as it was — a null answer is the
/// same as no source registered, so no switch appears, no offer is made, and no
/// page that was not written for this fixture can tell it is there.
/// </para>
/// </remarks>
internal sealed class StorybookDiagramArtifacts : IDiagramArtifactSource
{
    /// <summary>The fence the committed artifact was authored from. Held here
    /// rather than on the page so the story and the lookup cannot drift: a
    /// changed character anywhere in this string is a missed lookup, and the
    /// page reads it from here.</summary>
    public const string ComponentMap = """
        flowchart TD
            A[Desktop UI] --> B[Backlog.UI.Components]
            C[Mobile UI] --> B
            D[Storybook] --> B
            B --> E[components.css]
            B --> F[components.js]
        """;

    /// <summary>The same diagram after somebody edited one node's label. The
    /// artifact's specification still exists for it; the artifact itself no longer
    /// answers for what the fence says.</summary>
    public const string ComponentMapEdited = """
        flowchart TD
            A[Desktop UI] --> B[Backlog.UI.Components]
            C[Mobile UI] --> B
            D[UI Storybook] --> B
            B --> E[components.css]
            B --> F[components.js]
        """;

    /// <summary>Where the artifact and its specification live in the repository,
    /// which is what the app would report for a chapter's artifact and what a
    /// person regenerating this one needs to find.</summary>
    private const string Folder = "src/Harness/Backlog.UI.Storybook/Components/Shared/Archify";

    private const string ArtifactPath = Folder + "/slice-flow.architecture.html";

    private const string SpecPath = Folder + "/slice-flow.architecture.json";

    /// <summary>The LogicalName the csproj embeds the artifact under.</summary>
    private const string ResourceName = "archify/slice-flow.architecture.html";

    private const string ArchifyType = "architecture";

    private const string Quality = "showcase";

    private const string CannotRun =
        "The storybook serves a committed artifact and cannot run the generator. "
        + "Regenerate it from the specification with tools/archify — see the README beside it.";

    private static readonly string NormalizedComponentMap = DiagramSourceHash.Normalize(ComponentMap);

    private static readonly string NormalizedComponentMapEdited = DiagramSourceHash.Normalize(ComponentMapEdited);

    /// <summary>Read once and kept: the document is roughly 660 KB and every
    /// render of the story asks for it again.</summary>
    private static readonly Lazy<string> Html = new(ReadEmbeddedArtifact);

    /// <summary>False, so no diagram anywhere in the storybook offers to start an
    /// agent — there is none to start, and an offer is a promise.</summary>
    public bool CanAuthor => false;

    public DiagramArtifact? Find(string? source, string? language)
    {
        if (!DiagramView.CanRender(language)) return null;

        var normalized = DiagramSourceHash.Normalize(source);

        if (string.Equals(normalized, NormalizedComponentMap, StringComparison.Ordinal))
        {
            return new DiagramArtifact(Html.Value, ArtifactPath, SpecPath, ArchifyType, IsOutOfDate: false, Quality);
        }

        if (string.Equals(normalized, NormalizedComponentMapEdited, StringComparison.Ordinal))
        {
            return new DiagramArtifact(null, null, SpecPath, ArchifyType, IsOutOfDate: true, Quality);
        }

        return null;
    }

    public Task<string?> RenderAsync(string? source, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(CannotRun);

    public Task<string?> AuthorAsync(string? source, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(CannotRun);

    /// <summary>Fails loudly rather than answering null: the embed is a build-time
    /// fact, and a story that quietly showed the render offer instead of the
    /// artifact would read as the component misbehaving rather than as the
    /// csproj having lost a line.</summary>
    private static string ReadEmbeddedArtifact()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The storybook assembly does not embed '{ResourceName}'. {ArtifactPath} is meant to be an "
                + "EmbeddedResource with that LogicalName in Backlog.UI.Storybook.csproj.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
