namespace Backlog.ArchitectureTests;

/// <summary>
/// A cancelled test run has to stop, and it can only stop at a call that was
/// given the running test's cancellation token.
///
/// <para>The v3 analyzers raise one diagnostic per call that accepts a
/// <see cref="CancellationToken"/> and is not given one. The suite silenced it
/// wholesale during the v3 migration so the move stayed a migration; the
/// silence was recorded as its own piece of work rather than as a decision.
/// That work is done, so the silence is gone — and this is what keeps it gone,
/// because the cheapest way to answer the diagnostic is to turn it off again
/// somewhere less visible than the file it used to live in.</para>
///
/// <para>The token has to be the running test's own. A token that never
/// cancels compiles and answers the analyzer while buying none of the
/// responsiveness it exists for.</para>
/// </summary>
public class TestCancellationTests
{
    private const string Diagnostic = "xUnit1051";

    private const string RunningTestToken = "TestContext.Current.CancellationToken";

    /// <summary>The suppression that made the v3 move a migration.</summary>
    [Fact]
    public void The_test_subtree_no_longer_silences_the_diagnostic()
    {
        var props = File.ReadAllText(RepositoryRoot.File("tests", "Directory.Build.props"));

        Assert.DoesNotContain(Diagnostic, props, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same silence in another shape — a project's own exclusion list, an
    /// inline compiler directive, a severity entry in editor configuration, an
    /// assembly-level suppression attribute — is the same silence, and the list
    /// of shapes is open-ended.
    ///
    /// <para>So this does not enumerate them. Every one of them has to name the
    /// diagnostic in order to switch it off, and nothing else in the repository
    /// has any reason to name it now that the suppression is gone. Banning the
    /// name outside this file therefore catches the shapes nobody has thought of
    /// yet, which a predicate over known shapes cannot.</para>
    /// </summary>
    [Fact]
    public void Nothing_names_the_diagnostic_anywhere_else()
    {
        var mentions = Sources(".cs", ".csproj", ".props", ".targets", ".editorconfig", ".globalconfig")
            .Where(file => !DeclaredHere(file))
            .SelectMany(Lines)
            .Where(line => line.Contains(Diagnostic, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            mentions.Count == 0,
            $"The diagnostic is named outside this test at:{Environment.NewLine}{string.Join(Environment.NewLine, mentions)}"
            + $"{Environment.NewLine}Naming it is how it gets switched off. Pass {RunningTestToken} to the call instead.");
    }

    /// <summary>
    /// A token that never cancels answers the analyzer and cancels nothing, so
    /// the suite reaches for the running test's own or constructs a source it
    /// intends to cancel itself.
    /// </summary>
    [Fact]
    public void Tests_pass_the_running_test_s_own_token()
    {
        var placeholders = Sources(".cs")
            .Where(file => UnderTests(file))
            .SelectMany(Lines)
            .Where(line => line.Contains("CancellationToken" + ".None", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            placeholders.Count == 0,
            $"A token that never cancels is passed at:{Environment.NewLine}{string.Join(Environment.NewLine, placeholders)}"
            + $"{Environment.NewLine}Pass {RunningTestToken} so the call stops with the run.");
    }

    /// <summary>This file, the one place the diagnostic is named on purpose.</summary>
    private static bool DeclaredHere(FileInfo file) =>
        file.Name.Equals($"{nameof(TestCancellationTests)}.cs", StringComparison.OrdinalIgnoreCase);

    private static bool UnderTests(FileInfo file) =>
        file.FullName.Contains(
            $"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Everything under <c>src</c> and <c>tests</c>, plus the repository root's
    /// own build and editor configuration.
    ///
    /// <para>The root is not thoroughness for its own sake.
    /// <c>tests/Directory.Build.props</c> imports the root
    /// <c>Directory.Build.props</c> explicitly, so a single exclusion there
    /// reaches all twelve test projects without leaving a trace anywhere under
    /// <c>tests</c> — the one bypass that would silence the analyzer and leave
    /// every test in this file green.</para>
    /// </summary>
    private static IEnumerable<FileInfo> Sources(params string[] extensions) =>
        Repository.Root.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Concat(new[] { "src", "tests" }
                .Select(area => new DirectoryInfo(Path.Combine(Repository.Root.FullName, area)))
                .Where(area => area.Exists)
                .SelectMany(area => area.EnumerateFiles("*", SearchOption.AllDirectories)))
            .Where(file => extensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)
                           || extensions.Contains(file.Name, StringComparer.OrdinalIgnoreCase))
            .Where(file => !Generated(file));

    private static bool Generated(FileInfo file) =>
        Segment(file, "bin") || Segment(file, "obj") || Segment(file, "node_modules");

    private static bool Segment(FileInfo file, string folder) =>
        file.FullName.Contains(
            $"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Lines(FileInfo file) =>
        File.ReadAllLines(file.FullName)
            .Select((text, index) =>
                $"  {Path.GetRelativePath(Repository.Root.FullName, file.FullName)}({index + 1}): {text.Trim()}");
}
