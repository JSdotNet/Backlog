namespace Backlog.ArchitectureTests;

/// <summary>
/// The devbook gate has two halves and they are wired in two different places.
///
/// <para><c>build.mjs --check</c> resolves the references between chapters. It
/// says nothing about the values inside a <c>meta</c> block, even though the
/// module beside it exports a <c>validateDocument</c> that does — and that export
/// had no caller anywhere in this repository, which is how
/// <c>features.md</c> in the productivity context carried <c>status: idea</c>, a word in
/// no folder's vocabulary, past a workflow step named "Check references and
/// metadata blocks" (issue #241).</para>
///
/// <para><c>tools/devbook/check-metadata.mjs</c> is the missing caller and
/// <c>.github/workflows/devbook-metadata.yml</c> is where it blocks a pull
/// request. Both are repo-native on purpose: everything under
/// <c>.devbook/_tools/devbook-meta/</c> and the workflow
/// <c>.github/workflows/devbook-meta.yml</c> are materialized by the devbook
/// plugin, which CLAUDE.md says <c>devbook:update</c> refreshes and nobody edits
/// here. The rules below are what stops the next change putting the gate back
/// inside the installed copy, where the next refresh would silently drop it.</para>
/// </summary>
public class DevbookMetadataGateTests
{
    /// <summary>The repo-native check, as CI invokes it.</summary>
    private const string CheckCommand = "node tools/devbook/check-metadata.mjs";

    /// <summary>The workflow that runs it.</summary>
    private static readonly string[] GateWorkflow =
        [".github", "workflows", "devbook-metadata.yml"];

    /// <summary>The generator the devbook plugin materializes.</summary>
    private static readonly string[] InstalledGenerator = [".devbook", "_tools", "devbook-meta"];

    /// <summary>The workflow the devbook plugin installs beside it, and what it runs.</summary>
    private static readonly string[] InstalledWorkflow = [".github", "workflows", "devbook-meta.yml"];

    private const string InstalledCheckCommand = "node .devbook/_tools/devbook-meta/build.mjs --check";


    /// <summary>The repository's own metadata check, by name stem.</summary>
    /// <remarks>
    /// Deliberately not an inventory of what the plugin currently installs. The
    /// rule is "no repository-owned file lives in the installed copy", and a
    /// refresh is expected to add and remove plugin files freely — each devbook
    /// release adds and drops <c>*.test.mjs</c> beside the generator. Pinning the
    /// file list would turn the <c>devbook:update</c> CLAUDE.md mandates into a red
    /// suite, and blame the wrong thing while doing it.
    /// </remarks>
    private const string RepositoryCheckStem = "check-metadata";

    /// <summary>What plugin-installed content looks like: source and its
    /// documentation, nothing compiled, generated, or project-shaped.</summary>
    private static readonly string[] InstalledFileExtensions = [".mjs", ".md", ".json"];

    [Fact]
    public void The_metadata_check_is_wired_into_ci()
    {
        var workflow = File.ReadAllText(RepositoryRoot.File(GateWorkflow));

        Assert.True(
            workflow.Contains(CheckCommand, StringComparison.Ordinal),
            $"{Path.Combine(GateWorkflow)} does not run '{CheckCommand}'. The check only stops a bad "
            + "status reaching main if CI actually runs it — an uncalled validator is what issue #241 "
            + "was about.");
    }

    /// <summary>
    /// The reference half of the gate is the installed workflow, and the metadata
    /// half stands beside it rather than replacing it.
    /// </summary>
    [Fact]
    public void The_installed_reference_check_is_wired_into_ci()
    {
        var workflow = File.ReadAllText(RepositoryRoot.File(InstalledWorkflow));

        Assert.True(
            workflow.Contains(InstalledCheckCommand, StringComparison.Ordinal),
            $"{Path.Combine(InstalledWorkflow)} does not run '{InstalledCheckCommand}'. That workflow is "
            + "the devbook plugin's reference check; devbook-metadata.yml checks values and relies on it "
            + "for references.");
    }


