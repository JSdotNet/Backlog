using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The storybook's reading order: nothing is shown before its parts.
///
/// <para><c>StorybookIndex</c> is read top to bottom by someone who has not met
/// the library, so a page may only draw components a page above it has already
/// introduced. The index's own remarks have carried that rule for a while, and
/// the pages bent it in more places than the remarks recorded — a rule that lives
/// in a comment is checked by whoever reads the comment, which is nobody at merge
/// time. This reads the order back out of the index, works out each component's
/// own chapter from its library folder, and fails on a page that draws a component
/// whose chapter comes later.</para>
///
/// <para>The bends the rule allows are listed here, one line each, and the same
/// bend is recorded in the comment on the index entry it belongs to. Both lists
/// are checked: a violation not in the list fails, and so does a listed bend the
/// index no longer makes, so the list cannot outlive the exceptions it
/// describes.</para>
/// </summary>
public class StorybookOrderTests
{
    private static readonly DirectoryInfo Library =
        new(Path.Combine(Repository.Root.FullName, "src", "Core", "Backlog.UI.Components"));

    private static readonly DirectoryInfo StorybookPages =
        new(Path.Combine(Repository.Root.FullName, "src", "Harness", "Backlog.UI.Storybook", "Components", "Pages"));

    private static readonly FileInfo Index =
        new(Path.Combine(Repository.Root.FullName, "src", "Harness", "Backlog.UI.Storybook", "Components", "Shared", "StorybookIndex.cs"));

