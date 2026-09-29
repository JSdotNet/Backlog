using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The Dashboard's provider adapters live beside the provider they wrap, not in the
/// pane that shows what they read.
/// <para>
/// They used to sit in <c>Backlog.Modules.Dashboard.UI/Adapters</c>, which made the
/// pane unhostable without all three provider clients and put HTTP-backed code in a
/// screen project. Each one now lives in the infrastructure project it maps from and
/// answers its port in <c>Backlog.Modules.Dashboard.Abstractions</c>, the direction
/// <see cref="ModuleBoundaryTests"/> already allows. What this stops is the drift
/// back: an adapter added to the UI project because the folder is still there.
/// </para>
/// </summary>
public sealed class DashboardAdapterPlacementTests
{
    private const string DashboardUi = "Backlog.Modules.Dashboard.UI";
    private const string DashboardAbstractions = "Backlog.Modules.Dashboard.Abstractions";

    public static TheoryData<string, string, string> ProviderAdapters => new()
    {
        { "GitHubActivitySource", "IActivitySource", "Backlog.Infrastructure.GitHub" },
        { "GitHubActivityBaselineSource", "IActivityBaselineSource", "Backlog.Infrastructure.GitHub" },
        { "CopilotSpendSource", "ICopilotSpendSource", "Backlog.Infrastructure.GitHub" },
        { "SettingsRepositoryDirectory", "IRepositoryDirectory", "Backlog.Infrastructure.GitHub" },
        { "ClaudeSpendSource", "IClaudeSpendSource", "Backlog.Infrastructure.Claude" },
        { "AzureFoundrySpendSource", "IAzureFoundrySpendSource", "Backlog.Infrastructure.AzureFoundry" },
    };

    [Theory]
    [MemberData(nameof(ProviderAdapters))]
    public void A_provider_adapter_lives_in_the_infrastructure_it_wraps(string adapter, string port, string infrastructure)
    {
        var declaration = Declaration(adapter, port);

        var inUi = SourceFiles(ProjectFolder("src", "Modules", "Dashboard", DashboardUi))
            .Where(file => declaration.IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => file.FullName)
            .ToList();

        Assert.True(inUi.Count == 0, $"{adapter} is still declared in {DashboardUi}: " + string.Join(", ", inUi));

        var inInfrastructure = SourceFiles(ProjectFolder("src", "Infrastructure", infrastructure))
            .Where(file => declaration.IsMatch(File.ReadAllText(file.FullName)))
            .ToList();

        Assert.True(
            inInfrastructure.Count == 1,
            $"Expected {adapter} answering {port} declared once in {infrastructure}, found {inInfrastructure.Count}.");
    }

    [Theory]
    [InlineData("Backlog.Infrastructure.GitHub")]
    [InlineData("Backlog.Infrastructure.Claude")]
    [InlineData("Backlog.Infrastructure.AzureFoundry")]
    public void An_adapter_project_sees_the_dashboards_published_surface_only(string infrastructure)
    {
        var references = Repository.ReferencedProjectNames(Project("src", "Infrastructure", infrastructure)).ToList();

        Assert.Contains(DashboardAbstractions, references, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Backlog.Modules.Dashboard", references, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(DashboardUi, references, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_dashboard_ui_references_no_provider_adapter()
    {
        var references = Repository.ReferencedProjectNames(Project("src", "Modules", "Dashboard", DashboardUi)).ToList();

        Assert.DoesNotContain("Backlog.Infrastructure.GitHub", references, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Backlog.Infrastructure.Claude", references, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Backlog.Infrastructure.AzureFoundry", references, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nothing_calls_the_ui_projects_adapter_registration_any_more()
    {
        var registration = new FileInfo(Path.Combine(
            ProjectFolder("src", "Modules", "Dashboard", DashboardUi).FullName, "Extensions", "DashboardAdapterRegistration.cs"));

        Assert.False(registration.Exists, $"{registration.FullName} still exists.");

        var callers = SourceFiles(new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src")))
            .Where(file => File.ReadAllText(file.FullName).Contains("AddDashboardAdapters(", StringComparison.Ordinal))
            .Select(file => file.FullName)
            .ToList();

        Assert.True(callers.Count == 0, "AddDashboardAdapters() is still called: " + string.Join(", ", callers));
    }

    private static Regex Declaration(string adapter, string port) =>
        new($@"\bclass\s+{adapter}\b[^{{]*:\s*[^{{]*\b{port}\b", RegexOptions.Singleline);

    private static FileInfo Project(params string[] segments)
    {
        var folder = ProjectFolder(segments);
        var project = new FileInfo(Path.Combine(folder.FullName, folder.Name + ".csproj"));

        Assert.True(project.Exists, $"{project.FullName} is not where this rule expects it.");
        return project;
    }

    private static DirectoryInfo ProjectFolder(params string[] segments) =>
        new(Path.Combine([Repository.Root.FullName, .. segments]));

    private static IEnumerable<FileInfo> SourceFiles(DirectoryInfo folder) =>
        folder.Exists
            ? folder.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            : [];
}
