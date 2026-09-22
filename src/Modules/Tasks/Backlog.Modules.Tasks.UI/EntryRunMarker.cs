namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The line every entry copied out of the app opens with, so that a paste of it
/// into an AI session is a runnable prompt rather than a title and some prose.
/// <para>
/// A slash command, not a sentence: pasted as the first line of a message it
/// invokes the <c>backlog-run-plan-item</c> skill from the <c>backlog-tools</c>
/// plugin outright, with the entry's stored id as its argument and the title
/// and body that follow as the rest of the prompt. The id is the one thing the
/// line carries. It is what a connector is asked for, and everything else the
/// metadata line knew — the plan, the repositories, the dependencies — is
/// either answered by that connector or, for an imported prompt, restated by
/// the plan-item marker <c>backlog-import-plan</c> already wrote into the body.
/// </para>
/// <para>
/// The id is the stored one and never the local <c>id:</c> slug, and the line
/// ends in a colon so what follows reads as the argument's content rather than
/// as more of the command.
/// </para>
/// </summary>
public static class EntryRunMarker
{
    public static string Build(Guid id) =>
        $"/backlog-tools:backlog-run-plan-item entry `{id}`:";
}
