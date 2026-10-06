using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A module's <c>.UI</c> project is a screen. It may take an adapter — that is
/// what <see cref="ModuleBoundaryTests.A_module_ui_may_take_an_adapter_but_never_another_modules_implementation"/>
/// allows — but it may not <em>be</em> one: no file in it opens a file, walks a
/// folder or starts a process.
///
/// <para>Nothing asked this of every module until issue #741. The reference
/// rules read csproj files, and a screen that does its own IO needs no reference
/// to do it, so three modules grew their adapters inside their UI projects
/// without any rule noticing. Sessions and Dashboard moved theirs out (#737,
/// #739) and Devbook has started to (#738); this is the rule that keeps the next
/// module from starting down the same road, and the one list of what is still
/// left to move.</para>
/// </summary>
public sealed partial class ModuleUiIoTests
{
    /// <summary>
    /// The module UI files that still open a file or start a process, each group
    /// with why it is still here and the issue that empties it. A ratchet: an
    /// entry may leave as its class moves behind a port answered in
    /// <c>src/Infrastructure</c>, and none may join.
    ///
    /// <para>This is the one list. The Devbook ratchet issue #738 left in
    /// <see cref="DevbookModuleRegistrationTests"/> is folded into it rather than
    /// kept beside it, so the two cannot drift.</para>
    /// </summary>
    internal static readonly string[] AllowedUiIo =
    [
        // Devbook: the readers, writers and walkers the first slice of #738 left
        // in the screen project once the index reader and its parse cache moved
        // into Backlog.Infrastructure.Devbook. The later slices of #738 move them.
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/Arc42Devbook.cs",
        // Also starts the Archify CLI through Process.
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/ArchifyDiagramArtifacts.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/C4Devbook.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/DevbookAtlas.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/DevbookChapterFileReader.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/DevbookChapterResolver.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/DevbookChapterWriter.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/DevbookFolderOpenService.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/DevbookMarkdownStatusWriter.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/DevbookMarkdownSyncWriter.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/DocumentDevbook.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/DomainDevbookStore.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/InstructionFileWalk.cs",
        // Only by name: it has a private Directory(path) helper of its own, which
        // the pattern cannot tell from System.IO.Directory.
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/InstructionLoading.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/InstructionPathTree.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/InstructionSourceDiscovery.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/TechnologyDevbook.cs",
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/UserContextSources.cs",
        // The instructions panel reads an instruction file's text straight off the
        // disk to show it. The Devbook ratchet read only .cs files, so this one was
        // not on it; it goes with the instruction readers above, under #738.
        "src/Modules/Devbook/Backlog.Modules.Devbook.UI/InstructionsDevbookPanel.razor",

        // Capture: the import row writes the picked manifest to a temp folder for
        // the run to read and deletes it afterwards. The run's port should take
        // the text instead, answered in Backlog.Infrastructure.Capture. No issue
        // is filed for it yet; #741 seeded the entry and its pull request names
        // the follow-up.
        "src/Modules/Capture/Backlog.Modules.Capture.UI/CaptureSourcesPanel.razor"
    ];

