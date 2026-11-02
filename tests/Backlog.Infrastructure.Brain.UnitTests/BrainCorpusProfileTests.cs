namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// The Brain's corpus profile (local ADR 0040 §2): the folders Devbook's builder
/// reads the Brain under, the inputs and tag its fingerprint records, and the
/// fields a note's node carries — one <c>para</c> per node, the folder unless the
/// block says otherwise, plus <c>repos</c> and <c>tasks</c>
/// (<c>.devbook/domain/brain/domain.invariants.md</c>, re-homed from the builder).
/// </summary>
public class BrainCorpusProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-brain-profile", Guid.NewGuid().ToString("N"));

    public BrainCorpusProfileTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void A_file_node_gets_its_folder_as_its_para()
    {
        var node = new FakeNode();

        BrainCorpusProfile.Instance.ApplyNodeFields(node, "work");

        Assert.Equal(["work"], node.Added["para"]);
    }

    [Fact]
    public void A_blocks_para_overrides_the_folder()
    {
        var node = new FakeNode { ["para"] = "projects" };

        BrainCorpusProfile.Instance.ApplyNodeFields(node, "work");

        Assert.Equal(["projects"], node.Added["para"]);
    }

    [Fact]
    public void A_block_naming_an_empty_para_gets_none_rather_than_the_folder()
    {
        var node = new FakeNode { ["para"] = "" };

        BrainCorpusProfile.Instance.ApplyNodeFields(node, "work");

        Assert.False(node.Added.ContainsKey("para"));
    }

    [Fact]
    public void A_heading_carries_a_para_only_when_its_block_says_so()
    {
        var bare = new FakeNode();
        var named = new FakeNode { ["para"] = "areas" };

        BrainCorpusProfile.Instance.ApplyNodeFields(bare, folder: null);
        BrainCorpusProfile.Instance.ApplyNodeFields(named, folder: null);

        Assert.Empty(bare.Added);
        Assert.Equal(["areas"], named.Added["para"]);
    }

    [Fact]
    public void Repos_and_tasks_appear_only_when_the_block_names_them()
    {
        var without = new FakeNode();
        var with = new FakeNode { Lists = { ["repos"] = ["backlog"], ["tasks"] = ["t-1", "t-2"] } };

        BrainCorpusProfile.Instance.ApplyNodeFields(without, "work");
        BrainCorpusProfile.Instance.ApplyNodeFields(with, "work");

        Assert.Equal(["para"], without.Added.Keys);
        Assert.Equal(["para", "repos", "tasks"], with.Added.Keys);
        Assert.Equal(["backlog"], with.Added["repos"]);
        Assert.Equal(["t-1", "t-2"], with.Added["tasks"]);
    }

    [Fact]
    public void The_folders_are_the_declared_ones_first_then_the_discovered_ones()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Work | work |\n| Design | design |\n");
        Write("work/standup.md", "# Standup\n");
        Write("design/palette.md", "# Palette\n");
        Write("archive/old.md", "# Old\n");

        Assert.Equal(["work", "design", "archive"], BrainCorpusProfile.Instance.Folders(_root));
    }

    [Fact]
    public void An_ignored_folder_or_a_dot_folder_is_never_read()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n- `work`\n\n## Ignore\n\n- `_templates`\n");
        Write("work/standup.md", "# Standup\n");
        Write("_templates/note.md", "# Template\n");
        Write(".obsidian/workspace.md", "# Workspace\n");

        Assert.Equal(["work"], BrainCorpusProfile.Instance.Folders(_root));
    }

    [Fact]
    public void A_declared_folder_missing_from_disk_is_left_out()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n- `work`\n- `journal`\n");
        Write("work/standup.md", "# Standup\n");

        Assert.Equal(["work"], BrainCorpusProfile.Instance.Folders(_root));
    }

    [Fact]
    public void Its_extra_input_is_the_conventions_file_and_its_tag_is_brain()
    {
        Assert.Equal(["CONVENTIONS.md"], BrainCorpusProfile.Instance.ExtraInputs(_root));
        Assert.Equal("brain", BrainCorpusProfile.Instance.FingerprintTag);
        Assert.True(BrainCorpusProfile.Instance.ReadsCloudPlaceholders);
        Assert.True(BrainCorpusProfile.Instance.ReadsCrossCorpusLinks);
        Assert.False(BrainCorpusProfile.Instance.HasFolderConventions);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A scratch folder left behind costs disk, not correctness.
        }

        GC.SuppressFinalize(this);
    }

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A node whose block names the scalars and lists a test sets, and
    /// which records the attributes added to it in order.</summary>
    private sealed class FakeNode : IDevbookNodeFields
    {
        private readonly Dictionary<string, string> _scalars = new(StringComparer.Ordinal);

        public Dictionary<string, IReadOnlyList<string>> Lists { get; } = new(StringComparer.Ordinal);

        public OrderedDictionary<string, IReadOnlyList<string>> Added { get; } = new(StringComparer.Ordinal);

        public string this[string field]
        {
            set => _scalars[field] = value;
        }

        public bool Has(string field) => _scalars.ContainsKey(field) || Lists.ContainsKey(field);

        public string? Scalar(string field) => _scalars.GetValueOrDefault(field);

        public IReadOnlyList<string> List(string field) => Lists.GetValueOrDefault(field) ?? [];

        public void AddAttribute(string name, IReadOnlyList<string> values) => Added.Add(name, values);
    }
}
