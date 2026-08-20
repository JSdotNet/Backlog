namespace Backlog.UI.Components.Layout;

/// <summary>
/// A version of a file worth comparing the one on screen against.
/// <para>
/// A record and not a callback, because reading a version is the host's: one
/// baseline is a string it has been holding since the file was opened, another is
/// a blob it had to ask git for, and a third might be a draft it never wrote down.
/// A file view that knew how to fetch any of those would have to know about all of
/// them.
/// </para>
/// <para>
/// Which is also why <see cref="Unavailable"/> exists. "There is no committed
/// version of this file yet" is a perfectly ordinary answer — a new chapter has
/// never been committed — and it is not the same answer as an empty file. A
/// baseline that can say so gets a sentence on the screen instead of a diff
/// claiming every line is new.
/// </para>
/// </summary>
/// <param name="Id">Stable within the list, and what a selection is remembered
/// by. Not shown.</param>
/// <param name="Label">What the reader calls this version — "As opened", "Last
/// commit". It names the left-hand side of the comparison, so it is a version and
/// not an instruction.</param>
/// <param name="Text">The file as that version has it. Null with a reason in
/// <see cref="Unavailable"/> is a settled "there is no such version"; null with
/// no reason is the host not having answered yet, which is the ordinary state of
/// a baseline that costs a git process and is only read once a reader asks for
/// it. The file view keeps offering the comparison through the second, because
/// the press is what produces the answer.</param>
/// <param name="Unavailable">Why there is nothing to compare against, in the
/// reader's terms. Shown in place of the comparison; null when there is one, and
/// null too while the answer is still being waited for — see
/// <paramref name="Text"/>.</param>
public sealed record FileCompareBaseline(
    string Id,
    string Label,
    string? Text = null,
    string? Unavailable = null);
