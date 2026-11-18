using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Capture.Feeds;

/// <summary>
/// A tag's attributes read as a bag — the one way this project reads a tag a
/// page wrote, shared by the website adapter's feed discovery and the event
/// page adapter.
/// <para>
/// A bag rather than a pattern for the whole tag, because attribute order is
/// whatever the template wrote and quoting is whatever the author liked. The
/// values come back as written: a caller that needs the text an entity stands
/// for decodes it, since a <c>href</c> and an <c>alt</c> want different things
/// done with an <c>&amp;amp;</c>.
/// </para>
/// </summary>
internal static partial class HtmlAttributes
{
    /// <summary>First occurrence wins, and a repeated attribute — untrusted
    /// markup does that — is not an exception.</summary>
    public static Dictionary<string, string> Of(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match attribute in Attribute().Matches(tag))
        {
            attributes.TryAdd(attribute.Groups["name"].Value, attribute.Groups["value"].Value);
        }

        return attributes;
    }

    /// <summary>One attribute, quoted either way or not at all. The unquoted
    /// form stops at whitespace or the tag's end.</summary>
    [GeneratedRegex(@"(?<name>[\w:-]+)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>/]+))")]
    private static partial Regex Attribute();
}
