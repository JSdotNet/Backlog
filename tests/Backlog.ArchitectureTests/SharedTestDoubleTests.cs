using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The hand-rolled fakes the suite shares are declared once, under
/// <c>tests/Shared/TestDoubles</c>, and linked into the projects that use them.
///
/// <para>They used to be private nested classes copied into every file that
/// needed one — the GitHub client stub fourteen times, the connection probe
/// fifteen — and the copies drifted: one probe answered with different text,
/// one chat client recorded its requests and five did not. A fix to one copy
/// reached one file. These tests are what stops a sixteenth copy from being
/// the easiest way to write the next test.</para>
/// </summary>
public partial class SharedTestDoubleTests
{
    public static TheoryData<string> SharedDoubles =>
    [
        "StubGitHubClient",
        "StubProbe",
        "StubAzureFoundryChatClient",
        "InMemoryTaskRepository",
        "RecordingHandler",
        "RecordingTelemetry",
        "InMemorySessionRequestStore",
        "RecordingNotifications",
        "InMemoryPullRequests",
        "StubSessionSource",
        "StubFolderSource",
        "RecordingTaskItems",
    ];

    /// <summary>
    /// A copy under another name is still a copy. The suite once carried the
    /// throw-only GitHub client as <c>NoGitHub</c>, <c>SilentGitHubClient</c>,
    /// <c>UnusedGitHubClient</c> and <c>UnreachableGitHub</c>, and the probe as
    /// <c>NoProbe</c>, <c>SilentProbe</c> and <c>UnusedProbe</c>, none of which
    /// a check on the shared class's own name could see. So every test class
    /// that implements an interface a shared double implements is either that
    /// double or says, on the line above it, how it differs:
    /// <c>// Differs from StubProbe: answers connected.</c>
    /// </summary>
    [Fact]
    public void A_class_implementing_a_shared_doubles_interface_is_the_shared_double_or_says_how_it_differs()
    {
        var covered = SharedDoubleInterfaces();

        Assert.NotEmpty(covered);

        var implementers = TestSources()
            .Where(file => !Relative(file).StartsWith("tests/Shared/TestDoubles/", StringComparison.Ordinal))
            .SelectMany(file => Implementers(file, covered))
            .ToList();

        var undeclared = implementers
            .Where(implementer => !implementer.SaysHowItDiffers)
            .Select(implementer => $"{implementer.File}: {implementer.Name} implements {implementer.Interface}; use {implementer.Shared}, or say above it '// Differs from {implementer.Shared}: <how>'.")
            .ToList();

        Assert.True(undeclared.Count == 0, string.Join(Environment.NewLine, undeclared));

        // The doubles kept for a real difference are found too, so a scan that
        // matched nothing cannot pass for one that found nothing wrong.
        Assert.Contains(implementers, implementer => implementer.SaysHowItDiffers);
    }

    /// <summary>Each interface a double under <c>tests/Shared/TestDoubles</c>
    /// implements, mapped to the double that implements it.</summary>
    private static Dictionary<string, string> SharedDoubleInterfaces()
    {
        var covered = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in new DirectoryInfo(RepositoryRoot.Directory("tests", "Shared", "TestDoubles")).EnumerateFiles("*.cs").OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            foreach (Match declaration in TypeDeclaration().Matches(File.ReadAllText(file.FullName)))
            {
                foreach (var implemented in Interfaces(declaration.Groups["bases"].Value))
                {
                    covered.TryAdd(implemented, declaration.Groups["name"].Value);
                }
            }
        }

