namespace Backlog.ArchitectureTests;

/// <summary>
/// Rules for the desktop app's own bounded contexts. <c>src/App/Backlog.Desktop.UI</c>
/// is one project, but it hosts three of the contexts from
/// <c>.domain/context-map.md</c> — Inbox, Backlog Management, and Second Brain
/// (the Knowledge folder) — over a shared Workspace and a composing Shell.
/// <para>
/// One project means nothing stops a knowledge panel from injecting the backlog
/// list except somebody noticing. These tests are the noticing: a context may
/// only name another context's namespace where the context map says a
/// relationship exists.
/// </para>
/// </summary>
public class DesktopDomainBoundaryTests
{
    private const string App = "Backlog.Desktop.UI";
    private const string RootNamespace = "Backlog.Desktop.UI";

    /// <summary>The folders that carry a context, plus the two that do not: the
    /// Workspace everything may read, and the Shell that composes the rest.</summary>
    private static readonly string[] ContextFolders = ["Inbox", "BacklogManagement", "Knowledge"];

    [Fact]
    public void Each_context_has_its_own_folder()
    {
        foreach (var folder in ContextFolders.Concat(["Workspace", "Shell"]))
        {
            Assert.True(
                Directory.Exists(AppFolder(folder)),
                $"{App} keeps {folder} in its own folder; splitting the contexts is the whole point.");
        }
    }

    /// <summary>
    /// Inbox is upstream of both Backlog Management and Second Brain, and the
    /// two downstream contexts are a Partnership that coordinates by id rather
    /// than by reaching into each other. The one edge the map allows in code is
    /// Backlog Management conforming to the Inbox's published item contract.
    /// </summary>
    [Theory]
    [InlineData("Inbox", "BacklogManagement")]
    [InlineData("Inbox", "Knowledge")]
    [InlineData("Knowledge", "BacklogManagement")]
    [InlineData("Knowledge", "Inbox")]
    [InlineData("BacklogManagement", "Knowledge")]
    public void A_context_never_names_another_context(string context, string forbidden)
    {
        var offenders = FilesNaming(context, $"{RootNamespace}.{forbidden}");

        Assert.True(
            offenders.Count == 0,
            $"{context} must not depend on {forbidden} — see .domain/context-map.md for the relationships that do exist. "
            + string.Join(", ", offenders));
    }

    /// <summary>The shell composes the contexts, so it may see all of them. The
    /// reverse would make the panes unable to exist without the page they happen
    /// to sit on today.</summary>
    [Theory]
    [InlineData("Inbox")]
    [InlineData("BacklogManagement")]
    [InlineData("Knowledge")]
    [InlineData("Workspace")]
    public void Nothing_below_the_shell_depends_on_the_shell(string folder)
    {
        var offenders = FilesNaming(folder, $"{RootNamespace}.Shell");

        Assert.True(
            offenders.Count == 0,
            $"{folder} must not reach up into the Shell; the Shell wires it in. " + string.Join(", ", offenders));
    }

    /// <summary>The Workspace is where the data lives and which repositories are
    /// configured — the one thing every context is allowed to read. It stays
    /// underneath them all, so it may not read any of them back.</summary>
    [Theory]
    [InlineData("Inbox")]
    [InlineData("BacklogManagement")]
    [InlineData("Knowledge")]
    public void The_workspace_never_depends_on_a_context(string context)
    {
        var offenders = FilesNaming("Workspace", $"{RootNamespace}.{context}");

        Assert.True(
            offenders.Count == 0,
            $"Workspace must stay underneath {context}, not on top of it. " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The project-wide <c>_Imports.razor</c> is what decides which namespaces a
    /// component sees without asking. Listing a context there would hand every
    /// component in the app every other context's types, and the folder split
    /// would be filing rather than a boundary.
    /// </summary>
    [Fact]
    public void The_shared_razor_imports_stay_free_of_the_contexts()
    {
        var imports = Path.Combine(AppRoot, "_Imports.razor");
        Assert.True(File.Exists(imports), $"{App} should keep a project-wide _Imports.razor.");

        var text = File.ReadAllText(imports);
        foreach (var folder in ContextFolders.Concat(["Workspace", "Shell"]))
        {
            Assert.DoesNotContain($"{RootNamespace}.{folder}", text, StringComparison.Ordinal);
        }
    }

    private static string AppRoot => Path.Combine(Repository.Root.FullName, "src", "App", App);

    private static string AppFolder(string folder) => Path.Combine(AppRoot, folder);

    private static List<string> FilesNaming(string folder, string namespaceName)
    {
        var directory = new DirectoryInfo(AppFolder(folder));
        if (!directory.Exists) return [];

        return [.. directory
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => file.Extension is ".cs" or ".razor")
            .Where(file => File.ReadAllText(file.FullName).Contains(namespaceName, StringComparison.Ordinal))
            .Select(file => file.Name)];
    }
}
