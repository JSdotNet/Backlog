using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The two desktop heads — <c>src/App/Backlog.Desktop/MauiProgram.cs</c> and the
/// desktop web harness — compose the same modules and adapters, and they used to
/// do it by hand, line for line, with nothing but review to keep the copies level.
/// A registration missing from the MAUI head was found at runtime, because only the
/// harness copy is ever started by a test.
///
/// <para>
/// <c>AddDesktopComposition</c> in <c>Backlog.Desktop.Composition</c> is the one
/// copy now, and <c>DesktopCompositionOptions</c> carries what the two heads
/// genuinely do differently. These rules keep it that way: one definition, one call
/// per head, none of the shared calls back in either head, and nothing inside the
/// shared composition that asks which head it is running in.
/// </para>
/// </summary>
public sealed partial class DesktopCompositionTests
{
    private static readonly string[] Heads =
    [
        "src/App/Backlog.Desktop/MauiProgram.cs",
        "src/Harness/Backlog.Desktop.WebHarness/Program.cs"
    ];

    /// <summary>The module, adapter, AI-source, settings and sync calls the two
    /// heads used to make by hand, each of which now belongs to the shared
    /// composition alone.</summary>
    public static TheoryData<string> SharedCalls() =>
    [
        "AddTasksModule",
        "AddRoadmapModule",
        "AddInboxModule",
        "AddCaptureModule",
        "AddDashboardModule",
        "AddDevbookModule",
        "AddRoadmapCrossContextAdapters",
        "AddInboxCrossContextAdapters",
        "AddDashboardCrossContextAdapters",
        "AddCaptureAdapters",
        "AddTasksAdapters",
        "AddGitHubDashboardAdapters",
        "AddClaudeDashboardAdapters",
        "AddAzureFoundryDashboardAdapters",
        "AddDashboardUi",
        "AddAgentSessionSource",
        "AddAgentActivitySource",
        "AddRoadmapAiContentSource",
        "AddTasksAiContentSource",
        "AddInboxAiContentSource",
        "AddDevbookAiContentSource",
        "AddToolsAiContentSource",
        "AddSessionsAiContentSource",
        "AddInboxSettings",
        "AddSyncClient",
        "AddTaskSyncClient",
        "AddSessionSyncClient",
        "AddAnnotationSyncClient",
        "AddSessionSyncStores",
        "AddAnnotationSyncStore",
        "AddAzureFoundryChatClient",
        "AddSqlite",
        "AddGitHub",
        "AddClaude",
        "AddCopilot",
        "AddWorkspaceCaches"
    ];

    [Fact]
    public void The_shared_composition_and_its_options_are_defined_exactly_once()
    {
        var sources = SourceFiles("src").ToList();

        var compositions = sources.Where(file => CompositionDefinition().IsMatch(file.Text)).Select(file => file.RelativePath).ToList();
        var options = sources.Where(file => OptionsDefinition().IsMatch(file.Text)).Select(file => file.RelativePath).ToList();

        Assert.True(compositions.Count == 1, "AddDesktopComposition should be defined once under src/, found: " + string.Join(", ", compositions));
        Assert.True(options.Count == 1, "DesktopCompositionOptions should be defined once under src/, found: " + string.Join(", ", options));
        Assert.Equal(compositions[0].Split('/')[..3], options[0].Split('/')[..3]);
    }

    [Theory]
    [InlineData("src/App/Backlog.Desktop/MauiProgram.cs")]
    [InlineData("src/Harness/Backlog.Desktop.WebHarness/Program.cs")]
    public void Each_desktop_head_calls_the_shared_composition_exactly_once(string head)
    {
        var text = File.ReadAllText(Path.Combine([Repository.Root.FullName, .. head.Split('/')]));

        Assert.Single(Regex.Matches(text, @"\.AddDesktopComposition\("));
    }

    [Theory]
    [MemberData(nameof(SharedCalls))]
    public void No_desktop_head_makes_a_shared_call_of_its_own(string call)
    {
        var pattern = new Regex(@"\." + Regex.Escape(call) + @"\(");

        var offenders = Heads
            .Where(head => pattern.IsMatch(File.ReadAllText(Path.Combine([Repository.Root.FullName, .. head.Split('/')]))))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{call}() is part of AddDesktopComposition. A head that calls it as well registers it twice, and a "
            + "head that calls it instead is the drift the shared composition exists to end:\n" + string.Join('\n', offenders));

        Assert.Matches(pattern, CompositionSource());
    }

    /// <summary>
    /// What differs between the heads arrives through the options. A shared
    /// composition that looked at its surroundings instead — a preprocessor branch,
    /// the environment, the operating system — would be two compositions again, one
    /// of them never started by a test.
    /// </summary>
    [Fact]
    public void The_shared_composition_never_asks_which_head_it_is_in()
    {
        var text = CompositionSource();

        foreach (var probe in new[] { "#if", "Environment.", "OperatingSystem.", "IsDevelopment", "IHostEnvironment", "MauiApp", "WebApplication" })
        {
            Assert.DoesNotContain(probe, text, StringComparison.Ordinal);
        }
    }

    /// <summary>It references module implementations, so it may not be a
    /// presentation project — see
    /// <see cref="ModuleSurfaceTests.The_desktop_side_sees_only_the_published_surface"/>
    /// — and CI builds the filter, so the composition has to be in it.</summary>
    [Fact]
    public void The_shared_composition_is_a_non_ui_project_in_the_solution_and_its_filter()
    {
        var project = Repository.ProjectsUnder("src", "App", "Backlog.Desktop.Composition").SingleOrDefault();

        Assert.NotNull(project);
        Assert.False(Repository.IsUserInterface(project));

        var solution = File.ReadAllText(Path.Combine(Repository.Root.FullName, "Backlog.sln"));
        var filter = File.ReadAllText(Path.Combine(Repository.Root.FullName, "Backlog.WithoutAppHeads.slnf"));

        Assert.Contains(@"src\App\Backlog.Desktop.Composition\Backlog.Desktop.Composition.csproj", solution, StringComparison.Ordinal);
        Assert.Contains(@"src\\App\\Backlog.Desktop.Composition\\Backlog.Desktop.Composition.csproj", filter, StringComparison.Ordinal);

        foreach (var head in new[] { ("src", "App", "Backlog.Desktop"), ("src", "Harness", "Backlog.Desktop.WebHarness") })
        {
            var host = Repository.ProjectsUnder(head.Item1, head.Item2, head.Item3)
                .Single(candidate => Path.GetFileNameWithoutExtension(candidate.Name) == head.Item3);

            Assert.Contains("Backlog.Desktop.Composition", Repository.ReferencedProjectNames(host), StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string CompositionSource() =>
        Repository.DesktopCompositionSource() ?? throw new Xunit.Sdk.XunitException("Backlog.Desktop.Composition has no source.");

    private static IEnumerable<(string RelativePath, string Text)> SourceFiles(string root)
    {
        var folder = new DirectoryInfo(Path.Combine(Repository.Root.FullName, root));

        foreach (var file in folder.EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            yield return (
                Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/'),
                File.ReadAllText(file.FullName));
        }
    }

    [GeneratedRegex(@"static\s+IServiceCollection\s+AddDesktopComposition\s*\(\s*this\s+IServiceCollection")]
    private static partial Regex CompositionDefinition();

    [GeneratedRegex(@"\b(class|record)\s+DesktopCompositionOptions\b")]
    private static partial Regex OptionsDefinition();
}