    [Fact]
    public void A_module_ui_does_no_file_or_process_io()
    {
        var screens = ModuleUiSourceFiles().ToList();

        Assert.NotEmpty(screens);

        var offenders = screens
            .Where(file => DoesIo(File.ReadAllText(file.FullName)))
            .Select(Repository.RelativePath)
            .Where(path => !AllowedUiIo.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A module's .UI project is a screen: it asks a port, and the adapter answering it under "
            + "src/Infrastructure opens the file or starts the process. These do it themselves:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// An exception that has stopped being one is worse than no exception list:
    /// it reads as a considered decision while quietly permitting anything. A
    /// file that moved out or stopped doing IO takes its entry with it, so the
    /// list can only shrink.
    /// </summary>
    [Fact]
    public void Every_allowed_ui_io_file_still_does_io()
    {
        var stale = AllowedUiIo
            .Where(path =>
            {
                var file = new FileInfo(Path.Combine([Repository.Root.FullName, .. path.Split('/')]));
                return !file.Exists || !DoesIo(File.ReadAllText(file.FullName));
            })
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These files no longer exist or no longer do file or process IO, and should leave "
            + nameof(AllowedUiIo) + ":\n" + string.Join('\n', stale));
    }

    /// <summary>Every allowed entry is a file in a module's <c>.UI</c> project; an
    /// entry anywhere else permits nothing and reads as if it did.</summary>
    [Fact]
    public void Every_allowed_ui_io_file_is_in_a_module_ui_project()
    {
        var screens = ModuleUiSourceFiles()
            .Select(Repository.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var elsewhere = AllowedUiIo.Where(path => !screens.Contains(path)).ToList();

        Assert.True(
            elsewhere.Count == 0,
            "These entries are not source files of a module .UI project: " + string.Join(", ", elsewhere));
    }

    /// <summary>
    /// The rule above is only as good as its pattern, and a pattern that stopped
    /// matching would turn it green rather than red. One line per API the rule
    /// names, and the path helpers a screen may still use.
    /// </summary>
    [Theory]
    [InlineData("var text = File.ReadAllText(path);")]
    [InlineData("await File.WriteAllTextAsync(path, text);")]
    [InlineData("Directory.CreateDirectory(folder);")]
    [InlineData("var info = new FileInfo(path);")]
    [InlineData("foreach (var d in new DirectoryInfo(root).EnumerateDirectories()) { }")]
    [InlineData("using var stream = new FileStream(path, FileMode.Open);")]
    [InlineData("var watcher = new FileSystemWatcher(root);")]
    [InlineData("using System.IO.Enumeration;")]
    [InlineData("Process.Start(\"archify\");")]
    [InlineData("var start = new ProcessStartInfo\n{\n    FileName = \"archify\"\n};")]
    [InlineData("var p = new System.Diagnostics.Process();")]
    public void The_io_pattern_catches_each_api_the_rule_names(string source)
    {
        Assert.True(DoesIo(source), $"The IO pattern does not catch: {source}");
    }

    /// <inheritdoc cref="The_io_pattern_catches_each_api_the_rule_names" />
    [Theory]
    [InlineData("var path = Path.Combine(root, \"a.md\");")]
    [InlineData("var name = Path.GetFileName(path);")]
    [InlineData("catch (IOException) { }")]
    [InlineData("<span>@chapter.FileName</span>")]
    public void The_io_pattern_leaves_path_arithmetic_alone(string source)
    {
        Assert.False(DoesIo(source), $"The IO pattern flags code that does no IO: {source}");
    }

    /// <summary>Whether <paramref name="source"/> touches the disk or starts a
    /// process: the same pattern the Sessions and Devbook rules used, with the
    /// watcher, the enumeration namespace and an object-initialised
    /// <c>new ProcessStartInfo</c> added.</summary>
    internal static bool DoesIo(string source) => DiskOrProcess().IsMatch(source);

    /// <summary>The <c>.cs</c> and <c>.razor</c> files of every
    /// <c>src/Modules/*/*.UI</c> project, build output left out.</summary>
    private static IEnumerable<FileInfo> ModuleUiSourceFiles() =>
        Repository.ProjectsUnder("src", "Modules")
            .Where(Repository.IsUserInterface)
            .SelectMany(project => Repository.SourceFilesUnder(project.Directory!, ".cs", ".razor"));

    [GeneratedRegex(
        @"\b(File|Directory|FileInfo|DirectoryInfo|FileStream|FileSystemWatcher|Process|ProcessStartInfo)\s*[.(]"
        + @"|\bnew\s+(System\.IO\.|System\.Diagnostics\.)?(FileInfo|DirectoryInfo|FileStream|FileSystemWatcher|Process|ProcessStartInfo)\b"
        + @"|\bSystem\.IO\.Enumeration\b")]
    private static partial Regex DiskOrProcess();
}
