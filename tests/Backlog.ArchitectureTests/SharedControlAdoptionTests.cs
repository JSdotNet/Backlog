using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// <see cref="UiLibraryBoundaryTests"/> proves the applications reference the
/// shared library. Referencing it is not using it: a screen can take the
/// dependency and still hand-roll a button, and then the storybook documents a
/// control nobody renders while the app ships a second one nobody reviewed.
///
/// <para>These rules are about the second half — that a control the library
/// defines is the control the application renders. They are deliberately
/// narrow: a raw element is only a finding when the library has a component for
/// the job, and the exceptions below say which elements are not that.</para>
/// </summary>
public class SharedControlAdoptionTests
{
    /// <summary>The interactive elements the library ships a component for:
    /// AppButton/IconButton/ToggleButton, TextField, TextArea, and the select
    /// family.</summary>
    private static readonly string[] RawControls = ["button", "input", "select", "textarea"];

    /// <summary>
    /// The raw elements that are not a component the library is missing.
    ///
    /// <para>Each is keyed on a class the element carries, and each is here for
    /// a reason written next to it in the markup:</para>
    /// <list type="bullet">
    /// <item>the markdown editors hold a reading line inside the grow wrapper,
    /// replicate the text plus a trailing newline, and are focused through an
    /// <c>ElementReference</c> — TextArea shapes a labelled field, not an
    /// editing surface;</item>
    /// <item>the reorder grips are drop targets, and the
    /// <c>ondragover:preventDefault</c> they need is a directive attribute that
    /// cannot be written on a component at all.</item>
    /// </list>
    ///
    /// <para>Adding an entry here is a claim that the library has nothing to
    /// adopt. If the answer is instead that a component is missing a hook, the
    /// hook belongs in the library — that is what every entry above was
    /// weighed against.</para>
    /// </summary>
    private static readonly string[] AllowedRawControls =
    [
        "entry-doc__editor",
        "subitem-card__editor",
        "entry-doc__grip",
        "subitem-card__grip"
    ];

    [Fact]
    public void Application_screens_render_the_librarys_controls_rather_than_their_own()
    {
        var screens = ApplicationScreens().ToList();

        Assert.NotEmpty(screens);

        var offenders = new List<string>();

        foreach (var screen in screens)
        {
            var lines = File.ReadAllLines(screen.FullName);

            for (var index = 0; index < lines.Length; index++)
            {
                var tag = OpeningControlTag(lines[index]);
                if (tag is null) continue;

                // The opening tag runs over several lines here, and an event
                // handler in it can contain a `>` of its own, so the element is
                // read as a window rather than parsed.
                var window = string.Join('\n', lines.Skip(index).Take(12));
                if (AllowedRawControls.Any(allowed => window.Contains(allowed, StringComparison.Ordinal))) continue;

                offenders.Add($"{Relative(screen)}:{index + 1} <{tag}>");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These screens hand-roll a control the shared library already defines. Render the component, "
            + "and if it cannot wear the screen's classes, give it the hook rather than a second implementation:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// An exception that has stopped being one is worse than no exception list:
    /// it reads as a considered decision while quietly permitting anything.
    /// </summary>
    [Fact]
    public void Every_allowed_raw_control_is_still_a_raw_control_somewhere()
    {
        var markup = string.Join('\n', ApplicationScreens().Select(file => File.ReadAllText(file.FullName)));

        Assert.NotEmpty(markup);

        var stale = AllowedRawControls
            .Where(allowed => !markup.Contains(allowed, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These raw-control exceptions no longer match anything and should be deleted: "
            + string.Join(", ", stale));
    }

    private static string? OpeningControlTag(string line) =>
        RawControls.FirstOrDefault(control => Regex.IsMatch(line, $@"<{control}(\s|>|$)"));

    /// <summary>Every .razor file in the application UI projects — the screens a
    /// user actually sees, as opposed to the harnesses that host them.</summary>
    private static IEnumerable<FileInfo> ApplicationScreens() =>
        Repository.ProjectsUnder("src", "App")
            .Where(project => project.Name.EndsWith(".UI.csproj", StringComparison.OrdinalIgnoreCase))
            .SelectMany(project => project.Directory!.EnumerateFiles("*.razor", SearchOption.AllDirectories))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Name.StartsWith('_'));

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
