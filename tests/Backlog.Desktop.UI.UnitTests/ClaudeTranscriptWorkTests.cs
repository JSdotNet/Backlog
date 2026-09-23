using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a Claude transcript says about a session's work, over lines in the shapes Claude
/// really writes them.
/// </summary>
public sealed class ClaudeTranscriptWorkTests
{
    /// <summary>A <c>pr-link</c> line is a pull request the session linked; the same link
    /// written again is the same pull request, dated by its first line.</summary>
    [Fact]
    public void Pull_request_links_are_read_once_each()
    {
        var work = Observed(
            """{"type":"pr-link","sessionId":"s","prNumber":142,"prUrl":"https://github.com/JSdotNet/ai-plugins/pull/142","prRepository":"JSdotNet/ai-plugins","timestamp":"2026-09-14T19:58:28.742Z"}""",
            """{"type":"pr-link","sessionId":"s","prNumber":142,"prUrl":"https://github.com/JSdotNet/ai-plugins/pull/142","prRepository":"JSdotNet/ai-plugins","timestamp":"2026-09-15T08:00:00.000Z"}""",
            """{"type":"pr-link","sessionId":"s","prNumber":"not a number","prUrl":"https://x","prRepository":"a/b"}""");

        var pr = Assert.Single(work.PullRequests);
        Assert.Equal(new AgentPullRequest("JSdotNet/ai-plugins", 142, "https://github.com/JSdotNet/ai-plugins/pull/142", DateTimeOffset.Parse("2026-09-14T19:58:28.742Z")), pr);
    }

    /// <summary>Usage is summed once per message and per model: the lines of one message
    /// repeat its usage, a sidechain's spend is its own agent's, and a synthetic line is
    /// bookkeeping.</summary>
    [Fact]
    public void Tokens_are_summed_per_message_and_per_model()
    {
        var work = Observed(
            Assistant("m1", "claude-opus-5-5", input: 10, output: 100),
            Assistant("m1", "claude-opus-5-5", input: 10, output: 100),
            Assistant("m2", "claude-opus-5-5", input: 5, output: 50, cacheRead: 1000),
            Assistant("m3", "claude-sonnet-5", input: 1, output: 2),
            Assistant("m4", "claude-sonnet-5", input: 999, output: 999, sidechain: true),
            Assistant("m5", "<synthetic>", input: 0, output: 0));

        Assert.Equal(
            [
                new AgentModelUsage("claude-opus-5-5", 15, 150, 0, 1000),
                new AgentModelUsage("claude-sonnet-5", 1, 2, 0, 0)
            ],
            work.ModelUsage);
    }

    /// <summary>The entrypoint is the first one a line names.</summary>
    [Fact]
    public void The_entrypoint_is_the_first_named()
    {
        var work = Observed(
            """{"type":"user","entrypoint":"claude-desktop","sessionId":"s"}""",
            """{"type":"user","entrypoint":"cli","sessionId":"s"}""");

        Assert.Equal("claude-desktop", work.Entrypoint);
    }

    private static ClaudeTranscriptWork Observed(params string[] lines)
    {
        var work = new ClaudeTranscriptWork();

        foreach (var line in lines) work.Observe(line);

        return work;
    }

    private static string Assistant(string id, string model, long input, long output, long cacheRead = 0, bool sidechain = false) =>
        $$"""{"type":"assistant","isSidechain":{{(sidechain ? "true" : "false")}},"message":{"id":"{{id}}","model":"{{model}}","usage":{"input_tokens":{{input}},"output_tokens":{{output}},"cache_creation_input_tokens":0,"cache_read_input_tokens":{{cacheRead}}""" + "}}}";
}
