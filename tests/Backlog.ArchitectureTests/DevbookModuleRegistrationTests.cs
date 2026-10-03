using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The Devbook module's registration, its path helper and its file IO, each pinned
/// where issue #738 put it.
///
/// <para>
/// The two desktop heads used to register some twenty Devbook services line by line,
/// each its own copy, and the copies had drifted: the harness built the Copilot
/// launcher's two consumers by hand and held the domain store per circuit, the MAUI
/// head did neither. <c>AddDevbookModule</c> in the Devbook UI project is the one copy
/// now, called from <c>AddDesktopComposition</c> with what each head does differently
/// as parameters. Whether every service still resolves in each head is
/// <c>Backlog.HostComposition.UnitTests</c>' to prove; this is the text rule that keeps
/// the heads from growing a copy again, the same shape and the same limits as
/// <see cref="DesktopCompositionTests"/>.
/// </para>
/// </summary>
public sealed partial class DevbookModuleRegistrationTests
{
    private const string DevbookUi = "src/Modules/Devbook/Backlog.Modules.Devbook.UI";

    private static readonly string[] Heads =
    [
        "src/App/Backlog.Desktop/MauiProgram.cs",
        "src/Harness/Backlog.Desktop.WebHarness/Program.cs"
    ];

    /// <summary>Every service type the heads' inline Devbook blocks registered before
    /// <c>AddDevbookModule</c>, the ports and the classes alike.</summary>
    public static TheoryData<string> DevbookServices() =>
    [
        "IDevbookSnapshotCache",
        "DevbookSnapshotCache",
        "DevbookDatabaseRefresher",
        "IDevbookFolderSource",
        "DevbookFolderSource",
        "DesignDevbookProvider",
        "AiDevbookProvider",
        "TechnologyDevbookService",
        "DevbookAtlasService",
        "IDevbookSearch",
        "IDevbookVectorSearch",
        "InstructionSourceDiscovery",
        "DevbookMenu",
        "DevbookCopilotCli",
        "IDiagramArtifactSource",
        "ArchifyDiagramArtifacts",
        "DevbookScope",
        "DevbookUpdateService",
        "DevbookSourceSelection",
        "DevbookFolderOpenService",
        "Arc42DevbookStore",
        "C4DevbookStore",
        "DevbookChapterWriter",
        "DomainDevbookStore",
        "IDevbookAnnotationStore",
        "DevbookAnnotationStore"
    ];

    /// <summary>
    /// The files in the Devbook UI project that still open a file or start a process
    /// after the first slice of issue #738 moved the index reader and its parse cache
    /// into <c>Backlog.Infrastructure.Devbook</c>. A ratchet: a name may leave this
    /// list as its class moves out, and none may join it.
    /// </summary>
    private static readonly string[] RemainingFileIo =
    [
        "Arc42Devbook.cs",
        "ArchifyDiagramArtifacts.cs",
        "C4Devbook.cs",
        "DevbookAtlas.cs",
        "DevbookChapterFileReader.cs",
        "DevbookChapterResolver.cs",
        "DevbookChapterWriter.cs",
        "DevbookFolderOpenService.cs",
        "DevbookMarkdownStatusWriter.cs",
        // Landed on main beside the status writer (#939) while the ratchet was in review.
        "DevbookMarkdownSyncWriter.cs",
        "DocumentDevbook.cs",
        "DomainDevbookStore.cs",
        "InstructionFileWalk.cs",
        // Only by name: it has a private Directory(path) helper of its own, which
        // the pattern cannot tell from System.IO.Directory.
        "InstructionLoading.cs",
        "InstructionPathTree.cs",
        "InstructionSourceDiscovery.cs",
        "TechnologyDevbook.cs",
        "UserContextSources.cs"
    ];

