using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The library's <c>components.js</c>, for tests that pin a shape of the script no
/// render can reach: bUnit has no JS engine, so what the browser does with a resize
/// burst, a failed import or a reused SVG element is only visible in the source.
/// </summary>
internal static class ComponentsScript
{
    private static readonly Lazy<string> Text = new(() =>
        File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal));

    /// <summary>The whole file, comments included.</summary>
    public static string Source => Text.Value;

    /// <summary>
    /// The code between two anchors, without its comments. Several assertions are
    /// negative, and the comments explaining why a call is gone have to name it;
    /// reading them as code would fail each test on its own explanation.
    /// </summary>
    public static string Region(string from, string to)
    {
        var script = Text.Value;

        var start = script.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{from}' is no longer in components.js.");

        var end = script.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{to}' no longer follows '{from}' in components.js.");

        return WithoutComments(script[start..end]);
    }

    private static string WithoutComments(string script) =>
        Regex.Replace(Regex.Replace(script, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"(?m)^\s*//.*$", string.Empty);
}
