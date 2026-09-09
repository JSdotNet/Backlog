namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// Which file is a Claude session's transcript, once a session filed under two
/// project folders has been collapsed back into one.
/// <para>
/// Extracted from <see cref="ClaudeSessionReader"/> rather than copied out of it,
/// because the activity reader needs the identical rule and two copies of it is
/// exactly the drift the comment below exists to prevent. It is already load-bearing:
/// it was found on a real profile, and the defect it removes takes the circuit down
/// rather than merely showing a duplicate row.
/// </para>
/// </summary>
internal static class ClaudeTranscripts
{
    /// <summary>
    /// Every session the history holds, with the one file that speaks for it. Ordering
    /// is deliberately not decided here: a session list wants the most recent first and
    /// an activity read wants whatever the walk gives it, and a helper that sorted
    /// would be making one of those choices on the other's behalf.
    /// </summary>
    /// <remarks>
    /// A transcript is a file sitting <em>directly</em> in a project folder, and that
    /// position is the whole test. Claude files a session at
    /// <c>projects/&lt;slug&gt;/&lt;id&gt;.jsonl</c> and everything that session spawned
    /// in a folder of its own beside it — <c>projects/&lt;slug&gt;/&lt;id&gt;/subagents/</c>,
    /// holding a sidechain's transcript per subagent and a <c>journal.jsonl</c> of
    /// started/result records per Workflow run. Those belong to the session named by the
    /// folder they are under; none of them is a session.
    /// <para>
    /// This walk used to recurse, and so read every one of those filenames as a session
    /// id. On the profile it was found on that was 1,136 rows that were not sessions
    /// against 460 that were — a <c>journal</c> row with no folder and no branch,
    /// because a journal record states neither, and 1,135 <c>agent-&lt;id&gt;</c> rows
    /// wearing the cwd and branch of the session that spawned them, which is why those
    /// read as plausible rather than as broken. It cost more than the rows: the count of
    /// what a machine has been doing was over three times the truth, and the cap the
    /// list is read under was being spent on them.
    /// </para>
    /// <para>
    /// By position rather than by name, and not only to avoid chasing whatever Claude
    /// files under a session next: the alternative that reads as principled — keep a
    /// file whose basename is the session id it states inside — would have to open all
    /// 1,596 of them to build a list that currently opens none, and it separates the
    /// same two sets. Position is the same answer for free.
    /// </para>
    /// <para>
    /// A file that is in the right place and cannot be read is still a session. That is
    /// a transcript this reader failed on, not a file that was never a transcript, and
    /// the difference is a session missing from the list versus a session listed thinly.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<(string SessionId, FileInfo File)> Newest(string home)
    {
        var folder = new DirectoryInfo(Path.Combine(home, "projects"));

        if (!folder.Exists) return [];

        // One session, one row — even when two project folders hold a transcript for it.
        //
        // A transcript is filed under the folder the session ran in, so a session whose
        // cwd changed — resumed in a worktree it did not start in — is written under a
        // second slug while keeping its id. That is one session filed twice rather than
        // two sessions, so two rows would be wrong on its own terms; it was worse than
        // wrong on screen, for the reason the live dedupe in ClaudeSessionReader gives.
        // Found on a real profile, where one of 377 transcripts was filed under two
        // worktrees.
        //
        // The more recently written file wins, because it is the more current record of
        // the same session: it names the folder that session ended up in, and it is the
        // copy the agent went on appending to. Ties go to the path that sorts first, and
        // that tie-break is not decoration: two copies written in the same instant would
        // otherwise be separated by whichever the directory walk happened to reach first,
        // which nothing guarantees, and the row's folder and branch would change between
        // two refreshes of an unchanged profile.
        //
        // Deduped before the cap, not after, so a session filed twice costs one place in
        // the list rather than two.
        return
        [
            .. folder
                .EnumerateDirectories()
                .SelectMany(project => project.EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly))
                .GroupBy(
                    file => Path.GetFileNameWithoutExtension(file.Name),
                    StringComparer.OrdinalIgnoreCase)
                .Select(duplicates => duplicates
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    .First())

                // The id off the winning file rather than off the group's key: the two
                // agree on every profile anybody has, and the key is whichever spelling
                // the walk reached first under an ignore-case comparison, which is not
                // the same promise.
                .Select(file => (SessionId: Path.GetFileNameWithoutExtension(file.Name), File: file))
        ];
    }

    /// <summary>
    /// Every agent a session spawned, with the one file that speaks for it and the
    /// session it belongs to. Ordering is deliberately not decided here, for the reason
    /// <see cref="Newest"/> gives about its own.
    /// </summary>
    /// <remarks>
    /// The other side of <see cref="Newest"/>'s rule, and here rather than in a sibling
    /// helper because it is the same rule read from the far end. That one says a session
    /// is a file sitting <em>directly</em> in a project folder; this one says everything
    /// under <c>projects/&lt;slug&gt;/&lt;id&gt;/subagents/</c> belongs to the session
    /// named by the folder above it and is none of them. Two owners for one boundary is
    /// how the two walks come to disagree about which files they have each already taken.
    /// <para>
    /// <b>All the way down, not the top of the folder.</b> A Workflow's subagent is filed
    /// under <c>subagents/workflows/wf_&lt;runId&gt;/</c> and a directly spawned one sits
    /// in <c>subagents/</c>. On the profile this was measured against, 377 of 655 files
    /// were the deeper spelling — so a <c>TopDirectoryOnly</c> walk finds 42% of them and
    /// reports a peak of 9 where the truth is 19. This is the one rule here somebody will
    /// "simplify" back into a bug, and the number it produces is plausible rather than
    /// broken, which is what makes it worth the sentence.
    /// </para>
    /// <para>
    /// <b>By name as well as by position.</b> Beside these sit <c>journal.jsonl</c> — a
    /// Workflow run's started/result ledger — and <c>agent-&lt;id&gt;.meta.json</c>
    /// sidecars, and neither is a subagent. Admitting whatever is in the folder would make
    /// the set "whatever Claude files under a session next", which is the open-ended
    /// admission <see cref="Newest"/> refuses in its own remarks.
    /// </para>
    /// <para>
    /// <b>The session comes off the path.</b> The directory that owns the
    /// <c>subagents</c> folder is the session, and the id is never read out of the file.
    /// Opening 1,146 files to learn what the path already says is the trade
    /// <see cref="Newest"/> rejects: position is the same answer for free.
    /// </para>
    /// <para>
    /// <b>A folder that cannot be listed costs its own session.</b> The guard is around
    /// the per-session enumeration rather than around the whole walk, so one session's
    /// permissions lose one session's agents instead of every agent on the machine.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<(string SessionId, string AgentId, FileInfo File)> Spawned(string home)
    {
        var folder = new DirectoryInfo(Path.Combine(home, "projects"));

        if (!folder.Exists) return [];

        var found = new List<(string SessionId, string AgentId, FileInfo File)>();

        foreach (var project in folder.EnumerateDirectories())
        {
            foreach (var session in project.EnumerateDirectories())
            {
                var subagents = new DirectoryInfo(Path.Combine(session.FullName, "subagents"));

                // Most sessions spawn nothing at all, and an absent folder is that rather
                // than a failure: it is the ordinary case and costs nothing to say so.
                if (!subagents.Exists) continue;

                try
                {
                    found.AddRange(subagents
                        .EnumerateFiles("agent-*.jsonl", SearchOption.AllDirectories)
                        .Select(file => (
                            SessionId: session.Name,
                            AgentId: file.Name["agent-".Length..^".jsonl".Length],
                            File: file)));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // This session's agents, and no more. Letting it out would name Claude
                    // unreadable over one folder and take every other session's agents with
                    // it — the disproportion the per-file guard in the activity reader is
                    // already there to avoid.
                    continue;
                }
            }
        }

        // The dedupe ten lines above, applied to the folder beside the transcripts. Zero
        // duplicates measured today, but the mechanism that files a parent transcript
        // twice — a session resumed in a worktree it did not start in — has no reason to
        // spare what that session spawned, and two records for one agent would double it
        // in a figure whose whole point is how many there were at once.
        return
        [
            .. found
                .GroupBy(entry => entry.AgentId, StringComparer.OrdinalIgnoreCase)
                .Select(duplicates => duplicates
                    .OrderByDescending(entry => entry.File.LastWriteTimeUtc)
                    .ThenBy(entry => entry.File.FullName, StringComparer.OrdinalIgnoreCase)
                    .First())
        ];
    }
}
