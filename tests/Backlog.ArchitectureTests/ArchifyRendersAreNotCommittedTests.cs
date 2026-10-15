using System.Diagnostics;
using System.Text.Json;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Generated Archify renders stay out of version control (local ADR 0033).
///
/// <para>Each render is a self-contained HTML document of roughly 700 KB with two
/// font blobs inside it, derived by a script from the specification beside it.
/// Committed, the forty-two of them were more than half the tracked tree, and every
/// regeneration rewrote megabytes of history. The specifications and the
/// <c>_archify/index.json</c> files are what is authored and what ties a picture
/// to its fence; the app draws the picture itself, on demand, into its own
/// storage.</para>
///
/// <para>Both halves are checked: nothing is tracked, and <c>.gitignore</c> keeps
/// it that way, because the generator script still leaves a local preview beside
/// the specification and <c>git add -A</c> would otherwise sweep it back in.</para>
/// </summary>
public class ArchifyRendersAreNotCommittedTests
{
    /// <summary>The renders the generator writes beside a chapter's
    /// specifications, and the sample renders upstream ships with its
    /// examples.</summary>
    private static readonly string[] Renders =
        [":(glob)**/_archify/*.html", ":(glob)tools/archify/examples/*.html"];

    /// <summary>What stays committed: the authored specifications and the index
    /// beside them.</summary>
    private static readonly string[] Specifications = [":(glob)**/_archify/*.json"];

    /// <summary>Paths a fresh render would be written to, one per shape: a
    /// showcase render, a standard-quality render in a nested context folder, and
    /// an example render from <c>tools/archify/render-examples.mjs</c>.</summary>
    private static readonly string[] FreshRenders =
    [
        ".devbook/arc42/_archify/05-building-block-view.1.architecture.html",
        ".devbook/domain/orders/_archify/context-map.1.architecture.standard.html",
        "tools/archify/examples/web-app-rendered.html"
    ];

    /// <summary>The storybook's fixture artifact, which is a build-time input the
    /// Diagrams page embeds rather than a devbook render, and so stays
    /// committed.</summary>
    private const string StorybookFixture =
        "src/Harness/Backlog.UI.Storybook/Components/Shared/Archify/slice-flow.architecture.html";

    [Fact]
    public void No_generated_archify_render_is_tracked()
    {
        var tracked = TrackedFiles(Renders);

        Assert.True(
            tracked.Count == 0,
            "These generated Archify renders are tracked. The app draws each one on demand from its "
            + "specification into its own storage (local ADR 0033), so committing them only rewrites "
            + "megabytes of history on every regeneration. git rm --cached them:\n"
            + string.Join('\n', tracked));
    }

    [Fact]
    public void The_specifications_and_their_index_are_still_tracked()
    {
        var tracked = TrackedFiles(Specifications);

        Assert.Contains(tracked, path => path.EndsWith("/_archify/index.json", StringComparison.Ordinal));
        Assert.Contains(tracked, path => !path.EndsWith("/index.json", StringComparison.Ordinal));
        Assert.NotEmpty(TrackedFiles([":(glob)tools/archify/examples/*.json"]));
    }

    [Fact]
    public void A_fresh_render_is_ignored_by_git()
    {
        var ignored = Git(string.Join('\0', FreshRenders) + '\0', ["check-ignore", "--no-index", "--stdin", "-z"], allowExitCodeOne: true)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);

