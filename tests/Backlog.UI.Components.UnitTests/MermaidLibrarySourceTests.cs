using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Where Mermaid comes from, and what happens when it cannot be loaded.
/// <para>
/// It used to be imported from a CDN at a floating major version. A WebView with no
/// egress then drew no diagram at all, the version could change under a release
/// nobody made, and one failed import left the cached promise rejected for the rest
/// of the session, so a diagram never rendered again even after the network came
/// back. <c>.devbook/design/component-libraries.md#risks-and-gaps</c> already
/// decided the answer: a vendored, pinned copy, served by the library itself.
/// </para>
/// <para>
/// These read the script and the files rather than a render, because bUnit runs no
/// JS: the import, the URL it resolves to and the retry all live in the browser.
/// </para>
/// </summary>
public sealed partial class MermaidLibrarySourceTests
{
    private const string PinnedVersion = "11.17.2";

    private static readonly string[] Folder = ["src", "Core", "Backlog.UI.Components", "wwwroot", "lib", "mermaid"];

    [Fact]
    public void No_diagram_library_is_fetched_from_a_cdn()
    {
        Assert.DoesNotContain("cdn.jsdelivr.net", ComponentsScript.Source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", Sources(), StringComparison.Ordinal);
    }

    [Fact]
    public void Mermaid_defaults_to_the_copy_the_library_serves()
    {
        var sources = Sources();

        Assert.Contains("'lib/mermaid/mermaid.esm.min.mjs'", sources, StringComparison.Ordinal);

        // Resolved against this script's own URL, read while the file is first
        // evaluated: currentScript is null by the time a diagram asks for Mermaid,
        // and a host with a non-root <base href> would send a relative import
        // somewhere else.
        Assert.Contains("document.currentScript", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_can_still_point_mermaid_somewhere_else()
    {
        Assert.Contains("...(window.backlogDiagramLibrarySources ?? {})", Sources(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_is_forgotten_so_the_next_diagram_tries_again()
    {
        var loader = ComponentsScript.Region("    async function backlogLoadMermaid() {", "    function backlogRenderDiagramError(");

        Assert.Matches(new Regex(@"\.catch\(\s*\(\)\s*=>\s*\{[^}]*backlogMermaidPromise\s*=\s*undefined", RegexOptions.Singleline), loader);
    }

    /// <summary>
    /// Forgetting the promise is not enough on its own. The module map answers a
    /// second import of a URL that failed with the recorded failure and fetches
    /// nothing, so the retry has to ask for a URL the map has not seen.
    /// </summary>
    [Fact]
    public void A_retry_imports_the_library_under_a_url_of_its_own()
    {
        var attemptUrl = ComponentsScript.Region("    function backlogMermaidAttemptUrl(source, attempt) {", "    async function backlogLoadMermaid() {");
        Assert.Contains("if (!attempt) return source;", attemptUrl, StringComparison.Ordinal);
        Assert.Contains("url.searchParams.set('retry', String(attempt));", attemptUrl, StringComparison.Ordinal);

        var loader = ComponentsScript.Region("    async function backlogLoadMermaid() {", "    function backlogRenderDiagramError(");
        Assert.Contains("const attempt = backlogMermaidAttempts++;", loader, StringComparison.Ordinal);
        Assert.Contains("await import(backlogMermaidAttemptUrl(source, attempt))", loader, StringComparison.Ordinal);
        Assert.DoesNotContain("await import(source)", loader, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pinned_build_and_its_license_ship_with_the_library()
    {
        Assert.True(File.Exists(RepositoryRoot.File([.. Folder, "mermaid.esm.min.mjs"])));
        Assert.Contains("MIT License", File.ReadAllText(RepositoryRoot.File([.. Folder, "LICENSE"])), StringComparison.Ordinal);
        Assert.Contains($"Version {PinnedVersion}", File.ReadAllText(RepositoryRoot.File([.. Folder, "README.md"])), StringComparison.Ordinal);
    }

    /// <summary>
    /// The entry loads its chunks by relative path and file name, and so does every
    /// chunk. One missing file breaks the diagram types that reach it and nothing
    /// else, which is the kind of failure nobody notices until a reader opens that
    /// one kind of diagram.
    /// </summary>
    [Fact]
    public void Every_chunk_the_build_imports_is_there()
    {
        var root = RepositoryRoot.Directory(Folder);
        var modules = new[] { Path.Combine(root, "mermaid.esm.min.mjs") }
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "chunks", "mermaid.esm.min"), "*.mjs"))
            .ToList();

        Assert.True(modules.Count > 1, "The vendored build has no chunks.");

        var missing = modules
            .SelectMany(module => RelativeImport().Matches(File.ReadAllText(module))
                .Select(match => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(module)!, match.Groups["path"].Value))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(target => !File.Exists(target))
            .Select(target => Path.GetRelativePath(root, target))
            .ToList();

        Assert.Empty(missing);
    }

    private static string Sources() =>
        ComponentsScript.Region("    const backlogDiagramInstances = new Map();", "    let backlogMermaidPromise;");

    [GeneratedRegex("""(?:from|import\()\s*"(?<path>\.\.?/[^"]+\.mjs)"\)?""")]
    private static partial Regex RelativeImport();
}
