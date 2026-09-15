using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The four forms a reader may ask a snapshot for, pinned one by one, because
/// every reader in the Devbook module spells its needs in them.
/// </summary>
public class DevbookSnapshotSelectionTests
{
    private static readonly DevbookSnapshotEntry[] Index =
    [
        new(".arc42", "t", true),
        new(".arc42/01-intro.md", "b1", false),
        new(".arc42/_reading-order.json", "b2", false),
        new(".arc42/_archify", "t", true),
        new(".arc42/_archify/01-intro.1.workflow.html", "b3", false),
        new(".arc42/adr/0001-x.md", "b4", false),
        new(".domain/inbox/_reading-order.json", "b5", false),
        new("AGENTS.md", "b6", false),
        new("src/Inbox/AGENTS.md", "b7", false),
        new("src/Inbox/bin/AGENTS.md", "b8", false)
    ];

    private static string[] Paths(IReadOnlyCollection<string> selection) =>
        [.. DevbookSnapshotSelection.Select(Index, selection).Select(entry => entry.Path)];

    [Fact]
    public void A_file_names_itself()
    {
        Assert.Equal([".arc42/01-intro.md"], Paths([".arc42/01-intro.md"]));
    }

    [Fact]
    public void A_trailing_slash_names_a_subtree()
    {
        Assert.Equal(
            [".arc42/01-intro.md", ".arc42/_reading-order.json", ".arc42/_archify/01-intro.1.workflow.html", ".arc42/adr/0001-x.md"],
            Paths([".arc42/"]));
    }

    [Fact]
    public void The_root_subtree_is_everything()
    {
        Assert.Equal(Index.Count(entry => !entry.IsDirectory), Paths(["/"]).Length);
    }

    [Fact]
    public void A_double_star_prefix_names_a_suffix_at_any_depth()
    {
        Assert.Equal(
            [".arc42/_reading-order.json", ".domain/inbox/_reading-order.json"],
            Paths(["**/_reading-order.json"]));

        Assert.Equal(["AGENTS.md", "src/Inbox/AGENTS.md", "src/Inbox/bin/AGENTS.md"], Paths(["**/AGENTS.md"]));
    }

    [Fact]
    public void An_exclusion_drops_every_path_through_that_directory()
    {
        Assert.Equal(
            [".arc42/01-intro.md", ".arc42/_reading-order.json", ".arc42/adr/0001-x.md"],
            Paths([".arc42/", "!_archify"]));

        Assert.Equal(["AGENTS.md", "src/Inbox/AGENTS.md"], Paths(["**/AGENTS.md", "!bin"]));
    }

    /// <summary>Directories are never selected — there is nothing to fetch for
    /// one — and a selection with nothing to include selects nothing, however
    /// many exclusions it carries.</summary>
    [Fact]
    public void Directories_and_bare_exclusions_select_nothing()
    {
        Assert.Empty(Paths([".arc42"]));
        Assert.Empty(Paths(["!_archify"]));
    }

    [Fact]
    public void Spellings_are_normalized_to_the_index_s_own()
    {
        Assert.Equal([".arc42/01-intro.md"], Paths(["./.arc42\\01-intro.md"]));
        Assert.Equal(".arc42/", DevbookSnapshotSelection.Subtree("/.arc42/"));
        Assert.Equal("/", DevbookSnapshotSelection.Subtree(""));
        Assert.Equal("**/_meta/index.json", DevbookSnapshotSelection.AnyDepth("_meta\\index.json"));
        Assert.Equal("!_archify", DevbookSnapshotSelection.Exclude("_archify/"));
    }
}
