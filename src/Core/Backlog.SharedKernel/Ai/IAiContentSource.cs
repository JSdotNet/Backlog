namespace Backlog.SharedKernel.Ai;

/// <summary>
/// PORT — one area of the product, describing its own content for an AI question.
/// </summary>
/// <remarks>
/// <para>
/// Ask AI used to read the task rows the shell happened to have on screen —
/// filtered, sorted, capped at a byte count — and send them as the content.
/// That was screen state, not content: the same backlog answered differently
/// depending on which chip was pressed, and a long list blew straight through
/// the deployment's quota. This port turns the question round. Each area
/// declares what its content <em>is</em>, the shell asks the one the reader is
/// looking at, and the shell never learns what a task, an inbox item or a
/// chapter looks like.
/// </para>
/// <para>
/// A source never describes the screen — no filter text, no sort mode, no pane
/// layout, and no selection beyond pinning the record the reader has open,
/// because that record is the likeliest subject of the question. But the scope
/// that defines which records an area has at all — a dashboard's repositories
/// and period, a task list's repository scope — is content, not screen: it says
/// what the area is about, and the body's first line states it.
/// </para>
/// <para>
/// In the shared kernel rather than in a module, because every module UI answers
/// it and the shell collects the answers; it is the contract surface between the
/// two, and nothing in it names a module.
/// </para>
/// </remarks>
public interface IAiContentSource
{
    /// <summary>The stable slug the shell keys the area by: <c>tasks</c>,
    /// <c>inbox</c>, <c>devbook</c>, <c>roadmap</c>, <c>dashboard</c>,
    /// <c>sessions</c> or <c>tools</c>. Lower case, and never shown.</summary>
    string AreaKey { get; }

    /// <summary>What the area is called on the chip: <c>Tasks</c>,
    /// <c>Inbox</c>, <c>Devbook</c>, <c>Roadmap</c>, <c>Dashboard</c>,
    /// <c>Sessions</c> or <c>Tools</c>.</summary>
    string AreaTitle { get; }

    /// <summary>
    /// The area's content for the question, fitted to the budget.
    /// <para>
    /// Every source fits its records the one way <see cref="AiContentBudget"/>
    /// defines, so the sources cannot drift on what "fits" means. The records
    /// are the area's within its own scope — the repositories and window a
    /// dashboard is showing, the repositories a task list is narrowed to — and
    /// never a slice of that by filter, sort or layout. An area with nothing to
    /// say answers with an empty body rather than throwing; a source that cannot
    /// read its records at all says so in the body, because the reader is about
    /// to send a question and deserves to know it will be answered from nothing.
    /// A source that cannot even do that — a store that throws — lets the throw
    /// out, and the shell reports it beside the question rather than letting it
    /// take the page.
    /// </para>
    /// </summary>
    Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default);
}
