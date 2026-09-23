namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// A pull request a session linked itself to — Claude writes a <c>pr-link</c> line into
/// the transcript when a session opens or adopts one.
/// </summary>
/// <param name="Repository"><c>owner/name</c>, as the line spelled it.</param>
/// <param name="Number">The pull request's number in that repository.</param>
/// <param name="Url">Where it lives.</param>
/// <param name="LinkedAt">When the session recorded the link, where the line dated it.</param>
public sealed record AgentPullRequest(string Repository, int Number, string Url, DateTimeOffset? LinkedAt);

/// <summary>
/// What a session spent on one model: the assistant's own token counts, summed over the
/// messages it answered with that model.
/// <para>
/// Summed once per message rather than once per line. Claude writes a message's content
/// blocks as separate lines that each repeat the message's usage, so a sum over lines
/// counts a three-block answer three times.
/// </para>
/// <para>
/// The session's own transcript only: the agents it spawned write theirs elsewhere, so
/// this is the owner session's spend and not the whole tree's.
/// </para>
/// </summary>
public sealed record AgentModelUsage(
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens);