        foreach (var render in FreshRenders)
        {
            Assert.True(
                ignored.Contains(render, StringComparer.Ordinal),
                $"{render} is not ignored. tools/diagrams/archify-artifacts.mjs render still leaves a local "
                + "preview beside the specification, so .gitignore has to carry **/_archify/*.html and "
                + "tools/archify/examples/*.html or the next git add -A commits it again.");
        }
    }

    [Fact]
    public void The_ignore_rules_leave_the_specifications_and_the_storybook_fixture_alone()
    {
        string[] kept =
        [
            ".devbook/arc42/_archify/index.json",
            ".devbook/arc42/_archify/05-building-block-view.1.architecture.json",
            "tools/archify/examples/web-app.architecture.json",
            StorybookFixture
        ];

        var ignored = Git(string.Join('\0', kept) + '\0', ["check-ignore", "--no-index", "--stdin", "-z"], allowExitCodeOne: true)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);

        Assert.Empty(ignored);
        Assert.Equal([StorybookFixture], TrackedFiles([StorybookFixture]));
    }

    /// <summary>
    /// The generator script's own check needs no render on disk.
    ///
    /// <para><c>scan</c> used to call a diagram <c>rendered</c> only when its HTML
    /// was beside the specification, so deleting the renders would have turned
    /// every diagram <c>unrendered</c> and <c>verify</c> red. What makes a diagram
    /// rendered now is an index entry filed under its fence's hash, with the
    /// specification it names still there.</para>
    ///
    /// <para>Asserted on what <c>verify</c> reports rather than on its exit code.
    /// A <c>stale</c> diagram is a fence edited after its specification was
    /// rendered — drift a person re-authors, which the presence or absence of a
    /// render has no bearing on — so it is left to <c>verify</c> itself. Only the
    /// two states a missing render could cause are ruled out here. Skipped where
    /// Node is absent.</para>
    /// </summary>
    [Fact]
    public void The_artifact_check_needs_no_committed_render()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        var (_, verifyOutput) = Node(["tools/diagrams/archify-artifacts.mjs", "verify", "--json"]);

        using (var verify = JsonDocument.Parse(verifyOutput))
        {
            var unrendered = verify.RootElement.GetProperty("broken").EnumerateArray()
                .Where(row => row.GetProperty("state").GetString() is "unrendered" or "error")
                .Select(row => $"{row.GetProperty("state").GetString()} {row.GetProperty("chapter").GetString()} #{row.GetProperty("ordinal").GetInt32()}")
                .ToList();

            Assert.True(
                unrendered.Count == 0,
                "archify-artifacts.mjs verify reports diagrams as unrendered or in error with no render on "
                + "disk. A diagram is rendered when its index entry matches the fence and its specification "
                + "exists; the HTML is the app's to draw:\n" + string.Join('\n', unrendered));
        }

        var (scanExit, scanOutput) = Node(["tools/diagrams/archify-artifacts.mjs", "scan", "--json"]);
        Assert.Equal(0, scanExit);

        using var rows = JsonDocument.Parse(scanOutput);
        var rendered = rows.RootElement.EnumerateArray()
            .Where(row => row.GetProperty("state").GetString() == "rendered")
            .ToList();

        Assert.NotEmpty(rendered);

        foreach (var row in rendered)
        {
            var spec = row.GetProperty("spec").GetString()!;
            Assert.True(
                File.Exists(RepositoryRoot.File(spec.Split('/'))),
                $"{row.GetProperty("chapter").GetString()} #{row.GetProperty("ordinal").GetInt32()} is reported "
                + $"rendered, but its specification {spec} is not there to draw it from.");
        }
    }

    private static List<string> TrackedFiles(string[] pathspecs) =>
        [.. Git(null, ["ls-files", "-z", "--", .. pathspecs])
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)];

    private static string Git(string? standardInput, string[] arguments, bool allowExitCodeOne = false)
    {
        var (exit, output, error) = Run("git", arguments, standardInput);

        // check-ignore exits 1 when nothing it was asked about is ignored, which
        // is an answer rather than a failure.
        Assert.True(
            exit == 0 || (allowExitCodeOne && exit == 1),
            $"git {string.Join(' ', arguments)} exited {exit}: {error}");

        return output;
    }

    private static (int Exit, string Output) Node(string[] arguments)
    {
        var (exit, output, error) = Run("node", arguments, null);
        return (exit, exit == 0 ? output : output + error);
    }

    private static (int Exit, string Output, string Error) Run(string executable, string[] arguments, string? standardInput)
    {
        var startInfo = new ProcessStartInfo(executable)
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

        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output, error.GetAwaiter().GetResult());
    }

    private static bool NodeInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });

            process?.WaitForExit(10_000);
            return process is { ExitCode: 0 };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
