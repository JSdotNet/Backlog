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
    ];

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
