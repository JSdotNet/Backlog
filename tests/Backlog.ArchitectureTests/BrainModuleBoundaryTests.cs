using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The Brain is its own bounded context, and Devbook knows nothing of it (local ADR
/// 0040, Verification 2).
///
/// <para>
/// The Brain conforms to Devbook: it reads, indexes and renders its notes with
/// Devbook's machinery, and shows them on the Devbook pane through the pane's mode
/// extension point. The edge runs one way. Devbook's three projects name no Brain
/// type, no Brain namespace and none of the Brain's own words, the PARA areas, the
/// Ignore list and Capture, and reference no Brain project. A Brain concept that
/// creeps back into Devbook fails here with the file and line, rather than waiting
/// for someone to notice the context map no longer holds.
/// </para>
/// <para>
/// The forbidden type names are read from the Brain projects themselves, so a Brain
/// type added later is forbidden in Devbook without touching this file.
/// <see cref="The_brain_type_list_is_read_from_the_brain_projects"/> keeps that read
/// from coming back empty and passing for the wrong reason.
/// </para>
/// </summary>
public sealed partial class BrainModuleBoundaryTests
{
    private const string BrainModules = "src/Modules/Brain";
    private const string BrainAdapter = "src/Infrastructure/Backlog.Infrastructure.Brain";
    private const string DevbookAbstractions = "src/Modules/Devbook/Backlog.Modules.Devbook.Abstractions";
    private const string DevbookUi = "src/Modules/Devbook/Backlog.Modules.Devbook.UI";
    private const string DevbookAdapter = "src/Infrastructure/Backlog.Infrastructure.Devbook";

    private static readonly string[] DevbookProjects = [DevbookAbstractions, DevbookUi, DevbookAdapter];

    /// <summary>
    /// Where "capture" means a screenshot a scenario run took, not the Brain's
    /// Capture. Only the plain word is let through here; a code name such as
    /// <c>CanCapture</c> or <c>OnCaptured</c> is still reported in these files.
    /// A path ending in <c>/</c> covers the folder.
    /// </summary>
    private static readonly (string Path, string Reason)[] ScreenshotCapture =
    [
        ($"{DevbookUi}/DevbookScenarioShot.razor", "a scenario screenshot the run captured"),
        ($"{DevbookUi}/DevbookScenarioStore.cs", "a scenario screenshot the run captured"),
        ($"{DevbookAdapter}/Scenarios/", "a scenario screenshot the run captured"),
    ];

    /// <summary>
    /// Devbook members that keep a name saying Brain, accepted under scope decision
    /// Q1 of the ADR 0040 change: each carries no Brain type and none of the Brain's
    /// words, and shell code or tests address it by that name. They are listed so the
    /// acceptance is explicit — <see cref="Accepted_residue_names_no_brain_concept"/>
    /// proves each still matches no forbidden pattern, so none of them is an exemption.
    /// </summary>
    public static TheoryData<string, string> AcceptedResidue() => new()
    {
        { "ShowsBrainChanged", $"{DevbookUi}/DevbookPane.razor" },
        { "BrainMode", $"{DevbookUi}/DevbookPane.razor" },
        { "OnOpenRepositoryChapter", $"{DevbookUi}/DevbookPane.razor" },
        { "BrainOffered", $"{DevbookUi}/DevbookScope.cs" },
        { "IsBrain", $"{DevbookUi}/DevbookAtlas.cs" },
        { "IsBrain", $"{DevbookUi}/DevbookSearchMerge.cs" },
        { "ForBrainNote", $"{DevbookUi}/DevbookChapterLink.cs" },
        { "LoadBrainAsync", $"{DevbookUi}/DevbookMenu.cs" },
        { "devbook-stack--brain", $"{DevbookUi}/DevbookPane.razor" },
        { "devbook-stack__brain-", $"{DevbookUi}/DevbookPane.razor" },
    };

    [Fact]
    public void Devbook_names_no_brain_concept()
    {
        var patterns = ForbiddenPatterns();

        var offenders = DevbookProjects
            .SelectMany(project => SourceFiles(project, ".cs", ".razor"))
            .SelectMany(file => Offences(file, patterns))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Devbook names a Brain concept. The Brain conforms to Devbook, never the other way "
            + "(local ADR 0040); move the code to the Brain projects, or reword the comment:\n"
            + string.Join('\n', offenders));
    }

    [Fact]
    public void Devbook_references_no_brain_project()
    {
        var offenders = DevbookProjects
            .Select(project => ProjectFile(project))
            .SelectMany(csproj => XDocument.Load(csproj.FullName)
                .Descendants()
                .Where(element => element.Name.LocalName is "ProjectReference" or "InternalsVisibleTo")
                .Select(element => (string?)element.Attribute("Include") ?? "")
                .Where(NamesABrainProject)
                .Select(include => $"{Repository.RelativePath(csproj)}: {include}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A Devbook project references a Brain project or opens its internals to one "
            + "(local ADR 0040):\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Devbook_holds_no_brain_file()
    {
        var offenders = new[] { "src/Modules/Devbook", DevbookAdapter }
            .Select(Folder)
            .Where(folder => folder.Exists)
            .SelectMany(folder => folder.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            .Where(item => !IsBuildOutput(item))
            .Where(item => item is FileInfo
                ? item.Name.StartsWith("Brain", StringComparison.Ordinal)
                : item.Name.Equals("Brain", StringComparison.Ordinal))
            .Select(Repository.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A Brain file or folder is back under Devbook; it belongs in src/Modules/Brain or "
            + "Backlog.Infrastructure.Brain (local ADR 0040):\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void The_brain_type_list_is_read_from_the_brain_projects()
    {
        var types = BrainTypeNames();

        Assert.NotEmpty(types);

        foreach (var expected in new[]
        {
            "BrainConventions", "BrainArea", "IBrainNoteWriter", "BrainNoteDraft", "BrainCorpusProfile", "BrainDevbookPanel",
        })
        {
            Assert.Contains(expected, types);
        }
    }

    [Theory]
    [MemberData(nameof(AcceptedResidue))]
    public void Accepted_residue_names_no_brain_concept(string name, string path)
    {
        var file = new FileInfo(Path.Combine([Repository.Root.FullName, .. path.Split('/')]));

        Assert.True(file.Exists, $"{path} is gone; drop {name} from AcceptedResidue.");
        Assert.True(
            File.ReadAllText(file.FullName).Contains(name, StringComparison.Ordinal),
            $"{name} is no longer in {path}; drop it from AcceptedResidue.");

        var matches = ForbiddenPatterns()
            .Where(pattern => pattern.Regex.IsMatch(name))
            .Select(pattern => pattern.Regex.ToString())
            .ToList();

        Assert.True(
            matches.Count == 0,
            $"{name} is accepted residue, yet it matches a forbidden pattern: rename it instead:\n"
            + string.Join('\n', matches));
    }

    [Fact]
    public void Backlog_Infrastructure_Brain_is_the_brains_adapter()
    {
        var adapter = References(ProjectFile(BrainAdapter));

        Assert.Contains("Backlog.Modules.Brain.Abstractions", adapter);
        Assert.DoesNotContain(adapter, name => name.EndsWith(".UI", StringComparison.Ordinal));

        foreach (var project in new[] { "src/Infrastructure/Backlog.Infrastructure.FileSystem", DevbookAdapter })
        {
            var references = References(ProjectFile(project));

            Assert.True(
                !references.Any(NamesABrainProject),
                $"{project} references a Brain project; Backlog.Infrastructure.Brain is the Brain's adapter:\n"
                + string.Join('\n', references.Where(NamesABrainProject)));
        }

        var composition = References(ProjectFile("src/App/Backlog.Desktop.Composition"));

        Assert.Contains("Backlog.Modules.Brain.UI", composition);
        Assert.Contains("Backlog.Infrastructure.Brain", composition);

        foreach (var head in new[] { "src/App/Backlog.Desktop", "src/Harness/Backlog.Desktop.WebHarness" })
        {
            Assert.Contains("Backlog.Desktop.Composition", References(ProjectFile(head)));
        }

        var brainProjects = new[]
        {
            "src/Infrastructure/Backlog.Infrastructure.Brain/Backlog.Infrastructure.Brain.csproj",
            "tests/Backlog.Infrastructure.Brain.UnitTests/Backlog.Infrastructure.Brain.UnitTests.csproj",
            "src/Modules/Brain/Backlog.Modules.Brain.Abstractions/Backlog.Modules.Brain.Abstractions.csproj",
            "src/Modules/Brain/Backlog.Modules.Brain.UI/Backlog.Modules.Brain.UI.csproj",
        };

        var solution = File.ReadAllText(Path.Combine(Repository.Root.FullName, "Backlog.sln")).Replace('\\', '/');
        var filter = SolutionFilterProjects();

        foreach (var project in brainProjects)
        {
            Assert.True(solution.Contains($"\"{project}\"", StringComparison.Ordinal), $"{project} is not in Backlog.sln.");
            Assert.True(filter.Contains(project), $"{project} is not in Backlog.WithoutAppHeads.slnf.");
        }
    }

    [Fact]
    public void AddBrainModule_is_defined_once_in_the_brain_ui_extensions_folder()
    {
        var definitions = new[] { "src" }
            .SelectMany(root => SourceFiles(root, ".cs"))
            .Where(file => ModuleDefinition().IsMatch(file.Text))
            .ToList();

        var definition = Assert.Single(definitions);

        Assert.Equal($"{BrainModules}/Backlog.Modules.Brain.UI/Extensions/BrainModuleRegistration.cs", definition.RelativePath);
    }

    /// <summary>
    /// Every type declared in the Brain projects: the <c>class</c>, <c>record</c>,
    /// <c>interface</c>, <c>enum</c> and <c>struct</c> names in their C# files, and each
    /// Razor component by its file name. Comment lines are skipped and a type name
    /// starts with a capital, so prose such as "a class that" adds no "that".
    /// </summary>
    private static HashSet<string> BrainTypeNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in new[] { BrainModules, BrainAdapter })
        {
            foreach (var file in SourceFiles(root, ".cs"))
            {
                var code = string.Join('\n', file.Text.Split('\n').Where(line => !CommentLine().IsMatch(line)));

                foreach (Match match in TypeDeclaration().Matches(code))
                {
                    names.Add(match.Groups[1].Value);
                }
            }

            foreach (var file in SourceFiles(root, ".razor"))
            {
                var stem = Path.GetFileNameWithoutExtension(file.RelativePath);

                if (!stem.StartsWith('_')) names.Add(stem);
            }
        }

        return names;
    }

    private static List<ForbiddenPattern> ForbiddenPatterns()
    {
        var patterns = BrainTypeNames()
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new ForbiddenPattern(new Regex($@"\b{Regex.Escape(name)}\b"), ScreenshotExempt: false))
            .ToList();

        patterns.AddRange(
        [
            // The Brain's namespaces.
            new(new Regex(@"Backlog\.Modules\.Brain"), false),
            new(new Regex(@"Backlog\.Infrastructure\.Brain"), false),

            // The PARA areas: the acronym, the stored attribute, and a lowercase para
            // outside the <para> doc tags.
            new(new Regex(@"\bPARA\b"), false),
            new(new Regex("\"para\""), false),
            new(new Regex(@"(?<![</])\bpara\b(?!>)"), false),

            // The Ignore list.
            new(new Regex(@"\bIgnoreHeading\b"), false),
            new(new Regex(@"\bReadIgnored\b"), false),
            new(new Regex(@"\bignore list\b", RegexOptions.IgnoreCase), false),

            // A Brain area's key names.
            new(new Regex(@"\bBrainArea\w*"), false),

            // Capture: the word, except where it means a scenario screenshot, and the
            // code names everywhere.
            new(new Regex(@"\bcapture\b", RegexOptions.IgnoreCase), true),
            new(new Regex(@"\b(?:Can|On|Brain)Captur\w*"), false),
        ]);

        return patterns;
    }

    private static IEnumerable<string> Offences(SourceFile file, List<ForbiddenPattern> patterns)
    {
        var screenshot = ScreenshotCapture.Any(exempt => exempt.Path.EndsWith('/')
            ? file.RelativePath.StartsWith(exempt.Path, StringComparison.Ordinal)
            : file.RelativePath.Equals(exempt.Path, StringComparison.Ordinal));

        var lines = file.Text.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            foreach (var pattern in patterns)
            {
                if (screenshot && pattern.ScreenshotExempt) continue;

                var match = pattern.Regex.Match(lines[index]);

                if (match.Success) yield return $"{file.RelativePath}:{index + 1}: {match.Value}";
            }
        }
    }

    private static bool NamesABrainProject(string include) =>
        include.Contains("Backlog.Modules.Brain.", StringComparison.Ordinal)
        || include.Contains("Backlog.Infrastructure.Brain", StringComparison.Ordinal);

    private static List<string> References(FileInfo csproj) => Repository.ReferencedProjectNames(csproj).ToList();

    private static FileInfo ProjectFile(string project)
    {
        var folder = Folder(project);
        var csproj = new FileInfo(Path.Combine(folder.FullName, folder.Name + ".csproj"));

        Assert.True(csproj.Exists, $"{project} has no {folder.Name}.csproj.");

        return csproj;
    }

    private static HashSet<string> SolutionFilterProjects()
    {
        using var filter = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Repository.Root.FullName, "Backlog.WithoutAppHeads.slnf")));

        return filter.RootElement.GetProperty("solution").GetProperty("projects")
            .EnumerateArray()
            .Select(project => project.GetString()!.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static DirectoryInfo Folder(string relative) =>
        new(Path.Combine([Repository.Root.FullName, .. relative.Split('/')]));

    private static bool IsBuildOutput(FileSystemInfo item)
    {
        var path = Path.GetRelativePath(Repository.Root.FullName, item.FullName).Replace('\\', '/');

        return path.Split('/').Any(segment => segment is "bin" or "obj");
    }

    private static IEnumerable<SourceFile> SourceFiles(string root, params string[] extensions) =>
        Repository.SourceFilesUnder(Folder(root), extensions)
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .Select(file => new SourceFile(Repository.RelativePath(file), File.ReadAllText(file.FullName)));

    private sealed record SourceFile(string RelativePath, string Text);

    /// <param name="ScreenshotExempt">Skipped in <see cref="ScreenshotCapture"/>'s files.</param>
    private sealed record ForbiddenPattern(Regex Regex, bool ScreenshotExempt);

    [GeneratedRegex(@"(?:class|record|interface|enum|struct)\s+([A-Z]\w*)")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"^\s*(?://|\*|/\*)")]
    private static partial Regex CommentLine();

    [GeneratedRegex(@"public\s+static\s+IServiceCollection\s+AddBrainModule\s*\(")]
    private static partial Regex ModuleDefinition();
}
