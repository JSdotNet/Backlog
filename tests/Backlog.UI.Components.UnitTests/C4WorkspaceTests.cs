using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The workspace this repository actually ships, read by the reader that will read
/// it in the app.
/// <para>
/// The tests beside this one pin the reader against shapes written to exercise it.
/// These pin the real file, which is the only thing that catches the failure the
/// design is most exposed to: c4hero writes the workspace and this reader reads it,
/// across a repository boundary where nothing else can compare the two. If a
/// c4hero release starts emitting something this reader does not know, the
/// workspace stops parsing clean and this is what says so.
/// </para>
/// <para>
/// The reference tests are the other half. A <c>related</c> entry naming a view is
/// the only statement tying a chapter to a picture, and a typo in one is invisible:
/// the chapter still renders, the view still draws, and the link between them is
/// simply not there.
/// </para>
/// </summary>
public sealed class C4WorkspaceTests
{
    private const string WorkspaceFolder = "_c4";

    /// <summary>A reference to a workspace as a chapter would spell one. Present in a
    /// chapter it is a problem rather than a feature — see the test that says why.</summary>
    private static readonly Regex WorkspaceReferencePattern =
        new(@"\.arc42/_c4/[A-Za-z0-9._-]+\.dsl", RegexOptions.Compiled);

