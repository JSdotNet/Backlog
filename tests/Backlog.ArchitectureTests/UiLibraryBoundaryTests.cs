namespace Backlog.ArchitectureTests;

/// <summary>
/// Rules for <c>src/UI</c>. The shared component library exists so the UI can be
/// reviewed and driven on its own, in the storybook harness, without the
/// application behind it. That only stays true while the library knows nothing
/// about the domain — the moment it can see a module or an adapter, a component
/// starts reaching for state instead of taking a parameter, and the storybook
/// stops being able to render it.
/// </summary>
public class UiLibraryBoundaryTests
{
    [Fact]
    public void The_shared_ui_library_never_references_a_module_or_infrastructure()
    {
        var forbidden = Repository.ProjectsUnder("src", "Modules")
            .Concat(Repository.ProjectsUnder("src", "Infrastructure"))
            .Select(p => Path.GetFileNameWithoutExtension(p.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(forbidden);

        foreach (var project in Repository.ProjectsUnder("src", "UI"))
        {
            var offenders = Repository.ReferencedProjectNames(project)
                .Where(forbidden.Contains)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"{project.Name} must stay domain-free so the storybook can render it "
                + $"without the application: {string.Join(", ", offenders)}");
        }
    }

    [Fact]
    public void The_shared_ui_library_never_references_an_application_project()
    {
        var apps = Repository.ProjectsUnder("src", "App")
            .Select(p => Path.GetFileNameWithoutExtension(p.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(apps);

        foreach (var project in Repository.ProjectsUnder("src", "UI"))
        {
            var offenders = Repository.ReferencedProjectNames(project)
                .Where(apps.Contains)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"{project.Name} is referenced BY the applications, never the other way round: "
                + string.Join(", ", offenders));
        }
    }

    [Fact]
    public void The_storybook_harness_hosts_only_the_shared_library()
    {
        var storybook = Repository.ProjectsUnder("src", "Harness")
            .SingleOrDefault(p => p.Name.Equals("Backlog.UI.Storybook.csproj", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(storybook);

        // The point of the storybook is that it proves the components run with
        // nothing but the library behind them. A reference to an app, a module
        // or an adapter would quietly re-introduce the dependency the library
        // was carved out to remove.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Backlog.UI.Components",
            "Backlog.Aspire.ServiceDefaults",
        };

        var offenders = Repository.ReferencedProjectNames(storybook)
            .Where(reference => !allowed.Contains(reference))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The storybook must run on the component library alone: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_application_ui_project_uses_the_shared_library()
    {
        var uiProjects = Repository.ProjectsUnder("src", "App")
            .Where(p => p.Name.EndsWith(".UI.csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(uiProjects);

        foreach (var project in uiProjects)
        {
            Assert.True(
                Repository.ReferencedProjectNames(project)
                    .Contains("Backlog.UI.Components", StringComparer.OrdinalIgnoreCase),
                $"{project.Name} should render the shared components rather than growing its own copies.");
        }
    }
}
