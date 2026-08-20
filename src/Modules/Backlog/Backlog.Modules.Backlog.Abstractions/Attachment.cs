namespace Backlog.Modules.Backlog.Abstractions;

/// <summary>
/// What is attached to a task: one place on disk, as a path.
/// <para>
/// One place and never a list, which is the decision this type exists to hold.
/// The pane an attachment is read in is a side panel, and the height of that
/// panel is the one measurement in the layout that everything else has to live
/// with — every row it grows is a row the body loses. A collection here would
/// make that height a function of how many files somebody dropped on the task, so
/// what is attached is the folder or the archive, and how much is in it is a fact
/// about that place rather than a list of members.
/// </para>
/// <para>
/// A path and not a copy. Backlog is local-first and its tasks are markdown files
/// that get shared and committed; copying a folder into a store would make the
/// task's text stop being the whole of the task, which is the one property the
/// whole grammar rests on. So this points, and pointing is all it does — whether
/// the path still resolves is the file system's answer and not this record's.
/// </para>
/// <para>
/// In Abstractions rather than beside the aggregate for the reason
/// <see cref="Recurrence"/> is: the parser produces one, the DTO publishes one and
/// the aggregate holds one, and a value object all three have to name is part of
/// the published language.
/// </para>
/// </summary>
public sealed record Attachment(string Path)
{
    /// <summary>The extensions that make a path an archive rather than a folder.
    /// Deliberately short: these are the two a person means by "zip it up", and a
    /// longer list would be this type claiming to know about formats when all it
    /// does is choose a word for a row.</summary>
    private static readonly string[] ArchiveExtensions = [".zip", ".tar.gz"];

    /// <summary>
    /// Whether the path names an archive, read off the path and nothing else.
    /// <para>
    /// By spelling rather than by asking the disk, because this record is
    /// comparable and serializable and a value that changed when a file was
    /// renamed underneath it would be neither. A folder called
    /// <c>backup.zip</c> is an archive to this type and that is the right answer
    /// for the only thing the answer is used for: which word the row shows.
    /// </para>
    /// </summary>
    public bool IsArchive =>
        ArchiveExtensions.Any(extension => Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The last segment of the path — what a reader would call the thing.
    /// <para>
    /// Both separators, because a path written on Windows is read on the phone and
    /// committed to a repository that has seen both. Splitting on one of them
    /// would leave the other's whole path as the "name".
    /// </para>
    /// </summary>
    public string Name
    {
        get
        {
            var trimmed = Path.TrimEnd('/', '\\');
            var cut = trimmed.LastIndexOfAny(['/', '\\']);

            // No separator at all is a bare name, which is already the answer.
            return cut < 0 ? trimmed : trimmed[(cut + 1)..];
        }
    }

    /// <summary>
    /// The path, if there is one worth keeping, and null otherwise.
    /// <para>
    /// Absent means absent everywhere in this grammar: an unset field carries no
    /// token rather than an empty one, so a blank path is not an attachment to
    /// nowhere — it is no attachment. One place decides that, here, so the parser,
    /// the aggregate and the pane cannot disagree about what an empty string was
    /// supposed to mean.
    /// </para>
    /// </summary>
    public static Attachment? From(string? path)
    {
        var trimmed = path?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : new Attachment(trimmed);
    }
}