    /// <summary>Which routes document a library folder's components as their own
    /// subject, by prefix. A component's home is the first of these pages, in
    /// reading order, that draws it; a page outside its folder's chapters drawing
    /// it is a borrow, and a borrow is only allowed from a page above.
    ///
    /// <para>Two folders are documented on chapters named for something else:
    /// <c>Metadata</c> on the Knowledge base pages, because a metadata record is a
    /// knowledge chapter's, and <c>Tasks</c> on Inputs as well as Task list,
    /// because <c>TaskAction</c> is introduced with the date, time and repeat it
    /// sits over.</para></summary>
    private static readonly IReadOnlyDictionary<string, string[]> Chapters = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Badges"] = ["badges"],
        ["Buttons"] = ["buttons"],
        ["Code"] = ["code"],
        ["Compare"] = ["compare"],
        ["Data"] = ["data-table"],
        ["Diagrams"] = ["diagrams", "graph-explorer", "graph-atlas"],
        ["Feedback"] = ["feedback"],
        ["Inputs"] = ["inputs", "file-field"],
        ["Integrations"] = ["integrations"],
        ["Knowledge"] = ["knowledge-base", "markdown/references"],
        ["Layout"] = ["layout", "file-view", "folder-view"],
        ["Markdown"] = ["markdown", "markdown-document"],
        ["Menus"] = ["menus"],
        ["Metadata"] = ["knowledge-base"],
        ["Metrics"] = ["usage-metrics", "productivity"],
        ["Overlays"] = ["overlays"],
        ["Roadmap"] = ["roadmap"],
        ["Selection"] = ["selection-bar"],
        ["Selects"] = ["selects"],
        ["Tasks"] = ["task-list", "inputs"]
    };

    /// <summary>The bends the index records. Each reason is the one the comment on
    /// that index entry gives, shortened to a line; the comment is where the
    /// argument lives.</summary>
    private static readonly (string Route, string Component, string Reason)[] RecordedBends =
    [
        ("foundations", "Alert", "A token page precedes every component page by nature; the alert is chrome for the measurement, not its subject."),
        ("foundations", "Badge", "A token page precedes every component page by nature; the badge is chrome for the measurement, not its subject."),
        ("foundations", "Spinner", "A token page precedes every component page by nature; the spinner is chrome for the measurement, not its subject."),
        ("inputs", "SelectField", "The repeat picker is one of the dates-and-times set a scheduled thing takes; Selects is the next page."),
        ("selection-bar", "TaskActionPane", "The slot demo is the composition the backlog arrived at; Task list is its chapter."),
        ("selection-bar", "TaskActionGroup", "The slot demo is the composition the backlog arrived at; Task list is its chapter."),
        ("data-table", "ProviderMark", "A cell is a template the caller fills, and the examples fill theirs from Integrations; the table is a content item by kind."),
        ("data-table", "IntegrationStateChip", "A cell is a template the caller fills, and the examples fill theirs from Integrations; the table is a content item by kind."),
        ("markdown/references", "MetadataView", "The page is about the inline, and a record is the other place a reference appears; Knowledge base is the record's chapter.")
    ];

    [Fact]
    public void Every_page_renders_only_components_a_page_above_it_introduced()
    {
        var order = ReadingOrder();
        var rendered = RenderedComponentsByRoute();
        var folders = ComponentFolders();

        // Against the test passing because a moved file or a reshaped index left
        // it nothing to check.
        Assert.NotEmpty(order);
        Assert.NotEmpty(rendered);
        Assert.NotEmpty(folders);

        var position = order
            .Select((route, index) => (route, index))
            .ToDictionary(page => page.route, page => page.index, StringComparer.Ordinal);

        var homes = folders.ToDictionary(
            component => component.Key,
            component => Home(component.Key, component.Value, order, rendered),
            StringComparer.Ordinal);

        var homeless = homes
            .Where(home => home.Value is null)
            .Select(home => $"{home.Key} ({folders[home.Key]})")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            homeless.Count == 0,
            "These components are drawn by no page that documents their library folder as its own subject, "
            + "so no chapter introduces them and nothing can be ordered after it. Either draw each on its "
            + "folder's chapter, or add the chapter to the folder's routes in this test: "
            + string.Join(", ", homeless));

        // Only pages the index lists have a position; a page it does not list is
        // the coverage test's finding, not this one's.
        var violations = order
            .Where(rendered.ContainsKey)
            .SelectMany(route => rendered[route]
                .Where(component => position[homes[component]!] > position[route])
                .Select(component => (Route: route, Component: component, Home: homes[component]!)))
            .ToList();

        var unrecorded = violations
            .Where(bend => !RecordedBends.Any(recorded => recorded.Route == bend.Route && recorded.Component == bend.Component))
            .Select(bend => $"/{bend.Route} draws {bend.Component}, introduced by /{bend.Home}")
            .ToList();

        Assert.True(
            unrecorded.Count == 0,
            "A storybook page draws a component before the page that introduces it. Nothing is shown before "
            + "its parts, so either move the page below the component's chapter in StorybookIndex, drop the "
            + "borrow, or — if the borrow is the point — record it as a bend in both the StorybookIndex "
            + "comment on that entry and RecordedBends in this test: "
            + string.Join("; ", unrecorded));

        var stale = RecordedBends
            .Where(recorded => !violations.Any(bend => bend.Route == recorded.Route && bend.Component == recorded.Component))
            .Select(recorded => $"/{recorded.Route} and {recorded.Component} — \"{recorded.Reason}\"")
            .ToList();

        Assert.True(
            stale.Count == 0,
            "RecordedBends lists a bend the index no longer makes, so the list is describing an exception "
            + "that does not exist. Remove it here and from the StorybookIndex comment: "
            + string.Join("; ", stale));
    }

    /// <summary>The first page in reading order that documents the component's
    /// folder and actually draws it, or null when no such page does.</summary>
    private static string? Home(
        string component,
        string folder,
        IReadOnlyList<string> order,
        IReadOnlyDictionary<string, HashSet<string>> rendered)
    {
        string[] prefixes = Chapters.TryGetValue(folder, out var routes) ? routes : [];

        return order.FirstOrDefault(route =>
            prefixes.Any(prefix => route.StartsWith(prefix, StringComparison.Ordinal))
            && rendered.TryGetValue(route, out var components)
            && components.Contains(component));
    }

    /// <summary>The routes in the order the index lists them. A page entry is a
    /// target-typed new("href", "title", ...); a group is new("Title", [ and has
    /// no second string, so the pattern skips it.</summary>
    private static IReadOnlyList<string> ReadingOrder() =>
        [.. Regex.Matches(File.ReadAllText(Index.FullName), @"new\(""([^""]*)"",\s*""")
            .Select(entry => entry.Groups[1].Value)];

    /// <summary>Each page's route, and the library components it instantiates —
    /// &lt;Card&gt; or &lt;Card ... /&gt; but not the word "Card" in prose. The
    /// same reading the coverage test uses, so a page the coverage test counts as
    /// drawing a component is a page this one orders by it.</summary>
    private static IReadOnlyDictionary<string, HashSet<string>> RenderedComponentsByRoute()
    {
        var components = ComponentFolders().Keys.ToList();

        return StorybookPages.EnumerateFiles("*.razor")
            .Select(page => File.ReadAllText(page.FullName))
            .Select(text => (Route: Regex.Match(text, @"@page\s+""/([^""]*)"""), Text: text))
            .Where(page => page.Route.Success)
            .ToDictionary(
                page => page.Route.Groups[1].Value,
                page => components
                    .Where(name => Regex.IsMatch(page.Text, $@"<{Regex.Escape(name)}(\s|/|>)"))
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    /// <summary>Every component the library offers, keyed by name, to the top-level
    /// folder it lives in — Diagrams/C4/C4Explorer is a Diagrams component. Minus
    /// _Imports, which is compiler plumbing rather than a component.</summary>
    private static IReadOnlyDictionary<string, string> ComponentFolders() =>
        Library.EnumerateFiles("*.razor", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => !file.Name.StartsWith('_'))
            .ToDictionary(
                file => Path.GetFileNameWithoutExtension(file.Name),
                file => Path.GetRelativePath(Library.FullName, file.FullName).Split(Path.DirectorySeparatorChar)[0],
                StringComparer.Ordinal);
}
