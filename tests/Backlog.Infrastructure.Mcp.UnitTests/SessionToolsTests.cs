using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// What the sessions tool has to say beyond the list itself.
/// </summary>
public class SessionToolsTests
{
    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    /// <summary>
    /// A truncated list says so. Answering with 2 of 842 and no mention of the
    /// 842 would present a slice of the history as the whole of it, which is the
    /// one thing a capped list must not do.
    /// </summary>
    [Fact]
    public async Task A_capped_list_carries_what_it_dropped_and_what_it_could_not_read()
    {
        var catalog = new AgentSessionCatalog(
            [Sessions.Session("one"), Sessions.Session("two")],
            Unreadable: ["copilot: C:\\Users\\someone\\AppData\\Roaming\\…"],
            Discovered: 842);

        var tools = Tools(catalog);

        var answer = await tools.ListSessionsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, answer.Count);
        Assert.Equal(842, answer.Discovered);
        Assert.True(answer.Capped);
        Assert.Single(answer.Unreadable);
    }

    /// <summary>A list nothing was dropped from says that too, rather than
    /// leaving a caller to compare two numbers.</summary>
    [Fact]
    public async Task An_uncapped_list_says_it_is_whole()
    {
        var catalog = new AgentSessionCatalog([Sessions.Session("one")], Unreadable: [], Discovered: 1);

        var tools = Tools(catalog);

        var answer = await tools.ListSessionsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(answer.Capped);
        Assert.Empty(answer.Unreadable);
    }

    /// <summary>
    /// What the agent recorded and what this machine worked out stay two fields.
    /// One is a fact about the session and the other a fact about this machine's
    /// Repositories screen, and folding them together would have every reader
    /// treat the two with one confidence.
    /// </summary>
    [Fact]
    public async Task A_recorded_repository_and_a_resolved_one_stay_apart()
    {
        var catalog = new AgentSessionCatalog(
            [
                Sessions.Session("recorded", repository: "JSdotNet/Backlog"),
                Sessions.Session("resolved", resolvedRepository: "JSdotNet/Backlog"),
                Sessions.Session("neither")
            ],
            Unreadable: [],
            Discovered: 3);

        var tools = Tools(catalog);

        var answer = await tools.ListSessionsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var recorded = answer.Sessions.Single(session => session.Id == "recorded");
        Assert.Equal("JSdotNet/Backlog", recorded.Repository);
        Assert.Null(recorded.ResolvedRepository);

        var resolved = answer.Sessions.Single(session => session.Id == "resolved");
        Assert.Null(resolved.Repository);
        Assert.Equal("JSdotNet/Backlog", resolved.ResolvedRepository);

        var neither = answer.Sessions.Single(session => session.Id == "neither");
        Assert.Null(neither.Repository);
        Assert.Null(neither.ResolvedRepository);
    }

    /// <summary>The inventory's own reading, not a horizon: the two answer
    /// different questions and only one of them is "what is on this
    /// machine".</summary>
    [Fact]
    public async Task The_tool_asks_for_the_newest_reading()
    {
        var source = new FakeAgentSessionSource(AgentSessionCatalog.Empty);

        await new SessionTools(source, new FakeRepositoryDirectory([Backlog]))
            .ListSessionsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, source.NewestReads);
    }

    /// <summary>Omitted, the list is the machine's — the behaviour every caller
    /// had before there was an argument at all.</summary>
    [Fact]
    public async Task Without_a_repository_every_session_travels()
    {
        var catalog = new AgentSessionCatalog(
            [
                Sessions.Session("ours", resolvedRepository: "JSdotNet/Backlog"),
                Sessions.Session("elsewhere", resolvedRepository: "JSdotNet/Other"),
                Sessions.Session("unplaced")
            ],
            Unreadable: [],
            Discovered: 3);

        var answer = await Tools(catalog).ListSessionsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(answer.Repository);
        Assert.Equal(3, answer.Count);
    }

    /// <summary>
    /// The filter reads <see cref="AgentSession.ResolvedRepository"/> — what this
    /// machine worked out from the working folder — and never the recorded one.
    /// Claude writes no repository into anything, so filtering on the recorded
    /// field would hide every Claude session on the machine, running ones
    /// included.
    /// </summary>
    [Fact]
    public async Task A_repository_narrows_on_what_this_machine_resolved()
    {
        var catalog = new AgentSessionCatalog(
            [
                Sessions.Session("resolved", resolvedRepository: "JSdotNet/Backlog"),
                Sessions.Session("resolved, other casing", resolvedRepository: "jsdotnet/backlog"),
                Sessions.Session("recorded only", repository: "JSdotNet/Backlog"),
                Sessions.Session("elsewhere", resolvedRepository: "JSdotNet/Other"),
                Sessions.Session("unplaced")
            ],
            Unreadable: [],
            Discovered: 5);

        var answer = await Tools(catalog).ListSessionsAsync(
            "JSdotNet/Backlog",
            TestContext.Current.CancellationToken);

        Assert.Equal("JSdotNet/Backlog", answer.Repository);
        Assert.Equal(["resolved", "resolved, other casing"], answer.Sessions.Select(session => session.Id));
        Assert.Equal(2, answer.Count);
    }

    /// <summary>
    /// What the cap took is the catalog's own number and stays that under a
    /// filter. The cap ran before this tool saw a session, so a repository's
    /// oldest sessions can be missing from a capped catalog and no filtering here
    /// could tell — recomputing <c>Capped</c> over the filtered list would turn
    /// that warning off exactly when it is needed. <c>Unreadable</c> is not
    /// filtered either: a source nobody could read might have held this
    /// repository's sessions.
    /// </summary>
    [Fact]
    public async Task A_filtered_list_still_reports_the_catalog_s_own_truncation()
    {
        var catalog = new AgentSessionCatalog(
            [
                Sessions.Session("ours", resolvedRepository: "JSdotNet/Backlog"),
                Sessions.Session("elsewhere", resolvedRepository: "JSdotNet/Other")
            ],
            Unreadable: ["copilot: C:\\Users\\someone\\AppData\\Roaming\\…"],
            Discovered: 842);

        var answer = await Tools(catalog).ListSessionsAsync(
            "JSdotNet/Backlog",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, answer.Count);
        Assert.Equal(842, answer.Discovered);
        Assert.True(answer.Capped);
        Assert.Single(answer.Unreadable);
    }

    /// <summary>An unknown repository refuses here as everywhere else, rather
    /// than answering with an empty list that reads as "no sessions" — and it
    /// never registers what it was asked about.</summary>
    [Fact]
    public async Task An_unknown_repository_is_refused_and_never_registered()
    {
        var directory = new FakeRepositoryDirectory([Backlog]);
        var source = new FakeAgentSessionSource(AgentSessionCatalog.Empty);

        var failure = await Assert.ThrowsAsync<McpException>(() => new SessionTools(source, directory)
            .ListSessionsAsync("JSdotNet/Nowhere", TestContext.Current.CancellationToken));

        Assert.Contains("repository.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Empty(directory.Registered);

        // Refused before the catalog was read: there was nothing to answer with.
        Assert.Equal(0, source.NewestReads);
    }

    /// <summary>A blank argument is no argument. The client that sends an empty
    /// string means "all", not "the repository called nothing".</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_repository_is_the_same_as_omitting_it(string repository)
    {
        var catalog = new AgentSessionCatalog(
            [Sessions.Session("elsewhere", resolvedRepository: "JSdotNet/Other")],
            Unreadable: [],
            Discovered: 1);

        var answer = await Tools(catalog).ListSessionsAsync(repository, TestContext.Current.CancellationToken);

        Assert.Null(answer.Repository);
        Assert.Single(answer.Sessions);
    }

    private static SessionTools Tools(AgentSessionCatalog catalog) =>
        new(new FakeAgentSessionSource(catalog), new FakeRepositoryDirectory([Backlog]));
}
