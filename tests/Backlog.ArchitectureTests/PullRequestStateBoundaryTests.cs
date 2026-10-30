using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Repository Management owns pull-request state, and every other reader reaches it
/// through the module's contract (local ADR 0042). These facts read the source of the
/// readers that used to reach the GitHub adapter on their own, so a pull-request read
/// that goes around the contract — and so around its one cache — fails here rather
/// than in a count of GitHub calls nobody looks at.
/// </summary>
public sealed class PullRequestStateBoundaryTests
{
    /// <summary>
    /// Every pull-request member of <c>GitHubIntegration</c>: the open, pinned and merged
    /// lists, the reads by number (REST and the status query), the merge, auto-merge on
    /// and off, update branch, ready for review and re-run. A module screen reads and
    /// acts on a pull request through its own module's port, which an adapter answers
    /// out of Repository Management's contract, never through these (local ADR 0042 §4).
    /// </summary>
    private static readonly string[] PullRequestMembers =
    [
        "ListOpenPullRequestsAsync",
        "ListPinnedPullRequestsAsync",
        "ListMergedPullRequestsAsync",
        "ReadPullRequestAsync",
        "ReadPullRequestStatusAsync",
        "MergePullRequestAsync",
        "EnableAutoMergeAsync",
        "DisableAutoMergeAsync",
        "UpdateBranchAsync",
        "MarkReadyForReviewAsync",
        "RerunFailedChecksAsync"
    ];

    /// <summary>
    /// Every pull-request member of the adapter's public <c>IGitHubClient</c> whose name the
    /// list above does not already hold: the REST and status reads by number, the read by
    /// number the pinned list sends, the rules a branch requires, and the re-run of a
    /// failed job. ADR 0042 V1 forbids "a pull-request member of
    /// <c>Backlog.Infrastructure.GitHub</c>", not only of <c>GitHubIntegration</c>.
    /// </summary>
    private static readonly string[] ClientPullRequestMembers =
    [
        "GetPullRequestAsync",
        "GetPullRequestStatusAsync",
        "ListPullRequestsAsync",
        "ReadRequiredApprovalsAsync",
        "RerunFailedJobsAsync"
    ];

    /// <summary>
    /// Progress's pull-request reader asks the contract, not the GitHub adapter (local
    /// ADR 0042 §5, V6): no adapter in <c>Infrastructure.FileSystem/Progress</c> that
    /// answers goal pull requests names <c>GitHubIntegration</c> or the GitHub adapter's
    /// namespace.
    /// </summary>
    [Fact]
    public void Progress_reads_pull_requests_through_the_contract()
    {
        var readers = System.IO.Directory
            .EnumerateFiles(RepositoryRoot.Directory("src", "Infrastructure", "Backlog.Infrastructure.FileSystem", "Progress"), "*GoalPullRequests*.cs")
            .ToList();

        Assert.NotEmpty(readers);
        Assert.All(readers, reader =>
        {
            var source = File.ReadAllText(reader);
            Assert.DoesNotContain("GitHubIntegration", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Backlog.Infrastructure.GitHub", source, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// No module screen calls a pull-request member of the GitHub adapter (local ADR 0042
    /// §4, V1): every <c>.cs</c> and <c>.razor</c> file of every <c>src/Modules/*/*.UI</c>
    /// project is read for a call to one of <see cref="PullRequestMembers"/> or
    /// <see cref="ClientPullRequestMembers"/>, or a method group naming one. The task
    /// list reads and merges its pull requests through Tasks' own
    /// <c>ILinkedPullRequestStates</c>, so its one cache is the contract's.
    /// </summary>
    [Fact]
    public void No_module_screen_calls_a_pull_request_member_of_the_GitHub_adapter()
    {
        var screens = Repository.ProjectsUnder("src", "Modules")
            .Where(Repository.IsUserInterface)
            .SelectMany(project => Repository.SourceFilesUnder(project.Directory!, ".cs", ".razor"))
            .ToList();

        Assert.NotEmpty(screens);

        var call = PullRequestMemberCall();
        var offenders = screens
            .SelectMany(screen => File.ReadAllLines(screen.FullName)
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(line => call.IsMatch(line.Line))
                .Select(line => $"{Repository.RelativePath(screen)}:{line.Number}: {line.Line.Trim()}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A module screen calls a pull-request member of the GitHub adapter. Read and act through the module's own port, "
            + "answered out of Repository Management's contract (local ADR 0042 §4):\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// The guard on the rule above: each member it looks for is still a public member of
    /// <c>GitHubIntegration</c>, so renaming one cannot hollow the rule out while it keeps
    /// passing.
    /// </summary>
    [Fact]
    public void Every_pull_request_member_the_screen_rule_looks_for_is_still_on_the_GitHub_adapter()
    {
        var adapter = File.ReadAllText(RepositoryRoot.File("src", "Infrastructure", "Backlog.Infrastructure.GitHub", "GitHubIntegration.cs"));

        Assert.All(PullRequestMembers, member =>
            Assert.Matches(new Regex($@"\bpublic\s+(async\s+)?Task\b[^\n(]*\s{member}\(", RegexOptions.None, TimeSpan.FromSeconds(1)), adapter));
    }

    /// <summary>
    /// The same guard for the client: each client member the rule looks for is still a
    /// member of <c>IGitHubClient</c> in <c>GitHubClient.cs</c>.
    /// </summary>
    [Fact]
    public void Every_client_member_the_screen_rule_looks_for_is_still_on_the_GitHub_client()
    {
        var source = File.ReadAllText(RepositoryRoot.File("src", "Infrastructure", "Backlog.Infrastructure.GitHub", "GitHubClient.cs"));
        var start = source.IndexOf("public interface IGitHubClient", StringComparison.Ordinal);
        Assert.True(start >= 0, "GitHubClient.cs no longer declares IGitHubClient.");
        var client = source[start..];

        Assert.All(ClientPullRequestMembers, member =>
            Assert.Matches(new Regex($@"\n\s+Task\b[^\n(]*\s{member}\(", RegexOptions.None, TimeSpan.FromSeconds(1)), client));
    }

    /// <summary>
    /// The shapes the screen rule must see: a call, and a method group handed on without
    /// one — on the adapter or on its client — whatever the receiver is called; and the
    /// ones it must let through: the module's own port, and a longer name that only
    /// starts like a member.
    /// </summary>
    [Theory]
    [InlineData("ActOnPullRequestAsync(row, pr, _gitHub.MergePullRequestAsync, \"Couldn't merge.\");", true)]
    [InlineData("Func<GitHubPullRequestStatus, Task> merge = gitHub.EnableAutoMergeAsync;", true)]
    [InlineData("return await _gitHub.ReadPullRequestStatusAsync(pr.Repository, pr.Number);", true)]
    [InlineData("var pinned = await client.ListPullRequestsAsync(repository, numbers);", true)]
    [InlineData("acts.Add(client.RerunFailedJobsAsync);", true)]
    [InlineData("<button @onclick=\"() => GitHub.UpdateBranchAsync(pull)\">", true)]
    [InlineData("await _pullRequestStates.MergeNowAsync(key);", false)]
    [InlineData("var x = helper.ReadPullRequestAsyncs;", false)]
    public void The_screen_rule_sees_a_call_and_a_method_group(string line, bool flagged)
    {
        Assert.Equal(flagged, PullRequestMemberCall().IsMatch(line));
    }

    private static Regex PullRequestMemberCall() =>
        new($@"\.({string.Join('|', PullRequestMembers.Concat(ClientPullRequestMembers))})\b", RegexOptions.None, TimeSpan.FromSeconds(1));
}
