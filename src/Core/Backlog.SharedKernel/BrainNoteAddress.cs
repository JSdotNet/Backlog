using System.Diagnostics.CodeAnalysis;

namespace Backlog.SharedKernel;

/// <summary>
/// A note in its owner's Brain, as a plan or a task names it (local ADR 0053 §1):
/// <c>#brain:&lt;path&gt;</c>, optionally followed by <c>#&lt;slug&gt;</c>, the path
/// relative to the Brain folder.
/// <para>
/// The <c>#brain</c> in front is the Brain's name on the wire
/// (<c>.devbook/domain/brain/features.md#mcp-brain-scope</c>). No repository reference
/// can begin with it — a devbook path starts with a folder or a file name, never a
/// <c>#</c> — so a reader tells the two apart by the prefix alone, before it looks
/// anything up. Kept beside <see cref="BrainScope"/> so Roadmap, Tasks, Import and the
/// MCP tools share one spelling.
/// </para>
/// <para>
/// A reference is stored as it was written; this type reads it. The prefix is
/// forgiven its case, as the MCP scope is, and <see cref="Value"/> writes it in the
/// one spelling a reader compares on.
/// </para>
/// </summary>
public sealed record BrainNoteAddress
{
    /// <summary>The Brain's name on the wire, the scope an address belongs to.</summary>
    public const string Scope = "#brain";

    /// <summary>What every Brain address begins with.</summary>
    public const string Prefix = Scope + ":";

    private BrainNoteAddress(string path, string? slug)
    {
        Path = path;
        Slug = slug;
    }

    /// <summary>The note's path inside the Brain folder, with <c>/</c> separators and
    /// no leading <c>/</c>.</summary>
    public string Path { get; }

    /// <summary>The heading slug inside the note, or null for the whole note.</summary>
    public string? Slug { get; }

    /// <summary>The address in its one spelling: the lower-case prefix, the
    /// normalised path, then the slug when there is one.</summary>
    public string Value => Format(Path, Slug);

    /// <summary>The note as the Devbook pane opens one on the Brain side:
    /// <c>&lt;path&gt;[#slug]</c>, the prefix left off.</summary>
    public string Chapter => Slug is null ? Path : $"{Path}#{Slug}";

    /// <summary>
    /// Whether <paramref name="reference"/> claims the Brain — it wears the prefix,
    /// whatever follows. A reader routes such a reference to the Brain side even when
    /// it names no note there, so a malformed one is shown unresolved rather than
    /// looked up in a repository's devbook.
    /// </summary>
    public static bool Is(string? reference) =>
        reference is not null && reference.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads an address. Refuses a reference without the prefix, an empty path, and a
    /// path with a <c>..</c> segment, which would name something outside the Brain.
    /// </summary>
    public static bool TryParse(string? reference, [NotNullWhen(true)] out BrainNoteAddress? address)
    {
        address = null;
        if (!Is(reference)) return false;

        var rest = reference!.Trim()[Prefix.Length..];
        var hash = rest.IndexOf('#', StringComparison.Ordinal);
        var path = (hash < 0 ? rest : rest[..hash]).Trim().Replace('\\', '/');
        var slug = hash < 0 ? null : rest[(hash + 1)..].Trim();

        path = path.TrimStart('/');
        if (path.Length == 0) return false;
        if (path.Split('/').Any(segment => segment == "..")) return false;

        address = new BrainNoteAddress(path, string.IsNullOrEmpty(slug) ? null : slug);
        return true;
    }

    /// <summary>The address of <paramref name="path"/>, and of the heading
    /// <paramref name="slug"/> inside it when given, in the one spelling.</summary>
    public static string Format(string path, string? slug = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return string.IsNullOrEmpty(slug) ? $"{Prefix}{path}" : $"{Prefix}{path}#{slug}";
    }

    public override string ToString() => Value;
}
