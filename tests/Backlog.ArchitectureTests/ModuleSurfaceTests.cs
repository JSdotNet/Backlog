namespace Backlog.ArchitectureTests;

/// <summary>
/// Rules for what a module lets the rest of the solution see. A module exists to
/// own a decision; that only holds while callers have to ask it rather than
/// reach past it into the aggregate.
/// </summary>
public class ModuleSurfaceTests
{
    private const string Module = "Backlog.Modules.Backlog";
    private const string Abstractions = "Backlog.Modules.Backlog.Abstractions";

    /// <summary>
    /// A module library exists to be called, so it has to publish a contract. An
    /// <c>.Api</c> project is the opposite: a host that exposes the module over HTTP
    /// and that nothing in the solution references, so it has no surface to publish.
    /// </summary>
    [Fact]
    public void Every_module_publishes_an_abstractions_project()
    {
        var modules = Repository.ProjectsUnder("src", "Modules")
            .Select(project => Path.GetFileNameWithoutExtension(project.Name))
            .Where(name => !name.EndsWith(".Abstractions", StringComparison.Ordinal))
            .Where(name => !name.EndsWith(".Api", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(modules);

        foreach (var module in modules)
        {
            Assert.Contains(
                $"{module}.Abstractions",
                Repository.ProjectsUnder("src", "Modules").Select(p => Path.GetFileNameWithoutExtension(p.Name)));
        }
    }

    /// <summary>
    /// Abstractions is the module's contract, so it cannot depend on the thing it
    /// is a contract for — that would let an implementation type leak out through
    /// a DTO and make the split decorative.
    /// </summary>
    [Fact]
    public void Abstractions_never_reference_their_own_module()
    {
        foreach (var project in Repository.ProjectsUnder("src", "Modules")
                     .Where(p => p.Name.EndsWith(".Abstractions.csproj", StringComparison.Ordinal)))
        {
            var module = Path.GetFileNameWithoutExtension(project.Name)
                .Replace(".Abstractions", string.Empty, StringComparison.Ordinal);

            Assert.DoesNotContain(module, Repository.ReferencedProjectNames(project));
        }
    }

    /// <summary>
    /// The desktop is a host: it dispatches use cases and holds DTOs. Referencing
    /// the module implementation would put the aggregate and its repository back
    /// within reach, and the handlers would slowly stop being where the rules
    /// live.
    /// </summary>
    [Fact]
    public void The_desktop_ui_sees_only_the_published_surface()
    {
        var desktop = Repository.ProjectsUnder("src", "App")
            .Single(p => p.Name.Equals("Backlog.Desktop.UI.csproj", StringComparison.OrdinalIgnoreCase));

        var references = Repository.ReferencedProjectNames(desktop).ToList();

        Assert.Contains(Abstractions, references);
        Assert.DoesNotContain(Module, references);
    }

    /// <summary>
    /// Composition is a host's job — picking the storage adapter and calling
    /// AddBacklogModule() means seeing both sides, which only the executable
    /// heads do.
    /// </summary>
    [Theory]
    [InlineData("App", "Backlog.Desktop.csproj")]
    [InlineData("Harness", "Backlog.Desktop.WebHarness.csproj")]
    public void The_hosts_compose_the_module(string folder, string project)
    {
        var host = Repository.ProjectsUnder("src", folder)
            .Single(p => p.Name.Equals(project, StringComparison.OrdinalIgnoreCase));

        Assert.Contains(Module, Repository.ReferencedProjectNames(host));
    }
}