    /// <summary>The authored view-to-chapters map beside the workspace.</summary>
    private static Dictionary<string, string[]> References()
    {
        var path = Path.Combine(RepositoryRoot.Root.FullName, ".arc42", WorkspaceFolder, "references.json");
        Assert.True(File.Exists(path), $"Expected an authored reference map at {path}.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var views = document.RootElement.GetProperty("views");

        return views.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(entry => entry.GetString() ?? string.Empty).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<FileInfo> Workspaces()
    {
        var folder = new DirectoryInfo(Path.Combine(RepositoryRoot.Root.FullName, ".arc42", WorkspaceFolder));
        return folder.Exists ? [.. folder.EnumerateFiles("*.dsl", SearchOption.TopDirectoryOnly)] : [];
    }

    private static IEnumerable<FileInfo> Chapters()
    {
        foreach (var knowledge in new[] { ".arc42", ".domain" })
        {
            var folder = new DirectoryInfo(Path.Combine(RepositoryRoot.Root.FullName, knowledge));
            if (!folder.Exists) continue;

            foreach (var file in folder.EnumerateFiles("*.md", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    public static TheoryData<string> WorkspaceFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Workspaces()) data.Add(file.Name);
        return data;
    }

    private static C4Workspace Read(string name) =>
        C4DslReader.Read(File.ReadAllText(Path.Combine(RepositoryRoot.Root.FullName, ".arc42", WorkspaceFolder, name)));

    [Fact]
    public void The_architecture_folder_carries_at_least_one_workspace()
    {
        Assert.NotEmpty(Workspaces());
    }

    /// <summary>
    /// The one failure this arrangement cannot see from the inside. A workspace that
    /// half-parses still draws a complete-looking picture, and the only thing that
    /// notices is the problem report — so the report has to be empty for what the
    /// repository ships.
    /// </summary>
    [Theory]
    [MemberData(nameof(WorkspaceFiles))]
    public void The_committed_workspace_parses_with_no_problems(string name)
    {
        var workspace = Read(name);

        Assert.Empty(workspace.Problems.Select(problem => $"line {problem.Line}: {problem.Construct} — {problem.Message}"));
    }

    [Theory]
    [MemberData(nameof(WorkspaceFiles))]
    public void The_committed_workspace_declares_views_and_elements(string name)
    {
        var workspace = Read(name);

        Assert.NotEmpty(workspace.Elements);
        Assert.NotEmpty(workspace.Views);
        Assert.NotNull(workspace.Name);
    }

    /// <summary>
    /// Every view has to draw. A view that writes nothing, or writes unbalanced
    /// braces, is a view mermaid refuses — and the reader has no way to know that
    /// from the DSL alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(WorkspaceFiles))]
    public void Every_view_of_the_committed_workspace_writes_a_diagram(string name)
    {
        var workspace = Read(name);

        foreach (var view in workspace.Views)
        {
            var mermaid = C4MermaidWriter.Write(workspace, view);

            Assert.StartsWith("C4", mermaid, StringComparison.Ordinal);
            Assert.DoesNotContain("Nothing to draw", mermaid, StringComparison.Ordinal);
            Assert.Equal(mermaid.Count(character => character == '{'), mermaid.Count(character => character == '}'));
        }
    }

    [Theory]
    [MemberData(nameof(WorkspaceFiles))]
    public void Every_view_key_is_unique_and_addressable(string name)
    {
        var workspace = Read(name);
        var keys = workspace.Views.Select(view => view.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(key));
            Assert.NotNull(workspace.View(key));
        }
    }

    /// <summary>
    /// The committed model is the busy one, and the one the crossing was reported on:
    /// two cards in a row with a third between them, the edge straight through the
    /// middle card and its label printed on top of it. Small fixtures do not reproduce
    /// that; this workspace does.
    /// </summary>
    [Theory]
    [MemberData(nameof(WorkspaceFiles))]
    public void No_edge_of_the_committed_workspace_runs_through_a_card(string name)
    {
        var workspace = Read(name);

        foreach (var view in workspace.Views)
        {
            var diagram = C4LayoutEngine.Build(workspace, view);

            foreach (var edge in diagram.Edges)
            {
                foreach (var (x, y) in Along(edge.Path))
                {
                    foreach (var node in diagram.Nodes)
                    {
                        if (node.Alias == edge.FromAlias || node.Alias == edge.ToAlias) continue;

                        var inside = x > node.X + 4 && x < node.X + node.Width - 4
                            && y > node.Y + 4 && y < node.Y + node.Height - 4;

                        Assert.False(inside, $"{view.Key}: {edge.FromAlias} to {edge.ToAlias} runs through {node.Node.Name}");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(WorkspaceFiles))]
    public void No_edge_label_of_the_committed_workspace_lands_on_a_card(string name)
    {
        var workspace = Read(name);

        foreach (var view in workspace.Views)
        {
            var diagram = C4LayoutEngine.Build(workspace, view);

            foreach (var edge in diagram.Edges.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Label)))
            {
                foreach (var node in diagram.Nodes)
                {
                    if (node.Alias == edge.FromAlias || node.Alias == edge.ToAlias) continue;

                    var inside = edge.LabelX > node.X && edge.LabelX < node.X + node.Width
                        && edge.LabelY > node.Y && edge.LabelY < node.Y + node.Height;

                    Assert.False(inside, $"{view.Key}: '{edge.Label}' sits on {node.Node.Name}");
                }
            }
        }
    }

    /// <summary>
    /// Guards the two tests above from passing by default. They walk each edge's path
    /// and check where it goes, so they only mean anything if the path is one the walker
    /// understands — and the routed edges, drawn as lines and corners rather than as a
    /// single curve, are both the hardest cases and the ones a curve-only walker skips
    /// in silence. If the committed workspace ever stops needing a routed edge this test
    /// fails, and whoever changed it can decide whether the crossing tests still earn
    /// their keep.
    /// </summary>
    [Fact]
    public void The_committed_workspace_still_exercises_routing()
    {
        var routed = Workspaces()
            .Select(file => C4DslReader.Read(File.ReadAllText(file.FullName)))
            .SelectMany(workspace => workspace.Views
                .SelectMany(view => C4LayoutEngine.Build(workspace, view).Edges))
            .Where(edge => edge.Path.Contains(" Q ", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(routed);
        Assert.All(routed, edge => Assert.True(Along(edge.Path).Count() > 8, $"{edge.FromAlias} to {edge.ToAlias} was not walked"));
    }

    private static IEnumerable<(double X, double Y)> Along(string path) => C4PathSampler.Along(path);

    // ---- the references -------------------------------------------------------

    /// <summary>
    /// Where the reference lives, and why it is not in the chapter.
    /// <para>
    /// A <c>related:</c> entry in a chapter is the natural home and the knowledge-meta
    /// generator refuses it: it resolves references against nodes built from <c>.md</c>
    /// files only, and treats an unresolvable target under <c>.arc42/</c> as an error
    /// rather than a warning. A <c>properties</c> block in the workspace or on a view
    /// keeps the link with the model and c4hero deletes it — its parser skips both and
    /// never writes them back, so the reference would vanish on the first save in the
    /// editor.
    /// </para>
    /// <para>
    /// So it is an authored file, and this asserts the consequence: no chapter carries a
    /// <c>.dsl</c> reference, because one would break the index refresh for everybody.
    /// </para>
    /// </summary>
    [Fact]
    public void No_chapter_carries_a_reference_the_knowledge_index_generator_would_reject()
    {
        var offending = Chapters()
            .Where(chapter => WorkspaceReferencePattern.IsMatch(File.ReadAllText(chapter.FullName)))
            .Select(Relative)
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void The_reference_file_parses_and_names_only_views_that_exist()
    {
        var references = References();
        Assert.NotEmpty(references);

        var keys = Read("backlog.dsl").Views.Select(view => view.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = references.Keys.Where(key => !keys.Contains(key)).ToList();

        Assert.Empty(unknown);
    }

    /// <summary>
    /// Every chapter a view claims to document has to exist. A typo here is invisible:
    /// the view still draws and the reference simply goes nowhere.
    /// </summary>
    [Fact]
    public void Every_chapter_a_view_documents_exists()
    {
        var missing = new List<string>();

        foreach (var (key, chapters) in References())
        {
            foreach (var chapter in chapters)
            {
                var path = chapter.Split('#')[0].Replace('/', Path.DirectorySeparatorChar);
                if (!File.Exists(Path.Combine(RepositoryRoot.Root.FullName, path)))
                {
                    missing.Add($"{key} documents {chapter}, which does not exist");
                }
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// A view nothing documents is a view the app can draw and no chapter admits to,
    /// which is the arrangement failing quietly — the whole point is that the two sit
    /// beside each other with references.
    /// </summary>
    [Theory]
    [MemberData(nameof(WorkspaceFiles))]
    public void Every_view_of_the_committed_workspace_documents_a_chapter(string name)
    {
        var references = References();

        var undocumented = Read(name).Views
            .Select(view => view.Key)
            .Where(key => !references.TryGetValue(key, out var chapters) || chapters.Length == 0)
            .ToList();

        Assert.Empty(undocumented);
    }

    /// <summary>
    /// The scope that was agreed: the architecture chapters carry the model, and
    /// <c>.domain</c> gets the way across to it. Both halves are asserted, because
    /// either one alone would look like the feature working.
    /// </summary>
    [Fact]
    public void The_model_documents_both_the_architecture_chapters_and_a_domain_chapter()
    {
        var documented = References().Values.SelectMany(chapters => chapters).ToList();

        Assert.Contains(documented, chapter => chapter.StartsWith(".arc42/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(documented, chapter => chapter.StartsWith(".domain/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <c>.domain</c> links to the architecture model rather than carrying one. A
    /// <c>_c4</c> folder there would be a C4 model of a bounded context, and Structurizr
    /// has no vocabulary for what a domain chapter says — no aggregate root, no value
    /// object, no multiplicity.
    /// </summary>
    [Fact]
    public void No_knowledge_folder_other_than_arc42_carries_a_C4_workspace()
    {
        var stray = new[] { ".domain", ".tech", ".design" }
            .Select(folder => new DirectoryInfo(Path.Combine(RepositoryRoot.Root.FullName, folder)))
            .Where(folder => folder.Exists)
            .SelectMany(folder => folder.EnumerateDirectories(WorkspaceFolder, SearchOption.AllDirectories))
            .Select(Relative)
            .ToList();

        Assert.Empty(stray);
    }

    /// <summary>
    /// The arc42 chapters that already carry C4 content are the ones a C4 view is most
    /// obviously a richer drawing of, so a view has to claim each of them. Stated as a
    /// test because the pairing is the feature.
    /// </summary>
    [Theory]
    [InlineData(".arc42/03-context-and-scope.md")]
    [InlineData(".arc42/05-building-block-view.md")]
    [InlineData(".arc42/07-deployment-view.md")]
    public void The_architecture_chapters_with_C4_content_are_documented_by_a_view(string chapter)
    {
        var documented = References().Values
            .SelectMany(chapters => chapters)
            .Select(entry => entry.Split('#')[0])
            .ToList();

        Assert.Contains(chapter, documented);
    }

    /// <summary>
    /// Both existing mermaid C4 fences stay exactly where they were. The feature is
    /// additive, and removing either would be the one thing this change was told not to
    /// do.
    /// </summary>
    [Theory]
    [InlineData(".arc42/03-context-and-scope.md", "C4Context")]
    [InlineData(".arc42/05-building-block-view.md", "C4Container")]
    public void The_existing_mermaid_C4_fences_are_still_in_their_chapters(string chapter, string keyword)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot.Root.FullName, chapter.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("```mermaid", text, StringComparison.Ordinal);
        Assert.Contains(keyword, text, StringComparison.Ordinal);
    }

    private static string Relative(FileSystemInfo file) =>
        Path.GetRelativePath(RepositoryRoot.Root.FullName, file.FullName).Replace('\\', '/');
}
