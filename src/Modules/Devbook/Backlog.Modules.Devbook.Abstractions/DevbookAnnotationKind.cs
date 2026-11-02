namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// What a remark is: devbook's closed set of note kinds, the same four an
/// <c>annotation</c> fence names, <see cref="Comment"/> first because it is the
/// default. A value outside the set is never stored; it reads as a comment, which
/// is what the fence parser does with an unknown kind too.
/// </summary>
public enum DevbookAnnotationKind
{
    /// <summary>A plain remark. The default, and what an absent or unknown kind
    /// reads as.</summary>
    Comment,

    /// <summary>A question that wants an answer before the passage is settled.</summary>
    Question,

    /// <summary>A proposed change to the passage.</summary>
    Suggestion,

    /// <summary>Something wrong or risky the passage should not keep.</summary>
    Flag
}

/// <summary>
/// The wire and file names of <see cref="DevbookAnnotationKind"/>: lower case, as
/// the devbook fence spells them, so the remark file, the replica payload and the
/// fence all say <c>question</c> rather than three spellings of it.
/// </summary>
public static class DevbookAnnotationKinds
{
    /// <summary>The closed set as names, default first.</summary>
    public static IReadOnlyList<string> Names { get; } = ["comment", "question", "suggestion", "flag"];

    /// <summary>The name a kind is written as.</summary>
    public static string Name(DevbookAnnotationKind kind) => kind switch
    {
        DevbookAnnotationKind.Question => "question",
        DevbookAnnotationKind.Suggestion => "suggestion",
        DevbookAnnotationKind.Flag => "flag",
        _ => "comment"
    };

    /// <summary>Whether <paramref name="name"/> is one of the four, case aside.</summary>
    public static bool TryParse(string? name, out DevbookAnnotationKind kind)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "comment":
                kind = DevbookAnnotationKind.Comment;
                return true;
            case "question":
                kind = DevbookAnnotationKind.Question;
                return true;
            case "suggestion":
                kind = DevbookAnnotationKind.Suggestion;
                return true;
            case "flag":
                kind = DevbookAnnotationKind.Flag;
                return true;
            default:
                kind = DevbookAnnotationKind.Comment;
                return false;
        }
    }

    /// <summary>The kind a name reads as: its own when it is one of the four, and
    /// <see cref="DevbookAnnotationKind.Comment"/> when it is absent or unknown —
    /// a file or a payload written before kinds existed.</summary>
    public static DevbookAnnotationKind Parse(string? name) =>
        TryParse(name, out var kind) ? kind : DevbookAnnotationKind.Comment;
}
