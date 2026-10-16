namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// The per-part "Ask AI" handler, cascaded by the pane to every
/// <see cref="PartFrame"/> under it.
/// </summary>
/// <remarks>
/// <para>
/// Cascaded rather than a parameter because some twenty parts wear the frame and none
/// of them has anything to say about asking: a parameter would be threaded through
/// each of them to reach the one component that draws the control. The pane cascades
/// one only while the shell has said it can answer — the assistant switched on and a
/// Dashboard source composed — so a frame with none draws no control rather than a
/// button that does nothing.
/// </para>
/// <para>
/// It carries the part's title and nothing else. What the assistant is sent is the
/// Dashboard's content, every tab's figures, whichever chart was pressed; the title
/// goes into the reader's question, which is where something about the screen
/// belongs.
/// </para>
/// </remarks>
public sealed class DashboardAskAi(Func<string, Task> ask)
{
    /// <summary>Opens the Ask AI panel on the Dashboard with a question about the
    /// part titled <paramref name="title"/>.</summary>
    public Task AskAsync(string title) => ask(title);
}