    /// <summary>
    /// The installed generator folder holds installed files only.
    ///
    /// <para>The tempting fix for issue #241 was a few lines in <c>build.mjs</c>,
    /// or a new file next to it. Either one is lost the next time
    /// <c>devbook:update</c> refreshes the plugin's tooling, and lost quietly: the
    /// gate would stop running and nothing would go red.</para>
    /// </summary>
    [Fact]
    public void The_installed_generator_folder_holds_no_repository_files()
    {
        var folder = new DirectoryInfo(RepositoryRoot.Directory(InstalledGenerator));

        var unexpected = folder.EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder.FullName, file.FullName))
            .Where(IsRepositoryOwned)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"{string.Join(", ", unexpected)} sits inside the installed copy of the devbook-meta "
            + "generator. Everything in that folder is replaced wholesale by the next devbook:update, so "
            + "repository-owned tooling belongs beside it — tools/devbook/ — not in it.");
    }

    /// <summary>
    /// The rule above survives the refresh CLAUDE.md mandates.
    ///
    /// <para>Every devbook release adds and drops the generator's own
    /// <c>*.test.mjs</c> and helper modules. An allowlist of today's files would turn
    /// a correct <c>devbook:update</c> red and blame it on "repository-owned
    /// tooling", sending the developer to delete upstream's own tests. This pins the
    /// rule to the shape of a plugin file instead.</para>
    /// </summary>
    [Fact]
    public void A_generator_refresh_does_not_trip_the_folder_rule()
    {
        string[] afterRefresh =
        [
            "README.md", "annotations.mjs", "annotations-index.mjs", "build.mjs", "chapter-hash.mjs",
            "graph.mjs", "metadata.mjs", "outline.mjs", "schema-gate.test.mjs",
            "status-optional.test.mjs", "some-future-helper.mjs", "some-future-case.test.mjs"
        ];

        var misread = afterRefresh.Where(IsRepositoryOwned).ToArray();

        Assert.True(
            misread.Length == 0,
            $"A devbook:update of the plugin tooling would report [{string.Join(", ", misread)}] as "
            + "repository-owned. Those are upstream's files; the rule has been narrowed back to an "
            + "inventory and now blocks the only sanctioned way to update the generator.");

        // And it still does the job it is there for.
        Assert.True(IsRepositoryOwned("check-metadata.mjs"));
        Assert.True(IsRepositoryOwned("check-metadata.test.mjs"));
        Assert.True(IsRepositoryOwned("gate.exe"));
    }

    /// <summary>
    /// Whether a file in the installed folder is this repository's rather than the
    /// plugin's: it carries the check's own name, or it is not the kind of file the
    /// plugin ships. An upstream file added by a refresh satisfies neither.
    /// </summary>
    private static bool IsRepositoryOwned(string relativeName)
    {
        var name = Path.GetFileName(relativeName);

        return name.StartsWith(RepositoryCheckStem, StringComparison.OrdinalIgnoreCase)
            || !InstalledFileExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The technology chapter describes the gate that exists.
    ///
    /// <para>It said CI "fails on an unresolvable reference or an invalid
    /// <c>meta</c> block" while only the first half was true, which is the kind of
    /// documentation that keeps a missing check missing.</para>
    /// </summary>
    [Fact]
    public void The_generator_chapter_names_both_hard_failures()
    {
        var chapter = File.ReadAllText(RepositoryRoot.File(".devbook", "tech", "tooling.md"));

        foreach (var mention in new[] { "tools/devbook/check-metadata.mjs", "devbook-metadata.yml" })
        {
            Assert.True(
                chapter.Contains(mention, StringComparison.Ordinal),
                $".devbook/tech/tooling.md does not mention {mention}. The devbook-meta Generator chapter is "
                + "where the repository says what CI enforces, and a reader who trusts it would still "
                + "believe metadata values go unchecked.");
        }
    }
}
