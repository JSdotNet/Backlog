using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a chapter's last commit said, without a commit.
/// <para>
/// The real service is a conversation with four git processes, and it has its own
/// tests against a real repository. What a panel needs from it is one answer per
/// path, so that is what this gives — and, because "there is no committed version"
/// is the ordinary state of a chapter in a temp folder nobody has committed, that
/// is what it says unless a test asks for something else.
/// </para>
/// <para>
/// <see cref="Reads"/> is the point of the class as much as the answer is: a panel
/// that read the committed version on every render, rather than when a reader asked
/// for it, would still look right and would spend a process per keystroke.
/// </para>
/// </summary>
internal sealed class StubGitFileHistory : IGitFileHistoryService
{
    private readonly Func<string, GitFileAtRevisionResult> answer;

    internal StubGitFileHistory(Func<string, GitFileAtRevisionResult>? answer = null) =>
        this.answer = answer ?? (_ => GitFileAtRevisionResult.NotTracked());

    /// <summary>Every path asked about, in order.</summary>
    internal List<string> Reads { get; } = [];

    /// <summary>Answers with one committed text for whatever is asked.</summary>
    internal static StubGitFileHistory Committed(string content) =>
        new(_ => GitFileAtRevisionResult.Committed(content));

    public Task<GitFileAtRevisionResult> ReadAtHeadAsync(string absoluteFilePath, CancellationToken cancellationToken = default)
    {
        Reads.Add(absoluteFilePath);
        return Task.FromResult(answer(absoluteFilePath));
    }
}
