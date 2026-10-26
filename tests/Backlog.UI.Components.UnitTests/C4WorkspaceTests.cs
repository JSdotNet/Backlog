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
    /// <summary>devbook's contract 33: one workspace beside the architecture chapters.</summary>
    private const string WorkspaceFile = "workspace.dsl";

    /// <summary>The only folder the workspace may <c>!include</c> from.</summary>
    private const string IncludeFolder = "c4";

    /// <summary>The folder that held the workspaces before contract 33, and still holds
    /// the authored reference map.</summary>
    private const string ReferenceFolder = "_c4";

    /// <summary>A reference to a workspace as a chapter would spell one. Present in a
    /// chapter it is a problem rather than a feature — see the test that says why.</summary>
    private static readonly Regex WorkspaceReferencePattern =
        new(@"(?:\.devbook/arc42|\.arc42)/(?:_c4/|c4/)?[A-Za-z0-9._-]+\.dsl", RegexOptions.Compiled);

    /// <summary>How a chapter shows a view under contract 33: <c>![…](c4:&lt;view key&gt;)</c>.</summary>
    private static readonly Regex EmbedPattern =
        new(@"!\[[^\]]*\]\(c4:(?<key>[A-Za-z0-9._-]+)\)", RegexOptions.Compiled);

    private static string Arc42Folder => Path.Combine(RepositoryRoot.Root.FullName, ".devbook", "arc42");

    /// <summary>The authored view-to-chapters map beside the workspace.</summary>
    private static Dictionary<string, string[]> References()
    {
        var path = Path.Combine(Arc42Folder, ReferenceFolder, "references.json");
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
        var file = new FileInfo(Path.Combine(Arc42Folder, WorkspaceFile));
        return file.Exists ? [file] : [];
    }

    /// <summary>The chapters each view is embedded in, by view key — the contract 33
    /// statement of which chapter a view documents.</summary>
    private static Dictionary<string, List<string>> Embeds()
    {
        var embeds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var chapter in Chapters())
        {
            foreach (Match match in EmbedPattern.Matches(File.ReadAllText(chapter.FullName)))
            {
                var key = match.Groups["key"].Value;
                if (!embeds.TryGetValue(key, out var chapters)) embeds[key] = chapters = [];
                chapters.Add(Relative(chapter));
            }
        }

        return embeds;
    }

    private static IEnumerable<FileInfo> Chapters()
    {
        foreach (var knowledge in new[] { ".devbook/arc42", ".devbook/domain" })
        {
            var folder = new DirectoryInfo(Path.Combine([RepositoryRoot.Root.FullName, .. knowledge.Split('/')]));
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
        C4DslReader.Read(File.ReadAllText(Path.Combine(Arc42Folder, name)));

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
    /// A <c>related:</c> entry in a chapter is the natural home and the devbook-meta
    /// checker refuses it: it resolves references against nodes built from <c>.md</c>
    /// files only, and treats an unresolvable target under <c>.devbook/arc42/</c> as an error
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

        var keys = Read(WorkspaceFile).Views.Select(view => view.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
    /// beside each other with references. Either statement counts: a chapter embedding
    /// the view as <c>![…](c4:&lt;key&gt;)</c>, or the authored reference map naming it.
    /// </summary>
    [Theory]
    [MemberData(nameof(WorkspaceFiles))]
    public void Every_view_of_the_committed_workspace_documents_a_chapter(string name)
    {
        var references = References();
        var embeds = Embeds();

        var undocumented = Read(name).Views
            .Select(view => view.Key)
            .Where(key => !embeds.ContainsKey(key))
            .Where(key => !references.TryGetValue(key, out var chapters) || chapters.Length == 0)
            .ToList();

        Assert.Empty(undocumented);
    }

    /// <summary>Every embed has to name a view the workspace declares. A typo in one is
    /// a chapter showing nothing where its picture should be.</summary>
    [Fact]
    public void Every_embedded_view_exists_in_the_committed_workspace()
    {
        var keys = Read(WorkspaceFile).Views.Select(view => view.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = Embeds()
            .Where(pair => !keys.Contains(pair.Key))
            .Select(pair => $"{string.Join(", ", pair.Value)} embeds c4:{pair.Key}, which the workspace does not declare")
            .ToList();

        Assert.Empty(unknown);
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

        Assert.Contains(documented, chapter => chapter.StartsWith(".devbook/arc42/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(documented, chapter => chapter.StartsWith(".devbook/domain/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <c>.domain</c> links to the architecture model rather than carrying one. A
    /// workspace there would be a C4 model of a bounded context, and Structurizr has no
    /// vocabulary for what a domain chapter says — no aggregate root, no value object,
    /// no multiplicity.
    /// </summary>
    [Fact]
    public void No_knowledge_folder_other_than_arc42_carries_a_C4_workspace()
    {
        var folders = new[] { ".devbook/domain", ".devbook/tech", ".devbook/design", ".devbook/ai" }
            .Select(folder => new DirectoryInfo(Path.Combine([RepositoryRoot.Root.FullName, .. folder.Split('/')])))
            .Where(folder => folder.Exists)
            .ToList();

        var stray = folders
            .SelectMany(folder => folder.EnumerateFiles("*.dsl", SearchOption.AllDirectories))
            .Cast<FileSystemInfo>()
            .Concat(folders.SelectMany(folder => folder.EnumerateDirectories(ReferenceFolder, SearchOption.AllDirectories)))
            .Select(Relative)
            .ToList();

        Assert.Empty(stray);
    }

    /// <summary>Includes, when the workspace has any, come from <c>c4/</c> and nowhere
    /// else — the only folder devbook lets a workspace include from — so a <c>.dsl</c>
    /// anywhere else under the architecture folder is a model nothing reads.</summary>
    [Fact]
    public void Every_dsl_under_the_architecture_folder_is_the_workspace_or_one_it_includes_from_c4()
    {
        var workspace = Path.Combine(Arc42Folder, WorkspaceFile);
        var includes = Path.Combine(Arc42Folder, IncludeFolder) + Path.DirectorySeparatorChar;

        var stray = new DirectoryInfo(Arc42Folder)
            .EnumerateFiles("*.dsl", SearchOption.AllDirectories)
            .Where(file => !string.Equals(file.FullName, workspace, StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.FullName.StartsWith(includes, StringComparison.OrdinalIgnoreCase))
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
    [InlineData(".devbook/arc42/03-context-and-scope.md")]
    [InlineData(".devbook/arc42/05-building-block-view.md")]
    [InlineData(".devbook/arc42/07-deployment-view.md")]
    public void The_architecture_chapters_with_C4_content_are_documented_by_a_view(string chapter)
    {
        var documented = References().Values
            .SelectMany(chapters => chapters)
            .Select(entry => entry.Split('#')[0])
            .ToList();

        Assert.Contains(chapter, documented);
    }

    /// <summary>
    /// The two chapters whose mermaid C4 fences devbook's migration 033 moved into the
    /// workspace now embed the view each fence became, and the workspace declares it.
    /// Losing either embed would leave the chapter with no picture at all.
    /// </summary>
    [Theory]
    [InlineData(".devbook/arc42/03-context-and-scope.md", "business-context")]
    [InlineData(".devbook/arc42/05-building-block-view.md", "container-view")]
    public void The_migrated_chapters_embed_the_view_their_fence_became(string chapter, string viewKey)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot.Root.FullName, chapter.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains(
            viewKey,
            EmbedPattern.Matches(text).Select(match => match.Groups["key"].Value),
            StringComparer.OrdinalIgnoreCase);
        Assert.NotNull(Read(WorkspaceFile).View(viewKey));
    }

    private static string Relative(FileSystemInfo file) =>
        Path.GetRelativePath(RepositoryRoot.Root.FullName, file.FullName).Replace('\\', '/');
}
