using System.Diagnostics;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Every generator-written index file that git tracks is pinned to LF in
/// <c>.gitattributes</c>.
///
/// <para><c>writeIndex()</c> in <c>tools/diagrams/archify-artifacts.mjs</c> writes
/// <c>**/_archify/index.json</c> with a trailing newline and nothing else. With
/// <c>core.autocrlf=true</c> — the Windows default, and what this repository is
/// developed on — git hands the working copy back as CRLF, the generator's next run
/// rewrites it as LF, and <c>git status</c> reports a file whose content did not
/// change. The rule was written for the devbook database and the Archify half was
/// missing, so fourteen index files showed as modified after every re-render while
/// <c>git diff</c> showed nothing.</para>
///
/// <para>The devbook database is no longer one of these. Local ADR 0004 made the
/// derived knowledge layer a generated devbook database that
/// <c>.gitignore</c> covers, so <c>**/_meta/*.json</c> matches nothing git tracks
/// and there is nothing left for a phantom diff to be a diff against. That is why
/// the pathspecs below are one generator's rather than two, and why the guard on
/// each rule finding files at all matters: it is what would catch this list going
/// stale in the other direction.</para>
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
        [":(glob)**/_archify/index.json"];

    /// <summary>The hand-written Archify specifications, which sit in the same
    /// folder as the index the generator writes.</summary>
    private static readonly string[] AuthoredSpecifications =
        [":(glob)**/_archify/*.json", ":(exclude,glob)**/_archify/index.json"];

    /// <summary>The generators that write a pinned index git tracks, by the path
    /// each is invoked at. <c>.gitattributes</c> has to name every one: the rules
    /// there are not one workflow's private arrangement, and the next generator to
    /// be added needs the comment to read as a list it belongs on.</summary>
    private static readonly string[] Generators =
        ["tools/diagrams/archify-artifacts.mjs"];

    /// <summary>
    /// The comment above the rules names every generator whose output they pin.
    ///
    /// <para>It once named only a workflow that consumed an index rather than a
    /// generator that writes one, so the next person to add one had nothing
    /// telling them the rule was theirs to extend — which is how the Archify index
    /// came to be missed in the first place.</para>
    /// </summary>
    [Fact]
    public void The_line_ending_rules_name_every_generator()
    {
        var attributes = File.ReadAllText(RepositoryRoot.File(".gitattributes"));

        foreach (var generator in Generators)
        {
            Assert.True(
                attributes.Contains(generator, StringComparison.Ordinal),
                $".gitattributes does not mention {generator}. Every generator that writes a pinned "
                + "index has to be named there, so the rules read as a list of generator output rather "
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

        // -z separates the paths going in as well as the fields coming back. Joined
        // with newlines instead, git reads the whole blob as one path with a linefeed
        // in its name, matches no pattern, and answers "unspecified" for every
        // attribute — which reads exactly like a missing .gitattributes rule.
        var output = Git(
            string.Join('\0', paths) + '\0',
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
