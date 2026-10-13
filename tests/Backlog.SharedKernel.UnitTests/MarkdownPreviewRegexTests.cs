using System.Text.RegularExpressions;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// <see cref="Markdown.MarkdownPreview"/> runs on every render of every read view,
/// so its patterns are source-generated at build time rather than parsed and
/// compiled to IL the first time the type is touched. What it parses is pinned by
/// the Markdown suites; this pins only that a runtime-built regex does not creep
/// back in.
/// </summary>
public partial class MarkdownPreviewRegexTests
{
    private static readonly Lazy<string> Source = new(() =>
        File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.SharedKernel", "Markdown", "MarkdownPreview.cs")));

    [Fact]
    public void ThePreview_BuildsNoRegexAtRuntime()
    {
        Assert.DoesNotContain("new Regex(", Source.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("RegexOptions.Compiled", Source.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly Regex", Source.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The static helpers take the pattern as a string and parse it at run time,
    /// through the regex cache, so one of them is the same regression as a field.
    /// Only the static receiver is matched: a generated method called as
    /// <c>HeadingRegex().Match(</c> is what the file is meant to contain.
    /// </summary>
    [Fact]
    public void ThePreview_CallsNoStaticRegexHelper()
    {
        var calls = StaticRegexHelper().Matches(Source.Value).Select(match => match.Value);

        Assert.Empty(calls);
    }

    [Fact]
    public void ThePatterns_AreSourceGenerated()
    {
        // At least one rather than an exact count: adding a pattern the right way,
        // or removing one nobody calls, is not a regression.
        var generated = Source.Value.Split("[GeneratedRegex(").Length - 1;

        Assert.True(generated > 0, "MarkdownPreview declares no [GeneratedRegex] pattern.");
    }

    [GeneratedRegex(@"\bRegex\.(?:IsMatch|Match|Matches|Replace|Split|Count|EnumerateMatches|EnumerateSplits)\(")]
    private static partial Regex StaticRegexHelper();
}
