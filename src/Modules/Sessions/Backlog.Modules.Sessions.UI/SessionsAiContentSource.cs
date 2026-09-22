using System.Globalization;
using System.Text;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel.Ai;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// The Sessions context's answer to <see cref="IAiContentSource"/>: the catalog
/// of agent sessions, one line each, and never a word of what was said in them.
/// </summary>
/// <remarks>
/// <para>
/// The catalog is what <see cref="IAgentSessionSource"/> answers with — the
/// name, the branch, the folder, when it ran and for how long, on which
/// machine. That is what the list on screen shows, and it is what a question
/// about "what was I working on yesterday" needs.
/// </para>
/// <para>
/// Transcripts are deliberately not here, and not because they would not fit.
/// A transcript holds whatever the reader pasted into an agent — a key, a
/// customer's name, a file they were not supposed to have open — and this
/// source runs on every press of Ask, with the body leaving the machine. The
/// port this reads does not even answer with transcript text; the reader that
/// parses transcripts is a different port, <c>IAgentActivitySource</c>, and
/// nothing here holds it. A test pins that.
/// </para>
/// <para>
/// Most recent activity first before relevance ranks them, so the sessions the
/// question says nothing about fall to the recent, which is the order the
/// list shows them in.
/// </para>
/// </remarks>
internal sealed class SessionsAiContentSource(IAgentSessionSource sessions) : IAiContentSource
{
    public string AreaKey => "sessions";

    public string AreaTitle => "Sessions";

    public async Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var catalog = await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> records =
        [
            .. catalog.Sessions
                .OrderByDescending(session => session.LastActivityAt)
                .Select(Text)
        ];

        // The total is what the readers discovered, not what the catalog holds:
        // the catalog stops at a hundred per agent, and "200 of 842 entries" is
        // the truth a reader needs when the session they mean is not in it.
        // Files the readers could not open are said too, for the same reason.
        var unreadable = catalog.Unreadable.Count;
        var note = unreadable switch
        {
            0 => null,
            1 => "1 session file could not be read.",
            _ => $"{unreadable} session files could not be read."
        };

        return AiContentBudget.Compose(
            AreaKey,
            AreaTitle,
            records,
            record => record,
            request.Question,
            request.BudgetCharacters,
            total: catalog.Discovered,
            note: note);
    }

    /// <summary>"Session: title — agent, state, repository, branch, folder,
    /// started, last active, duration, turns, environment."</summary>
    internal static string Text(AgentSession session)
    {
        var text = new StringBuilder("Session: ").Append(string.IsNullOrWhiteSpace(session.Title) ? "(untitled)" : session.Title.Trim());
        text.Append(" — ").Append(session.Kind).Append(", ").Append(session.State.ToString().ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(session.Repository)) text.Append(", repository ").Append(session.Repository.Trim());
        if (!string.IsNullOrWhiteSpace(session.Branch)) text.Append(", branch ").Append(session.Branch.Trim());
        if (!string.IsNullOrWhiteSpace(session.WorkingFolder)) text.Append(", folder ").Append(session.WorkingFolder.Trim());

        if (session.StartedAt is { } started)
        {
            text.Append(", started ").Append(Stamp(started));

            // A duration only when there are two ends to measure between: a
            // session still running has no end, and a length written for it
            // would be a length as of some moment nobody can see.
            if (session.State != AgentSessionState.Running)
            {
                text.Append(", lasted ").Append(Duration(session.LastActivityAt - started));
            }
        }

        text.Append(", last active ").Append(Stamp(session.LastActivityAt));
        if (session.TurnCount is { } turns) text.Append(", ").Append(turns.ToString(CultureInfo.InvariantCulture)).Append(turns == 1 ? " turn" : " turns");
        if (!string.IsNullOrWhiteSpace(session.Environment)) text.Append(", on ").Append(session.Environment.Trim());

        return text.ToString();
    }

    private static string Stamp(DateTimeOffset at) => at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{Math.Max(1, (int)span.TotalMinutes)}m";
    }
}
