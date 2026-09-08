namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The reading order the knowledge panes ask for, and the four ways it can fail
/// to arrive.
///
/// <para>Three panes call <c>ForFolder</c> and all three treat an empty list as
/// "sort it yourself" — <c>TechnologyKnowledge</c>, <c>DesignKnowledge</c> and
/// <c>DomainKnowledgeStore</c>. So the failure paths are not defensive
/// decoration here, they are the contract those three are written against, and a
/// throw would take down a pane the user is looking at. Each of them gets its own
/// case: a missing file, a malformed one, and a <c>version</c> from tooling this
/// build has never met.</para>
/// </summary>
public sealed class KnowledgeReadingOrderTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public void Reads_the_root_document_first_and_then_the_declared_order()
    {
        var folder = WriteOrder(".tech", """
            {
              "version": 1,
              "scope": ".tech",
              "directories": {
                ".tech": {
                  "root": "technology-graph.md",
                  "order": ["shared.md", "desktop.md", "ide.md"]
                }
              }
            }
            """);

        Assert.Equal(
            ["technology-graph.md", "shared.md", "desktop.md", "ide.md"],
            KnowledgeReadingOrder.ForFolder(folder));
    }

    [Fact]
    public void Reads_subdirectories_and_sibling_files_alike()
    {
        // .domain orders bounded contexts, which are directories, after a root
        // document that is a file. The list is names, not paths, and the caller
        // decides what a name means in its own folder.
        var folder = WriteOrder(".domain", """
            {
              "version": 1,
              "scope": ".domain",
              "directories": {
                ".domain": { "root": "context-map.md", "order": ["inbox", "capture", "tasks"] },
                ".domain/inbox": { "root": "domain.md", "order": ["features.md"] }
              }
            }
            """);

        // The nested declaration is for the generator resolving the whole
        // outline; this reader answers about the folder it was handed.
        Assert.Equal(
            ["context-map.md", "inbox", "capture", "tasks"],
            KnowledgeReadingOrder.ForFolder(folder));
    }

    [Fact]
    public void A_folder_that_declares_no_root_document_declares_no_order()
    {
        // .arc42's numbered chapters sort themselves. Rule 5 of the reading
        // order: no root document means no declared order at all.
        var folder = WriteOrder(".arc42", """
            {
              "version": 1,
              "scope": ".arc42",
              "directories": {
                ".arc42": { "root": null, "order": [] },
                ".arc42/adr": { "root": "README.md", "order": ["0001-first.md"] }
              }
            }
            """);

        Assert.Empty(KnowledgeReadingOrder.ForFolder(folder));
    }

    [Fact]
    public void A_missing_file_is_an_empty_order()
    {
        var folder = Path.Combine(TempDir(), ".tech");
        Directory.CreateDirectory(folder);

        Assert.Empty(KnowledgeReadingOrder.ForFolder(folder));
    }

    [Fact]
    public void A_malformed_file_is_an_empty_order()
    {
        var folder = WriteOrder(".tech", "{ \"version\": 1, \"directories\": ");

        Assert.Empty(KnowledgeReadingOrder.ForFolder(folder));
    }

    [Fact]
    public void An_unknown_version_is_an_empty_order()
    {
        // Written by tooling this build has never met. The shape below is one it
        // would otherwise read perfectly, which is the point: the version is
        // refused on its own, not because the rest failed to parse.
        var folder = WriteOrder(".tech", """
            {
              "version": 2,
              "scope": ".tech",
              "directories": {
                ".tech": { "root": "technology-graph.md", "order": ["shared.md"] }
              }
            }
            """);

        Assert.Empty(KnowledgeReadingOrder.ForFolder(folder));
    }

    [Fact]
    public void A_missing_version_is_an_empty_order()
    {
        var folder = WriteOrder(".tech", """
            {
              "scope": ".tech",
              "directories": { ".tech": { "root": "technology-graph.md", "order": ["shared.md"] } }
            }
            """);

        Assert.Empty(KnowledgeReadingOrder.ForFolder(folder));
    }

    [Fact]
    public void A_file_that_describes_another_scope_is_an_empty_order()
    {
        // The file names the scope it describes. One that names a scope its own
        // `directories` does not declare is not a folder this reader can order,
        // and inferring one from the path would be a guess.
        var folder = WriteOrder(".tech", """
            {
              "version": 1,
              "scope": ".design",
              "directories": { ".tech": { "root": "technology-graph.md", "order": ["shared.md"] } }
            }
            """);

        Assert.Empty(KnowledgeReadingOrder.ForFolder(folder));
    }

    [Fact]
    public void Entries_that_are_not_strings_are_skipped_rather_than_failing_the_folder()
    {
        var folder = WriteOrder(".tech", """
            {
              "version": 1,
              "scope": ".tech",
              "directories": {
                ".tech": { "root": "technology-graph.md", "order": ["shared.md", 7, null, "  ", "desktop.md"] }
              }
            }
            """);

        Assert.Equal(
            ["technology-graph.md", "shared.md", "desktop.md"],
            KnowledgeReadingOrder.ForFolder(folder));
    }

    [Fact]
    public void A_blank_folder_path_is_an_empty_order()
    {
        Assert.Empty(KnowledgeReadingOrder.ForFolder("   "));
    }

    [Fact]
    public void The_committed_files_in_this_repository_order_their_folders()
    {
        // The one case pointed at the real corpus rather than a fixture: the
        // panes read the files that are actually committed, and a migration that
        // produced a file this reader cannot read would pass every case above.
        var repoRoot = RepositoryRoot();
        if (repoRoot is null) return; // packaged test run, no repository beside it

        Assert.Equal(
            "technology-graph.md",
            KnowledgeReadingOrder.ForFolder(Path.Combine(repoRoot, ".tech")).FirstOrDefault());
        Assert.Equal(
            "README.md",
            KnowledgeReadingOrder.ForFolder(Path.Combine(repoRoot, ".design")).FirstOrDefault());

        var domain = KnowledgeReadingOrder.ForFolder(Path.Combine(repoRoot, ".domain"));
        Assert.Equal("context-map.md", domain.FirstOrDefault());
        Assert.Contains("inbox", domain);
    }

    /// <summary>The repository root, found by walking up from the test binary
    /// until a folder holding both <c>.domain</c> and <c>.tech</c> appears.</summary>
    private static string? RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".domain"))
                && Directory.Exists(Path.Combine(dir.FullName, ".tech")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    /// <summary>A knowledge folder in a throwaway directory, holding
    /// <paramref name="json"/> as its <c>_reading-order.json</c>.</summary>
    private string WriteOrder(string folderName, string json)
    {
        var folder = Path.Combine(TempDir(), folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "_reading-order.json"), json);
        return folder;
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "knowledge-reading-order-tests", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(path);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
