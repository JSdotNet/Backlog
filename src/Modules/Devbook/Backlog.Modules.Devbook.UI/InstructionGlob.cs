using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Matches a repository-relative path against the globs an instruction file
/// scopes itself by — GitHub Copilot's <c>applyTo</c> and Claude Code's
/// <c>paths</c>, which are written in the same shape.
///
/// <para>Written here rather than taken from a package. No globbing library is
/// referenced by this repository, and the vocabulary actually in use is small:
/// <c>**</c>, <c>*</c>, <c>?</c>, and a comma-separated list written as one
/// scalar. That is the same call the shared frontmatter reader made — three
/// shapes of YAML, read here, rather than a YAML parser.</para>
///
/// <para>Case-insensitive and forward-slashed, because the hosts treat these
/// paths that way and the file system underneath does not agree with itself
/// across platforms.</para>
/// </summary>
public static class InstructionGlob
{
    /// <summary>Compiled patterns are kept, because the comparison asks the same
    /// handful of globs about every path a reader picks.</summary>
    private static readonly Dictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    private static readonly Lock Gate = new();

    /// <summary>Whether any of a file's globs claims this path.</summary>
    public static bool Matches(IEnumerable<string> globs, string path)
    {
        ArgumentNullException.ThrowIfNull(globs);

        return !string.IsNullOrWhiteSpace(path) && globs.Any(glob => Matches(glob, path));
    }

    /// <summary>Whether one glob claims this path.</summary>
    public static bool Matches(string? glob, string? path)
    {
        if (string.IsNullOrWhiteSpace(glob) || string.IsNullOrWhiteSpace(path)) return false;

        var candidate = Normalize(path);

        return Pattern(glob.Trim()).IsMatch(candidate);
    }

    private static Regex Pattern(string glob)
    {
        lock (Gate)
        {
            if (Patterns.TryGetValue(glob, out var cached)) return cached;

            var pattern = new Regex(
                Translate(glob),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

            Patterns[glob] = pattern;

            return pattern;
        }
    }

    /// <summary>
    /// The glob as an anchored regular expression.
    /// <para><c>**/</c> is translated as a unit so <c>src/**/*.cs</c> matches
    /// <c>src/a.cs</c> as well as <c>src/deep/a.cs</c> — the separator is part of
    /// what the double star swallows, and translating the two halves separately
    /// leaves a mandatory slash that excludes the shallow case.</para>
    /// <para>A single star stops at a separator and a double star does not. That
    /// difference is the whole reason these two spellings exist.</para>
    /// </summary>
    private static string Translate(string glob)
    {
        var builder = new StringBuilder("^");

        for (var index = 0; index < glob.Length; index++)
        {
            var character = glob[index];

            if (character == '*')
            {
                if (index + 1 < glob.Length && glob[index + 1] == '*')
                {
                    index++;

                    if (index + 1 < glob.Length && glob[index + 1] == '/')
                    {
                        index++;
                        builder.Append("(?:.*/)?");
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }

                continue;
            }

            builder.Append(character == '?' ? "[^/]" : Regex.Escape(character.ToString()));
        }

        return builder.Append('$').ToString();
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();

        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }
}