        return covered;
    }

    private static IEnumerable<Implementer> Implementers(FileInfo file, Dictionary<string, string> covered)
    {
        var text = File.ReadAllText(file.FullName);

        foreach (Match declaration in TypeDeclaration().Matches(text))
        {
            foreach (var implemented in Interfaces(declaration.Groups["bases"].Value))
            {
                if (covered.TryGetValue(implemented, out var shared))
                {
                    yield return new Implementer(
                        Relative(file),
                        declaration.Groups["name"].Value,
                        implemented,
                        shared,
                        SaysHowItDiffers(text, declaration.Index, shared));
                }
            }
        }
    }

    /// <summary>A test class implementing an interface a shared double covers.</summary>
    private sealed record Implementer(string File, string Name, string Interface, string Shared, bool SaysHowItDiffers);

    /// <summary>The interfaces a base list names: each entry's simple name with
    /// its type arguments dropped, kept when it reads as an interface.</summary>
    private static IEnumerable<string> Interfaces(string bases) =>
        TypeArguments().Replace(bases, "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('.')[^1])
            .Where(entry => InterfaceName().IsMatch(entry));

    /// <summary>Whether the line above the declaration, past its attributes and
    /// modifiers, is a <c>// Differs from {shared}:</c> comment that says how.</summary>
    private static bool SaysHowItDiffers(string text, int declarationIndex, string shared)
    {
        var lines = text[..declarationIndex].Split('\n');

        // The last line holds the declaration's own modifiers.
        for (var i = lines.Length - 2; i >= 0; i--)
        {
            var line = lines[i].Trim();

            if (line.StartsWith('['))
            {
                continue;
            }

            var prefix = $"// Differs from {shared}:";

            return line.StartsWith(prefix, StringComparison.Ordinal) && line.Length > prefix.Length + 1;
        }

        return false;
    }

    [Theory]
    [MemberData(nameof(SharedDoubles))]
    public void Each_shared_double_is_declared_once_under_the_shared_folder(string name)
    {
        var declaration = new Regex($@"\b(class|record)\s+{name}\b");

        var declared = TestSources()
            .Where(file => declaration.IsMatch(File.ReadAllText(file.FullName)))
            .Select(Relative)
            .ToList();

        Assert.Equal([$"tests/Shared/TestDoubles/{name}.cs"], declared);
    }

    /// <summary>
    /// A shared double implements a product interface, so it can only compile
    /// in a project that references that interface. It is linked into each
    /// project that uses it rather than into every test project from
    /// <c>tests/Directory.Build.props</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(SharedDoubles))]
    public void Each_shared_double_is_linked_only_into_the_projects_that_use_it(string name)
    {
        var linking = TestProjects()
            .Where(project => Links(project, $"TestDoubles\\{name}.cs"))
            .Select(project => project.Directory!.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        var using_ = TestProjects()
            .Where(project => project.Directory!
                .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !Generated(file))
                .Any(file => Regex.IsMatch(File.ReadAllText(file.FullName), $@"\bnew\s+{name}\b|\b{name}\s*[\(\{{<\[.,;)]")))
            .Select(project => project.Directory!.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(linking);
        Assert.Equal(using_, linking);
        Assert.DoesNotContain(
            $"TestDoubles\\{name}.cs",
            File.ReadAllText(RepositoryRoot.File("tests", "Directory.Build.props")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Two clocks coexisted: a hand-rolled <c>FixedClock</c> in a dozen files and
    /// <c>FakeTimeProvider</c> in the rest. The suite uses the one it does not
    /// have to maintain.
    /// </summary>
    [Fact]
    public void No_test_rolls_its_own_fixed_clock()
    {
        var clocks = TestSources()
            .Where(file => Regex.IsMatch(File.ReadAllText(file.FullName), @"\bclass\s+FixedClock\b"))
            .Select(Relative)
            .ToList();

        Assert.Empty(clocks);
    }

    /// <summary>
    /// A poll written as a private helper around <c>Task.Delay(50)</c> and a
    /// wall-clock deadline never passes the running test's token, and the
    /// analyzer cannot see it because the call sits outside a test method.
    /// The shared <c>WaitUntilAsync</c> is the one that does.
    /// </summary>
    [Fact]
    public void Polls_go_through_the_shared_wait()
    {
        var polls = TestSources()
            .Where(file => !file.Name.Equals($"{nameof(SharedTestDoubleTests)}.cs", StringComparison.Ordinal))
            .Where(file => HandRolledPoll().IsMatch(File.ReadAllText(file.FullName)))
            .Select(Relative)
            .ToList();

        Assert.Empty(polls);

        var shared = File.ReadAllText(RepositoryRoot.File("tests", "Shared", "Polling.cs"));
        Assert.Contains("WaitUntilAsync(Func<Task<bool>>", shared, StringComparison.Ordinal);
        Assert.Contains("TestContext.Current.CancellationToken", shared, StringComparison.Ordinal);
        Assert.Contains(
            "Shared\\Polling.cs",
            File.ReadAllText(RepositoryRoot.File("tests", "Directory.Build.props")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Two test classes with one simple name make a TRX report and a
    /// <c>-class</c> filter ambiguous: the filter runs both, the report cannot
    /// say which failed.
    /// </summary>
    [Fact]
    public void No_two_test_classes_share_a_name()
    {
        var duplicates = TestSources()
            .SelectMany(file => TestClassName().Matches(File.ReadAllText(file.FullName))
                .Select(match => (Name: match.Groups[1].Value, File: Relative(file))))
            .GroupBy(declaration => declaration.Name, StringComparer.Ordinal)
            .Where(group => group.Select(d => d.File).Distinct().Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(d => d.File))}")
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// A test with no assertion reads as unfinished. The ones whose point is
    /// that nothing throws say so.
    /// </summary>
    [Theory]
    [InlineData("tests/Backlog.Infrastructure.FileSystem.UnitTests/ActivityListingCacheTests.cs")]
    [InlineData("tests/Backlog.Infrastructure.FileSystem.UnitTests/AgentActivityCacheTests.cs")]
    [InlineData("tests/Backlog.Infrastructure.FileSystem.UnitTests/AiCreditUsageCacheTests.cs")]
    [InlineData("tests/Backlog.Infrastructure.FileSystem.UnitTests/PullRequestDetailCacheTests.cs")]
    [InlineData("tests/Backlog.Infrastructure.FileSystem.UnitTests/TranscriptFactsCacheTests.cs")]
    [InlineData("tests/Backlog.Infrastructure.Claude.UnitTests/ClaudeCodeUsageCacheTests.cs")]
    public void Forgetting_nothing_states_that_it_does_not_throw(string path)
    {
        var text = File.ReadAllText(RepositoryRoot.File(path.Split('/')));

        var body = ForgetTestBody().Match(text);

        Assert.True(body.Success, $"No forget-when-nothing-was-stored test found in {path}.");
        Assert.Contains("Assert.Null(", body.Value, StringComparison.Ordinal);
        Assert.Contains("Record.Exception", body.Value, StringComparison.Ordinal);
    }

    /// <summary>A fixed fifty-millisecond sleep after a read of the wall clock
    /// for its deadline.</summary>
    [GeneratedRegex(@"DateTime\.UtcNow[\s\S]{0,600}?Task\.Delay\(50\)")]
    private static partial Regex HandRolledPoll();

    /// <summary>A class or record declaration up to its body: its name, an
    /// optional type parameter list and primary constructor, and its base list.</summary>
    [GeneratedRegex(@"\b(?:class|record)\s+(?<name>\w+)\s*(?:<[^>]*>)?\s*(?:\((?:[^()]|\([^()]*\))*\))?\s*:(?<bases>[^{;]*?)(?:\bwhere\b[^{;]*)?[{;]")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"<[^<>]*(?:<[^<>]*>[^<>]*)*>")]
    private static partial Regex TypeArguments();

    [GeneratedRegex(@"^I[A-Z]\w*$")]
    private static partial Regex InterfaceName();

    [GeneratedRegex(@"\bclass\s+(\w+Tests)\b")]
    private static partial Regex TestClassName();

    [GeneratedRegex(@"public\s+(?:async\s+Task|void)\s+\w*[Ff]orget\w*[Nn]othing\w*\([^)]*\)\s*\{(?:[^{}]|\{[^{}]*\})*\}")]
    private static partial Regex ForgetTestBody();

    private static IEnumerable<FileInfo> TestProjects() =>
        Repository.ProjectsUnder("tests");

    private static bool Links(FileInfo project, string sharedPath) =>
        XDocument.Load(project.FullName)
            .Descendants("Compile")
            .Any(item => (item.Attribute("Include")?.Value ?? "").EndsWith(sharedPath, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<FileInfo> TestSources() =>
        new DirectoryInfo(RepositoryRoot.Directory("tests"))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !Generated(file));

    private static bool Generated(FileInfo file) =>
        file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
