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
                .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
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
}
