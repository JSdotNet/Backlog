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
    /// A <c>.UI</c> project is the same shape for a different reason: it renders one
    /// context's screens and is consumed by the shell that composes it, not called
    /// across a boundary, so there is no contract for it to publish either.
    ///
    /// <para>Read what is left carefully: this asks that a module <em>that exists</em>
    /// publishes a contract, not that every context folder holds a module. Inbox,
    /// Second Brain, Roadmap and Dev PC Management currently have a
    /// <c>.UI</c> project and no domain module at all, and that is a statement about
    /// how far each context has been built rather than a boundary violation. This
    /// rule stays silent about it on purpose; demanding an abstractions project for
    /// a module nobody has written yet would only produce empty ones.</para>
    /// </summary>
    [Fact]
    public void Every_module_publishes_an_abstractions_project()
    {
        var modules = Repository.ProjectsUnder("src", "Modules")
            .Where(project => !Repository.IsUserInterface(project))
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
    /// The desktop side is a host: it dispatches use cases and holds DTOs.
    /// Referencing the module implementation would put the aggregate and its
    /// repository back within reach, and the handlers would slowly stop being
    /// where the rules live.
    ///
    /// <para>The same intent, restated over more than one project. This used to
    /// read <c>Backlog.Desktop.UI</c> alone, because that project was the whole
    /// desktop side; it is now the shell and one UI project per context, and the
    /// reference that used to sit on the shell moved to the projects that
    /// actually use it. Asserting on the shell alone would now be asserting that
    /// the shell does not consume a contract it never needed — true, and about
    /// nothing. So the rule names every presentation project: none of them may
    /// see the implementation, and the published surface has to be referenced by
    /// whichever of them genuinely dispatches a use case.</para>
    /// </summary>
    [Fact]
    public void The_desktop_side_sees_only_the_published_surface()
    {
        var presentation = PresentationProjects().ToList();

        Assert.NotEmpty(presentation);

        var offenders = presentation
            .Where(project => Repository.ReferencedProjectNames(project)
                .Contains(Module, StringComparer.OrdinalIgnoreCase))
            .Select(project => project.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"The presentation side asks {Module} through {Abstractions}. Referencing the implementation "
            + "puts the aggregate, its repository and the handlers back within reach of a component: "
            + string.Join(", ", offenders));

        var consumers = presentation
            .Where(project => Repository.ReferencedProjectNames(project)
                .Contains(Abstractions, StringComparer.OrdinalIgnoreCase))
            .Select(project => project.Name)
            .ToList();

        Assert.True(
            consumers.Count > 0,
            $"Nothing on the presentation side references {Abstractions}. Either the desktop stopped "
            + "dispatching backlog use cases, or it found another way to reach them — and the second "
            + "is what this rule exists to notice.");
    }

    /// <summary>
    /// Everything that renders a screen: every <c>.UI</c> project. The mobile UI
    /// is in here too and always was in spirit — it is presentation, and the
    /// aggregate is no more within its reach than the desktop's.
    /// <para>This used to add <c>Backlog.Desktop.Workspace</c>, which was neither a
    /// UI project nor a module and sat under the contexts. That project is gone;
    /// what it held is now module ports with adapters in <c>src/Infrastructure</c>
    /// behind them, and an adapter referencing the module implementation is the
    /// normal direction rather than the thing this rule watches for.</para>
    /// </summary>
    private static IEnumerable<FileInfo> PresentationProjects() =>
        Repository.UserInterfaceProjects();

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
