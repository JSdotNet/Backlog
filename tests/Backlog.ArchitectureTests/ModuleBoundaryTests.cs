namespace Backlog.ArchitectureTests;

/// <summary>
/// Boundary rules for the modular monolith. Modules own domain behaviour; the
/// adapters they need are cross-cutting and live in <c>src/Infrastructure</c>,
/// so a module must never be able to see one.
/// </summary>
public class ModuleBoundaryTests
{
    [Fact]
    public void A_module_never_references_infrastructure()
    {
        var infrastructure = Repository.ProjectsUnder("src", "Infrastructure")
            .Select(p => Path.GetFileNameWithoutExtension(p.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(infrastructure);

        var offenders = Repository.ProjectsUnder("src", "Modules")
            .Where(module => Repository.ReferencedProjectNames(module).Any(infrastructure.Contains))
            .Select(module => module.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Modules declare ports; infrastructure implements them, never the other way round: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void A_module_never_references_another_module()
    {
        var modules = Repository.ProjectsUnder("src", "Modules").ToList();
        var names = modules.Select(m => Path.GetFileNameWithoutExtension(m.Name)).ToList();

        foreach (var module in modules)
        {
            var self = Path.GetFileNameWithoutExtension(module.Name);

            var otherModules = Repository.ReferencedProjectNames(module)
                .Where(reference => names.Contains(reference, StringComparer.OrdinalIgnoreCase))
                .Where(reference => !reference.StartsWith(OwningModuleOf(self), StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(
                otherModules.Count == 0,
                $"{module.Name} reaches into another module directly: {string.Join(", ", otherModules)}");
        }
    }

    [Fact]
    public void The_shared_kernel_depends_on_nothing_in_the_solution()
    {
        foreach (var project in Repository.ProjectsUnder("src", "Shared"))
        {
            Assert.True(
                !Repository.ReferencedProjectNames(project).Any(),
                $"{project.Name} must stay dependency-free — it is the one thing everything else may reference.");
        }
    }

    /// <summary>
    /// <c>Backlog.Modules.Backlog.Abstractions</c> and friends belong to the same
    /// module as <c>Backlog.Modules.Backlog</c>; only the first three segments identify it.
    /// </summary>
    private static string OwningModuleOf(string projectName)
    {
        var parts = projectName.Split('.');
        return parts.Length >= 3 ? string.Join('.', parts[..3]) : projectName;
    }
}
