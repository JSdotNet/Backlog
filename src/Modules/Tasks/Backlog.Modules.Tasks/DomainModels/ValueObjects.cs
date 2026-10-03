using System.Diagnostics.CodeAnalysis;

namespace Backlog.Modules.Tasks.DomainModels;

/// <summary>
/// Immutable link to a downstream external artifact created from an entry.
/// Equality is by value.
/// </summary>
public sealed record ProjectionRef(string RepoId, string ExternalId, string TargetType);

/// <summary>
/// Immutable audit record of a prompt copy/use. Equality is by value.
/// </summary>
public sealed record UsageEvent(DateTimeOffset Timestamp, string Action);

/// <summary>
/// A task's pointer at a Devbook page — <c>&lt;path&gt;</c> — or at one chapter
/// of it — <c>&lt;path&gt;#&lt;anchor&gt;</c>. Equality is by value, after
/// normalisation, so two spellings of one chapter are one reference.
/// <para>
/// The path is repository-relative and written the way a link in the repository
/// writes it: backslashes become <c>/</c>, a leading <c>./</c> or <c>/</c> is
/// dropped, and surrounding <c>[...]</c> — how a reference is quoted in prose and
/// in a pasted plan — is taken off. What is refused is a value that names no page
/// at all: a path part with neither a folder nor a <c>.md</c> ending is a word,
/// not a page.
/// </para>
/// <para>
/// A path whose first folder is a devbook area — <c>arc42</c>, <c>domain</c>,
/// <c>tech</c>, <c>design</c> or <c>ai</c> — is taken to be under the devbook
/// root: <c>domain/tasks/features.md</c> is stored as
/// <c>.devbook/domain/tasks/features.md</c>. The task panel names a page by its
/// path without that root, so it is the form a reader types back, and a plan or an
/// MCP client writes it too; stored bare it read as a reference outside the
/// devbook. The rule lives here because every writer comes through here. It cannot
/// ask which layout the repository is on, so it writes the current one; a host
/// that knows the repository's pages — the task panel does — may spell a reference
/// in the legacy <c>.domain/</c> form first, and a path that starts with a dot is
/// left as it is.
/// </para>
/// <para>
/// Whether the page exists is deliberately not asked. A reference outlives a
/// rename of the chapter it names, and showing it as broken is the reader's
/// answer to that; refusing the write would lose the link that says what the
/// task was about.
/// </para>
/// </summary>
public sealed record TaskDevbookReference
{
    private TaskDevbookReference(string path, string? anchor)
    {
        Path = path;
        Anchor = anchor;
    }

    /// <summary>The page, repository-relative, with <c>/</c> separators.</summary>
    public string Path { get; }

    /// <summary>The chapter's heading slug, or null when the reference names the
    /// whole page.</summary>
    public string? Anchor { get; }

    /// <summary>The normalised spelling, the one the task stores.</summary>
    public string Value => Anchor is null ? Path : $"{Path}#{Anchor}";

    /// <summary>Reads a written reference, or throws <see cref="ArgumentException"/>
    /// when it names no page.</summary>
    public static TaskDevbookReference Parse(string? text) =>
        TryParse(text, out var reference)
            ? reference
            : throw new ArgumentException(
                $"'{text}' is not a Devbook reference: it needs a path with a folder or a .md ending, optionally followed by #anchor.",
                nameof(text));

    /// <summary>Reads a written reference, answering false when it names no
    /// page.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out TaskDevbookReference? reference)
    {
        reference = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']') value = value[1..^1].Trim();

        var hash = value.IndexOf('#', StringComparison.Ordinal);
        var path = (hash < 0 ? value : value[..hash]).Trim().Replace('\\', '/');
        var anchor = hash < 0 ? null : value[(hash + 1)..].Trim();

        while (path.StartsWith("./", StringComparison.Ordinal) || path.StartsWith('/'))
        {
            path = path.StartsWith('/') ? path[1..] : path[2..];
        }

        if (path.Length == 0) return false;
        if (!path.Contains('/', StringComparison.Ordinal) && !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;

        reference = new TaskDevbookReference(UnderDevbookRoot(path), string.IsNullOrEmpty(anchor) ? null : anchor);
        return true;
    }

    /// <summary>The devbook's own root, the one every adopted area sits under.</summary>
    public const string DevbookRoot = ".devbook/";

    /// <summary>The devbook areas a path may name as its first folder without the
    /// root in front of it.</summary>
    private static readonly HashSet<string> Areas = new(StringComparer.OrdinalIgnoreCase) { "arc42", "domain", "tech", "design", "ai" };

    private static string UnderDevbookRoot(string path)
    {
        var slash = path.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && Areas.Contains(path[..slash]) ? DevbookRoot + path : path;
    }

    public override string ToString() => Value;
}
