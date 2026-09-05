namespace Backlog.ArchitectureTests;

/// <summary>
/// The knowledge gate has two halves and they are wired in two different places.
///
/// <para><c>build.mjs --check</c> resolves the references between chapters. It
/// says nothing about the values inside a <c>meta</c> block, even though the
/// module beside it exports a <c>validateDocument</c> that does — and that export
/// had no caller anywhere in this repository, which is how
/// <c>.domain/productivity/features.md</c> carried <c>status: idea</c>, a word in
/// no folder's vocabulary, past a workflow step named "Check references and
/// metadata blocks" (issue #241).</para>
///
/// <para><c>tools/knowledge/check-metadata.mjs</c> is the missing caller and
/// <c>.github/workflows/knowledge-metadata.yml</c> is where it blocks a pull
/// request. Both are repo-native on purpose: everything under
/// <c>.github/tools/knowledge-meta/</c>, both <c>knowledge-meta*</c> workflows and
/// <c>build/Update-KnowledgeIndex.ps1</c> are installed copies of the
/// knowledge-base plugin's tooling, which CLAUDE.md says to re-sync and never edit
/// here. The rules below are what stops the next change putting the gate back
/// inside the installed copy, where the next re-sync would silently drop it.</para>
/// </summary>
public class KnowledgeMetadataGateTests
{
    /// <summary>The repo-native check, as CI invokes it.</summary>
    private const string CheckCommand = "node tools/knowledge/check-metadata.mjs";

    /// <summary>The workflow that runs it.</summary>
    private static readonly string[] GateWorkflow =
        [".github", "workflows", "knowledge-metadata.yml"];

    /// <summary>The installed workflow it sits beside, which stays untouched.</summary>
    private static readonly string[] InstalledWorkflow =
        [".github", "workflows", "knowledge-meta.yml"];

    /// <summary>The repository's own metadata check, by name stem.</summary>
    /// <remarks>
    /// Deliberately not an inventory of what the plugin currently installs. The
    /// rule is "no repository-owned file lives in the installed copy", and a
    /// re-sync is expected to add and remove plugin files freely — 0.16.0 ships
    /// five <c>*.test.mjs</c> alongside the generator that the version installed
    /// here does not. Pinning the file list would turn the re-sync CLAUDE.md
    /// mandates into a red suite, and blame the wrong thing while doing it.
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
    /// Index drift stays a warning.
    ///
    /// <para>Refresh of the generated <c>_meta</c> indexes is deliberate rather
    /// than per-pull-request, because making every knowledge change carry a
    /// regenerated index is what turns those files into merge conflicts. Adding a
    /// hard failure for metadata values is not an excuse to harden the drift step
    /// on the way past, so this pins the split.</para>
    /// </summary>
    [Fact]
    public void The_index_drift_report_stays_advisory()
    {
        var workflow = File.ReadAllText(RepositoryRoot.File(InstalledWorkflow));

        Assert.True(
            workflow.Contains("::warning::", StringComparison.Ordinal),
            $"{Path.Combine(InstalledWorkflow)} no longer reports index drift as a '::warning::'. "
            + "Drift is advisory by design — see knowledge-derived-artifacts.instructions.md — and the "
            + "nightly refresh is the other half of that trade.");
    }

    /// <summary>
    /// The installed generator folder holds installed files only.
    ///
    /// <para>The tempting fix for issue #241 was a few lines in <c>build.mjs</c>,
    /// or a new file next to it. Either one is lost the next time the plugin's
    /// tooling is re-synced, and lost quietly: the gate would stop running and
    /// nothing would go red.</para>
    /// </summary>
    [Fact]
    public void The_installed_generator_folder_holds_no_repository_files()
    {
        var folder = new DirectoryInfo(RepositoryRoot.Directory(".github", "tools", "knowledge-meta"));

        var unexpected = folder.EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder.FullName, file.FullName))
            .Where(IsRepositoryOwned)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"{string.Join(", ", unexpected)} sits inside the installed copy of the knowledge-meta "
            + "generator. Everything in that folder is replaced wholesale on the next re-sync, so "
            + "repository-owned tooling belongs beside it — tools/knowledge/ — not in it.");
    }

    /// <summary>
    /// The rule above survives the re-sync CLAUDE.md mandates.
    ///
    /// <para>The version installed here ships five files; plugin 0.16.0 ships ten,
    /// having added the generator's own <c>*.test.mjs</c>. An allowlist of the
    /// current five would turn a correct re-sync red and blame it on
    /// "repository-owned tooling", sending the developer to delete upstream's own
    /// tests. This pins the rule to the shape of a plugin file instead.</para>
    /// </summary>
    [Fact]
    public void A_generator_resync_does_not_trip_the_folder_rule()
    {
        string[] afterResync =
        [
            "README.md", "annotation-fence.test.mjs", "build.mjs", "escape-lint.test.mjs",
            "graph.mjs", "metadata.mjs", "outline.mjs", "status-config.test.mjs",
            "status-optional.test.mjs", "tests-field.test.mjs"
        ];

        var misread = afterResync.Where(IsRepositoryOwned).ToArray();

        Assert.True(
            misread.Length == 0,
            $"A re-sync to the current plugin tooling would report [{string.Join(", ", misread)}] as "
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
    /// plugin ships. An upstream file added by a re-sync satisfies neither.
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
        var chapter = File.ReadAllText(RepositoryRoot.File(".tech", "tooling.md"));

        foreach (var mention in new[] { "tools/knowledge/check-metadata.mjs", "knowledge-metadata.yml" })
        {
            Assert.True(
                chapter.Contains(mention, StringComparison.Ordinal),
                $".tech/tooling.md does not mention {mention}. The knowledge-meta Generator chapter is "
                + "where the repository says what CI enforces, and a reader who trusts it would still "
                + "believe metadata values go unchecked.");
        }
    }
}
