using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Rules that keep the storybook honest about the library it documents.
///
/// <para>The dependency rules in <see cref="UiLibraryBoundaryTests"/> prove the
/// components <em>can</em> render without the application. These prove they are
/// actually rendered: a component with no story is one nobody can look at before
/// it ships, which is the whole reason the harness exists.</para>
/// </summary>
public class StorybookCoverageTests
{
    private static readonly DirectoryInfo Library =
        new(Path.Combine(Repository.Root.FullName, "src", "Core", "Backlog.UI.Components"));

    private static readonly DirectoryInfo StorybookPages =
        new(Path.Combine(Repository.Root.FullName, "src", "Harness", "Backlog.UI.Storybook", "Components", "Pages"));

    [Fact]
    public void Every_component_in_the_library_is_rendered_by_a_story()
    {
        var stories = StoryMarkup();
        var components = ComponentNames().ToList();

        // Both guards are against the test passing because it found nothing to
        // check — a moved folder would otherwise make it silently vacuous.
        Assert.NotEmpty(stories);
        Assert.NotEmpty(components);

        var missing = components
            // A component is covered when a page actually instantiates it, not
            // merely mentions it in prose: <Card> or <Card ... /> but not "Card".
            .Where(name => !Regex.IsMatch(stories, $@"<{Regex.Escape(name)}(\s|/|>)"))
            .OrderBy(name => name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Every component needs a story, or it ships without anyone having seen it. "
            + $"No storybook page renders: {string.Join(", ", missing)}");
    }

    /// <summary>Routes that exist to be landed on rather than navigated to, so
    /// nothing links to them on purpose.</summary>
    private static readonly HashSet<string> UnlistedByDesign = ["not-found"];

    [Fact]
    public void Every_storybook_page_is_reachable_from_the_index()
    {
        // A page nobody links to is a page nobody reviews. StorybookIndex is the
        // single list the sidebar and the introduction both render from, so a new
        // page has exactly one place to register itself.
        var index = File.ReadAllText(Path.Combine(
            Repository.Root.FullName,
            "src", "Harness", "Backlog.UI.Storybook", "Components", "Shared", "StorybookIndex.cs"));

        var unlisted = StorybookPages.EnumerateFiles("*.razor")
            .Select(page => Regex.Match(File.ReadAllText(page.FullName), @"@page\s+""/([^""]*)"""))
            .Where(route => route.Success)
            .Select(route => route.Groups[1].Value)
            .Where(route => !UnlistedByDesign.Contains(route))
            .Where(route => !Regex.IsMatch(index, $@"new\(""{Regex.Escape(route)}"""))
            .OrderBy(route => route)
            .ToList();

        Assert.True(
            unlisted.Count == 0,
            "These storybook routes are not in StorybookIndex, so nothing links to them: "
            + string.Join(", ", unlisted.Select(route => "/" + route)));
    }

    /// <summary>Every component the library offers a host: one .razor file each,
    /// minus _Imports, which is compiler plumbing rather than a component.</summary>
    private static IEnumerable<string> ComponentNames() =>
        Library.EnumerateFiles("*.razor", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(file => Path.GetFileNameWithoutExtension(file.Name))
            .Where(name => !name.StartsWith('_'));

    private static string StoryMarkup() =>
        string.Join('\n', StorybookPages.EnumerateFiles("*.razor").Select(file => File.ReadAllText(file.FullName)));
}
