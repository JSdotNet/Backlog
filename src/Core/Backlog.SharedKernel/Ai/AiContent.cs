namespace Backlog.SharedKernel.Ai;

/// <summary>
/// One area's content for one question: the body the prompt carries, and how it
/// was chosen.
/// </summary>
/// <remarks>
/// <para>
/// The counts are here so the shell can say, and a test can prove, that a body
/// was cut down rather than merely short. A body alone cannot tell "the area
/// holds three entries" from "the area holds three hundred and these are the
/// three that fit", and the second is the case the reader deserves to know about.
/// </para>
/// </remarks>
/// <param name="AreaKey">The source's <see cref="IAiContentSource.AreaKey"/>,
/// repeated here so a body can be traced back to who wrote it.</param>
/// <param name="Body">What goes into the prompt as the content. Empty when the
/// area has nothing to say, which is a valid answer rather than an error.</param>
/// <param name="Shown">How many records the body carries.</param>
/// <param name="Total">How many records the area had to choose from.</param>
/// <param name="Trimmed">Whether <see cref="Shown"/> is less than
/// <see cref="Total"/> because of the budget.</param>
public sealed record AiContent(string AreaKey, string Body, int Shown, int Total, bool Trimmed);
