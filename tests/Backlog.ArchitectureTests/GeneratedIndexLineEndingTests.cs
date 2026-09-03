using System.Diagnostics;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Every generator-written index file is pinned to LF in <c>.gitattributes</c>.
///
/// <para>Two generators in this repository write a JSON index and both emit LF
/// unconditionally: the knowledge index behind <c>build/Update-KnowledgeIndex.ps1</c>
/// writes <c>**/_meta/*.json</c>, and <c>writeIndex()</c> in
/// <c>tools/diagrams/archify-artifacts.mjs</c> writes <c>**/_archify/index.json</c>
/// with a trailing newline and nothing else. With <c>core.autocrlf=true</c> — the
/// Windows default, and what this repository is developed on — git hands the
/// working copy back as CRLF, the generator's next run rewrites it as LF, and
/// <c>git status</c> reports a file whose content did not change. The
/// <c>_meta</c> half of the rule was written for exactly that; the
/// <c>_archify</c> half was missing, so fourteen index files showed as modified
/// after every re-render while <c>git diff</c> showed nothing.</para>
///
/// <para>Asserted through <c>git check-attr</c> rather than by re-implementing
/// git's pattern matching here. The claim is about what git resolves for a path,
/// so a home-grown glob matcher would only prove that this file agrees with
/// itself — and it is git's own answer that decides whether the phantom diff
/// comes back.</para>
/// </summary>
public class GeneratedIndexLineEndingTests
{
    /// <summary>What a pinned path has to resolve to.</summary>
    private const string Set = "set";

    private const string Lf = "lf";

    /// <summary>Unmatched by any rule — git falls back to <c>core.autocrlf</c>.</summary>
    private const string Unspecified = "unspecified";

    /// <summary>The tracked paths the two generators own, as git pathspecs. Resolved
    /// through <c>git ls-files</c> so build output and untracked scratch copies
    /// cannot join the set.</summary>
    private static readonly string[] GeneratedIndexes =
        [":(glob)**/_meta/*.json", ":(glob)**/_archify/index.json"];

    /// <summary>The hand-written Archify specifications, which sit in the same
    /// folder as the index the generator writes.</summary>
    private static readonly string[] AuthoredSpecifications =
        [":(glob)**/_archify/*.json", ":(exclude,glob)**/_archify/index.json"];

    [Fact]
    public void Every_generated_index_is_pinned_to_lf()
    {
        var indexes = TrackedFiles(GeneratedIndexes);

        Assert.True(
            indexes.Count > 0,
            "This rule found no generated index files at all, so it is passing on nothing. Either the "
            + "generators stopped writing _meta/*.json and _archify/index.json, or the pathspecs here "
            + "are stale.");

        foreach (var (path, attributes) in Attributes(indexes))
        {
            Assert.True(
                attributes["text"] == Set && attributes["eol"] == Lf,
                $"{path} resolves to text: {attributes["text"]}, eol: {attributes["eol"]} rather than "
                + $"text: {Set}, eol: {Lf}. The generator that writes it always emits LF, so with "
                + "core.autocrlf=true git checks it out as CRLF and the next run reports it as modified "
                + "with an empty diff. Add the pattern that covers it to .gitattributes.");
        }
    }

    /// <summary>
    /// The rules stay scoped to what a generator writes.
    ///
    /// <para><c>**/_archify/*.json</c> would have been the shorter pattern and it
    /// is the wrong one: the Archify specifications beside the index are written
    /// by hand — <c>.arc42/08-crosscutting-concepts.md</c> says so — and pinning
    /// an authored file's line endings changes what a Windows editor writes
    /// rather than fixing a generator's phantom diff. This rule is what stops the
    /// pattern being widened by accident on the way past.</para>
    /// </summary>
    [Fact]
    public void Hand_written_archify_specifications_are_left_to_git()
    {
        var specifications = TrackedFiles(AuthoredSpecifications);

        Assert.True(
            specifications.Count > 0,
            "This rule found no authored Archify specifications at all, so it is passing on nothing. "
            + "Either the specifications moved out of _archify/, or the pathspecs here are stale.");

        foreach (var (path, attributes) in Attributes(specifications))
        {
            Assert.True(
                attributes["text"] == Unspecified && attributes["eol"] == Unspecified,
                $"{path} resolves to text: {attributes["text"]}, eol: {attributes["eol"]} rather than "
                + "being left unspecified. This file is authored by hand, not written by a generator, "
                + "so it is outside what the line-ending rules are for — a pattern was widened past the "
                + "index it was meant to cover.");
        }
    }

    /// <summary>
    /// The comment above the rules names both generators.
    ///
    /// <para>It named only <c>.github/workflows/knowledge-meta.yml</c>, which is a
    /// consumer of one of them, so the next person to add a generator had nothing
    /// telling them the rule was theirs to extend — which is how the Archify index
    /// came to be missed in the first place.</para>
    /// </summary>
    [Fact]
    public void The_line_ending_rules_name_both_generators()
    {
        var attributes = File.ReadAllText(RepositoryRoot.File(".gitattributes"));

        foreach (var generator in Generators)
        {
            Assert.True(
                attributes.Contains(generator, StringComparison.Ordinal),
                $".gitattributes does not mention {generator}. Both generators that write a pinned "
                + "index have to be named there, so the rules read as a list of generator output rather "
                + "than as one workflow's private arrangement.");
        }
    }

    /// <summary>The tracked paths matching <paramref name="pathspecs"/>, as git
    /// reports them: forward slashes, repository-relative.</summary>
    private static List<string> TrackedFiles(string[] pathspecs) =>
        [.. Git(null, ["ls-files", "-z", "--", .. pathspecs])
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>What git resolves <c>text</c> and <c>eol</c> to for each path,
    /// asked in one call because <c>check-attr</c> re-reads every
    /// <c>.gitattributes</c> in the tree on each invocation.</summary>
    private static IEnumerable<(string Path, Dictionary<string, string> Attributes)> Attributes(List<string> paths)
    {
        var resolved = paths.ToDictionary(
            path => path,
            _ => new Dictionary<string, string>(StringComparer.Ordinal));

        var output = Git(
            string.Join('\n', paths) + '\n',
            ["check-attr", "--stdin", "-z", "text", "eol"]);

        // -z emits a flat NUL-separated stream of path, attribute, value triples.
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 2 < fields.Length; index += 3)
        {
            if (resolved.TryGetValue(fields[index], out var attributes))
            {
                attributes[fields[index + 1]] = fields[index + 2];
            }
        }

        return paths.Select(path => (path, resolved[path]));
    }

    private static string Git(string? standardInput, string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot.Root.FullName,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;

        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {error}{output}");

        return output;
    }
}
