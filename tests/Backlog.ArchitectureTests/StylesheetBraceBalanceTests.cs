namespace Backlog.ArchitectureTests;

/// <summary>
/// Every block a stylesheet opens, it closes.
///
/// <para>A merge of the C4 zoom plan (6ddc854f5) dropped two closing braces from
/// <c>components.css</c> and one from the desktop <c>app.css</c>. A browser does
/// not reject the file: with native CSS nesting every rule after the open block
/// nests under it, so the Notifications cards, the cohesion view and the
/// popped-out window lost their styling with no error anywhere. The duplicate
/// selector check cannot see it either — the nested rules are no longer top
/// level. Counting braces can.</para>
/// </summary>
public class StylesheetBraceBalanceTests
{
    public static TheoryData<string> Stylesheets => [.. Found()];

    [Fact]
    public void The_stylesheets_under_src_are_found()
    {
        var found = Found();

        Assert.Contains("src/Core/Backlog.UI.Components/wwwroot/components.css", found);
        Assert.Contains("src/App/Backlog.Desktop.UI/wwwroot/app.css", found);
    }

    [Theory]
    [MemberData(nameof(Stylesheets))]
    public void Every_block_the_stylesheet_opens_is_closed(string stylesheet)
    {
        var (unclosed, stray) = CssRules.Unbalanced(
            File.ReadAllText(Path.Combine(Repository.Root.FullName, stylesheet)));

        var problems = unclosed
            .Select(block => $"`{block.Prelude} {{` on line {block.Line} is never closed")
            .Concat(stray.Select(line => $"the `}}` on line {line} closes nothing"))
            .ToList();

        Assert.True(
            problems.Count == 0,
            $"{stylesheet}: {string.Join("; ", problems)}. With native CSS nesting every rule after an open "
            + "block nests under it and stops applying; a merge that kept both sides of a conflict is the usual cause.");
    }

    [Fact]
    public void An_open_rule_is_reported_where_it_opens()
    {
        var (unclosed, stray) = CssRules.Unbalanced(
            ".a {\n    color: red;\n}\n\n.b {\n    margin: 0;\n\n.c {\n    padding: 0;\n}\n");

        var block = Assert.Single(unclosed);
        Assert.Equal((5, ".b"), block);
        Assert.Empty(stray);
    }

    [Fact]
    public void A_brace_that_closes_nothing_is_reported_on_its_line()
    {
        var (unclosed, stray) = CssRules.Unbalanced(".a {\n    color: red;\n}\n}\n");

        Assert.Empty(unclosed);
        Assert.Equal([4], stray);
    }

    [Fact]
    public void Braces_in_comments_and_strings_do_not_count()
    {
        var (unclosed, stray) = CssRules.Unbalanced(
            "/* .x { */\n.a { content: \"{\"; }\n.b { content: '}'; }\n.d { content: \"\\\"{\"; }\n/* } */\n@media print { .c { color: black; } }\n");

        Assert.Empty(unclosed);
        Assert.Empty(stray);
    }

    /// <summary>Every stylesheet under <c>src</c>, build output left out, as a
    /// repository-relative path with forward slashes.</summary>
    private static List<string> Found()
    {
        var source = Path.Combine(Repository.Root.FullName, "src");
        var separator = Path.DirectorySeparatorChar;

        return [.. Directory.EnumerateFiles(source, "*.css", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}node_modules{separator}", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(Repository.Root.FullName, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }
}
