namespace Backlog.UI.Components.Tasks;

/// <summary>
/// A row's body was typed into. The id and the markdown, and nothing else:
/// whether the change is saved, and where, is the host's — the same bargain
/// <see cref="TaskRename"/> makes about a title and <see cref="TaskMove"/> makes
/// about order.
/// </summary>
/// <param name="Id">The row whose body was edited.</param>
/// <param name="Body">The markdown as it now stands, verbatim, after one
/// keystroke.
/// <para>
/// Untrimmed, unlike <see cref="TaskRename.Title"/>. A title is one line and its
/// surrounding whitespace is slack; a body is prose, and its blank line before a
/// list or its trailing newline are things the author typed on purpose. Trimming
/// them would mean the text the host stores is not the text that was in the
/// editor.
/// </para>
/// <para>
/// Empty is a value rather than a reason not to report. A reader who selected the
/// whole body and deleted it has said something, and swallowing it would leave
/// the host holding a body the reader can see is gone.
/// </para></param>
public sealed record TaskBodyChange(string Id, string Body);