    [Fact]
    public void AddDevbookModule_is_defined_once_in_the_devbook_ui_extensions_folder()
    {
        var definitions = SourceFiles("src", "*.cs")
            .Where(file => ModuleDefinition().IsMatch(file.Text))
            .ToList();

        var definition = Assert.Single(definitions);

        Assert.Equal($"{DevbookUi}/Extensions/DevbookModuleRegistration.cs", definition.RelativePath);

        // The two things the heads do differently arrive as parameters rather than
        // as a branch inside: which Copilot launcher this host may start, and how
        // long one reader's domain store lives.
        var signature = ModuleDefinition().Match(definition.Text).Value;
        Assert.Contains("ICopilotCliLauncher", signature, StringComparison.Ordinal);
        Assert.Contains("ServiceLifetime", signature, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DevbookServices))]
    public void No_desktop_head_registers_a_devbook_service_of_its_own(string service)
    {
        var registration = new Regex(
            @"\.(Add|AddSingleton|AddScoped|AddTransient|TryAdd\w*)\b[^;]*\b" + Regex.Escape(service) + @"\b");

        var offenders = Heads
            .Where(head => registration.IsMatch(File.ReadAllText(Path.Combine([Repository.Root.FullName, .. head.Split('/')]))))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{service} is registered by AddDevbookModule through AddDesktopComposition. A head that "
            + "registers it as well is the line-by-line copy issue #738 removed:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// The panels used to carry a private <c>PathMatches</c> and <c>NormalizePath</c>
    /// each, five copies of which one had diverged. One helper now, in the module's
    /// published surface beside <c>DevbookLayout</c>.
    /// </summary>
    [Fact]
    public void The_devbook_panels_share_one_path_helper()
    {
        var copies = SourceFiles(DevbookUi, "*.razor")
            .Concat(SourceFiles(DevbookUi, "*.cs"))
            .Where(file => PrivatePathHelper().IsMatch(file.Text))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            copies.Count == 0,
            "A Devbook panel has a path helper of its own again; use DevbookPath instead:\n" + string.Join('\n', copies));

        var helper = Path.Combine(
            Repository.Root.FullName, "src", "Modules", "Devbook", "Backlog.Modules.Devbook.Abstractions", "DevbookPath.cs");

        Assert.True(File.Exists(helper), $"{helper} is not where the shared path helper lives.");
        Assert.Matches(@"public\s+static\s+class\s+DevbookPath\b", File.ReadAllText(helper));
    }

    /// <summary>
    /// The first slice of the file IO out of the Devbook UI project: the reader of the
    /// generated outline and the parse cache it remembers answers in now sit with the
    /// rest of the database's read side, and the screen project does no new file IO.
    /// Modelled on <see cref="ModuleBoundaryTests.Backlog_Modules_Sessions_UI_is_a_screen_and_Backlog_Infrastructure_Sessions_is_its_adapter"/>.
    /// </summary>
    [Fact]
    public void Backlog_Modules_Devbook_UI_does_no_more_file_io_than_it_did()
    {
        var diskOrProcess = new Regex(
            @"\b(File|Directory|FileInfo|DirectoryInfo|FileStream|Process|ProcessStartInfo)\s*[.(]|using\s+System\.Diagnostics\s*;");

        var doingIo = SourceFiles(DevbookUi, "*.cs")
            .Where(file => diskOrProcess.IsMatch(file.Text))
            .Select(file => Path.GetFileName(file.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = doingIo.Where(name => !RemainingFileIo.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(
            added.Count == 0,
            "Backlog.Modules.Devbook.UI reads the disk or starts a process in a file the ratchet does not list; "
            + "put that in an adapter instead: " + string.Join(", ", added));

        // A name that no longer does IO is an exception that has stopped being one.
        var stale = RemainingFileIo.Where(name => !doingIo.Contains(name)).ToList();
        Assert.True(
            stale.Count == 0,
            "These files no longer do file IO and should leave the ratchet: " + string.Join(", ", stale));

        var adapter = Path.Combine(Repository.Root.FullName, "src", "Infrastructure", "Backlog.Infrastructure.Devbook");

        foreach (var moved in new[] { "DevbookIndexReader.cs", "DevbookFileCache.cs" })
        {
            Assert.False(
                File.Exists(Path.Combine([Repository.Root.FullName, .. DevbookUi.Split('/'), moved])),
                $"{moved} is back in the Devbook UI project; it belongs in Backlog.Infrastructure.Devbook.");
            Assert.True(File.Exists(Path.Combine(adapter, moved)), $"{moved} is not in Backlog.Infrastructure.Devbook.");
        }
    }

    private static IEnumerable<(string RelativePath, string Text)> SourceFiles(string root, string pattern)
    {
        var folder = new DirectoryInfo(Path.Combine([Repository.Root.FullName, .. root.Split('/')]));

        foreach (var file in folder.EnumerateFiles(pattern, SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            yield return (
                Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/'),
                File.ReadAllText(file.FullName));
        }
    }

    [GeneratedRegex(@"public\s+static\s+IServiceCollection\s+AddDevbookModule\s*\(\s*this\s+IServiceCollection\s+services\s*,[^)]*\)")]
    private static partial Regex ModuleDefinition();

    [GeneratedRegex(@"static\s+[\w<>?]+\s+NormalizePath\s*\(|static\s+bool\s+PathMatches\s*\(")]
    private static partial Regex PrivatePathHelper();
}
