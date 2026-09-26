using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The rail drawn for a repository read from a branch: out of the branch's
/// index, before a single chapter has been fetched, and without asking for one.
/// </summary>
public sealed class DevbookMenuBranchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devbook-menu-branch-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task The_menu_of_a_branch_is_listed_from_the_index_with_nothing_on_disk()
    {
        var source = new IndexedBranchSource(_root,
            ".domain/context-map.md",
            ".domain/inbox/model.md",
            ".domain/inbox/domain.md",
            ".domain/inbox/actors.md",
            ".domain/_meta/index.json",
            ".design/color-scheme.md");

        var tree = await new DevbookMenu(source).LoadAsync(["domain", "design"], "backlog", TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(_root));

        var domain = Assert.Single(tree.Roots, node => node.AreaKey == "domain");
        Assert.True(domain.Available);
        Assert.Equal("context-map.md", domain.Children.First().Path);
        var inbox = Assert.Single(domain.Children, node => node.Kind == DevbookMenuNodeKind.Folder && node.Path == "inbox");
        // Ordered by the convention from the index's names alone — nothing about
        // the order is fetched, because nothing about it is authored.
        Assert.Equal(["inbox/domain.md", "inbox/actors.md", "inbox/model.md"], inbox.Children.Select(node => node.Path));
        Assert.DoesNotContain(domain.Children, node => node.Path.StartsWith("_meta", StringComparison.OrdinalIgnoreCase));

        var design = Assert.Single(tree.Roots, node => node.AreaKey == "design");
        Assert.Equal(["color-scheme.md"], design.Children.Select(node => node.Path));
    }

    /// <summary>Listing is what a menu asks for, and only listing: a chapter is
    /// fetched when a panel opens the area, never to draw the rail.</summary>
    [Fact]
    public async Task The_menu_prepares_the_listing_and_never_the_content()
    {
        var source = new IndexedBranchSource(_root, ".arc42/01-intro.md");

        await new DevbookMenu(source).LoadAsync(["arc42", "domain"], "backlog", TestContext.Current.CancellationToken);

        Assert.Equal([".domain", ".arc42"], source.ListingsPrepared);
        Assert.Empty(source.ContentPrepared);
    }

    [Fact]
    public async Task The_instructions_area_of_a_branch_lists_its_agent_folders_from_the_index()
    {
        var source = new IndexedBranchSource(_root,
            "CLAUDE.md",
            ".github/copilot-instructions.md",
            ".agents/rules/naming.md",
            "src/Program.cs");

        var tree = await new DevbookMenu(source).LoadAsync(["instructions"], "backlog", TestContext.Current.CancellationToken);

        var instructions = Assert.Single(tree.Roots);
        Assert.True(instructions.Available);

        var github = Assert.Single(instructions.Children, node => node.Path == ".github");
        Assert.Contains(github.Children, node => node.Path == ".github/copilot-instructions.md");

        var agents = Assert.Single(instructions.Children, node => node.Path == ".agent");
        Assert.True(agents.Available);
        Assert.Contains(agents.Children, folder => folder.Children.Any(node => node.Path == ".agent/rules/naming.md"));

        Assert.Contains(instructions.Children, node => node.Kind == DevbookMenuNodeKind.File && node.Path == "CLAUDE.md");
    }

    [Fact]
    public async Task A_listing_that_cannot_be_prepared_reports_why()
    {
        var source = new IndexedBranchSource(_root, ".arc42/01-intro.md") { Failure = "GitHub refused the listing." };

        var tree = await new DevbookMenu(source).LoadAsync(["arc42"], "backlog", TestContext.Current.CancellationToken);

        var arc42 = Assert.Single(tree.Roots);
        Assert.False(arc42.Available);
        Assert.Equal("GitHub refused the listing.", arc42.Message);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A branch whose index is known and whose files are not here:
    /// every folder the index names resolves, the tree is the index, and both
    /// prepare calls are counted rather than performed.</summary>
    private sealed class IndexedBranchSource : IDevbookFolderSource
    {
        private readonly string _root;
        private readonly DevbookSnapshotFileTree _tree;

        public IndexedBranchSource(string root, params string[] files)
        {
            _root = root;

            var entries = new List<DevbookSnapshotEntry>();
            var directories = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in files)
            {
                var parts = file.Split('/');
                for (var depth = 1; depth < parts.Length; depth++)
                {
                    var directory = string.Join('/', parts[..depth]);
                    if (directories.Add(directory)) entries.Add(new DevbookSnapshotEntry(directory, "tree", true));
                }

                entries.Add(new DevbookSnapshotEntry(file, "blob", false));
            }

            _tree = new DevbookSnapshotFileTree(root, entries);
        }

        public string? Failure { get; init; }

        public List<string> ListingsPrepared { get; } = [];

        public List<string> ContentPrepared { get; } = [];

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public void NotifyContentChanged()
        {
        }

        public string StorageDirectory => _root;

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => DevbookFolderSetting.Defaults();

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
        {
            var folder = DevbookFolderSetting.Defaults().Single(setting => string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase));
            // The fixtures sit in the root layout, which the real source reaches
            // through its fallback to the legacy folder.
            folder = folder with { ResolvedPath = folder.LegacyRelativePath };
            var fullPath = Path.Combine(_root, folder.EffectivePath.Replace('/', Path.DirectorySeparatorChar));

            return _tree.DirectoryExists(fullPath)
                ? new DevbookFolderLocation(key, true, null, "JSdotNet/Backlog", folder, fullPath, _root, "JSdotNet/Backlog (main)", repositoryAlias, DevbookSourceKind.Branch)
                : DevbookFolderLocation.Unavailable(key, $"main has no {folder.DisplayName} folder.", "JSdotNet/Backlog", folder, fullPath, _root, repositoryAlias: repositoryAlias, source: DevbookSourceKind.Branch);
        }

        public Task<DevbookFolderLocation> PrepareListingAsync(string key, string? repositoryAlias = null, CancellationToken cancellationToken = default)
        {
            ListingsPrepared.Add(key);

            return Task.FromResult(Failure is null
                ? Resolve(key, repositoryAlias)
                : DevbookFolderLocation.Unavailable(key, Failure, repositoryAlias: repositoryAlias, source: DevbookSourceKind.Branch));
        }

        public Task<DevbookFolderLocation> PrepareContentAsync(
            string key,
            string? repositoryAlias = null,
            IReadOnlyCollection<string>? relativePaths = null,
            CancellationToken cancellationToken = default)
        {
            ContentPrepared.Add(key);
            return Task.FromResult(Resolve(key, repositoryAlias));
        }

        public IDevbookFileTree FileTree(DevbookFolderLocation location) => _tree;
    }
}
