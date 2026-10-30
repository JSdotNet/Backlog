namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Domain view over this repository's own <c>.devbook/domain/</c> (local ADR
/// 0044): every requirement a context writes is on its Requirements page, and every
/// invariant about an aggregate or a shared grouping on its Always true tab. A requirement or invariant whose shape the
/// view cannot read would otherwise drop off its page without a sign.
/// </summary>
public sealed class DomainRepositoryChaptersTests
{
    public static TheoryData<string> Contexts()
    {
        var data = new TheoryData<string>();
        foreach (var directory in DomainFolder().EnumerateDirectories().Where(d => File.Exists(Path.Combine(d.FullName, "context.md"))).OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            data.Add(directory.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_requirement_is_on_the_requirements_page(string context)
    {
        var files = Files(context);
        foreach (var page in files.Where(file => System.IO.Path.GetFileName(file.Path).StartsWith("requirements", StringComparison.Ordinal)))
        {
            var written = Count(page.Chapters, DomainUnits.IsRequirement);
            var shown = DomainRequirements.Groups(page, files).Sum(group => group.Requirements.Count);
            Assert.True(written == shown, $"{page.Path}: {written} requirements written, {shown} shown.");
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_invariant_is_on_always_true(string context)
    {
        var files = Files(context);
        var invariantFiles = files.Where(file => file.Path.EndsWith(".invariants.md", StringComparison.Ordinal)).ToList();
        if (invariantFiles.Count == 0) return;

        var written = invariantFiles
            .SelectMany(file => Titles(file.Chapters, string.Empty))
            .ToList();
        // An aggregate's or a shared grouping's rules are its Always true tab. A
        // group about a domain service has no tab: the service's page lists what is
        // always true of the units its related names, not a group of its own.
        var catalog = DomainRuleCatalog.Of(files);
        var shown = catalog.Units.SelectMany(unit => unit.Rules).Select(rule => rule.Title).ToHashSet(StringComparer.Ordinal);
        var services = files
            .Where(file => DomainFeatures.KindOf(file.Path) == "domain" && !file.Path.EndsWith(".invariants.md", StringComparison.Ordinal))
            .SelectMany(file => DomainUnits.Of(file, invariantFiles))
            .Where(unit => unit.Kind == DomainUnitKind.DomainService)
            .Select(unit => unit.Chapter.Title)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        written = [.. written.Where(w => !services.Contains(w.Group))];
        var missing = written.Where(w => !shown.Contains(w.Title)).Select(w => $"{w.Group} > {w.Title}").ToList();
        Assert.True(missing.Count == 0, $"{context}: not on Always true: {string.Join(" | ", missing)}");
    }

    private static List<DomainChapterFile> Files(string context)
    {
        var folder = Path.Combine(DomainFolder().FullName, context);
        return
        [
            .. Directory.EnumerateFiles(folder, "*.md")
                .Where(file => !System.IO.Path.GetFileName(file).StartsWith('_'))
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(file => DomainUnits.Parse(File.ReadAllText(file), $".devbook/domain/{context}/{System.IO.Path.GetFileName(file)}")),
        ];
    }

    private static IEnumerable<(string Group, string Title)> Titles(IEnumerable<DomainChapter> chapters, string group)
    {
        foreach (var chapter in chapters)
        {
            if (chapter.Title.StartsWith("Invariant:", StringComparison.Ordinal))
            {
                yield return (group, chapter.Title["Invariant:".Length..].Trim());
                continue;
            }
            foreach (var child in Titles(chapter.Children, chapter.Title)) yield return child;
        }
    }

    private static int Count(IEnumerable<DomainChapter> chapters, Func<DomainChapter, bool> match) =>
        chapters.Sum(chapter => (match(chapter) ? 1 : 0) + Count(chapter.Children, match));

    private static DirectoryInfo DomainFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Backlog.sln"))) directory = directory.Parent;
        return new DirectoryInfo(Path.Combine(directory?.FullName ?? throw new InvalidOperationException("No Backlog.sln above the test output."), ".devbook", "domain"));
    }
}
