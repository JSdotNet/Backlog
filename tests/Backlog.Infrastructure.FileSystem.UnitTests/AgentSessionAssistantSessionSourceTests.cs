using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The one place in the product that may see both the Sessions context and the
/// Dashboard's. What is worth asserting about it is that it is a mapping and not a
/// second opinion: every judgement it passes on — what an assistant is called, how many
/// sessions a read stops at, how much was left unread — is the other context's, so a
/// change there reaches the dashboard rather than being contradicted here.
/// </summary>
public class AgentSessionAssistantSessionSourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_session_arrives_as_a_machine_an_assistant_and_two_timestamps()
    {
        var source = Source(Session("claude-1", AgentSessionKind.Claude, AgentSessionState.Finished));

        var session = Assert.Single((await source.GetSessionsAsync()).Sessions);

        // The id is the machine, and the name is only what it is called: the Sessions
        // context's Environment splits into exactly those two here.
        Assert.Equal("tower", session.MachineId);
        Assert.Equal("DEV-TOWER", session.MachineName);
        Assert.Equal("Claude", session.Assistant);
        Assert.Equal(Noon.AddHours(-2), session.StartedAt);
        Assert.Equal(Noon.AddHours(-1), session.LastActivityAt);
    }

    /// <summary>
    /// The label comes from the Sessions context rather than from a switch here, so a
    /// third assistant appears on the dashboard the day it appears in the session list.
    /// </summary>
    [Theory]
    [InlineData(AgentSessionKind.Claude, "Claude")]
    [InlineData(AgentSessionKind.Copilot, "Copilot")]
    public async Task An_assistant_is_named_the_way_the_sessions_context_names_it(
        AgentSessionKind kind,
        string expected)
    {
        var source = Source(Session("one", kind, AgentSessionState.Finished));

        var session = Assert.Single((await source.GetSessionsAsync()).Sessions);

        Assert.Equal(expected, session.Assistant);
    }

    /// <summary>
    /// A capped catalog has to arrive at the dashboard as capped, and an agent whose
    /// folder could not be read has to arrive by name. Both are what let the part say
    /// its figures are a floor instead of presenting a partial reading as a whole one.
    /// </summary>
    [Fact]
    public async Task What_could_not_be_read_and_what_was_left_out_travel_with_the_sessions()
    {
        var catalog = new AgentSessionCatalog(
            [Session("one", AgentSessionKind.Claude, AgentSessionState.Finished)],
            ["Copilot"],
            Discovered: 842);

        var report = await new AgentSessionAssistantSessionSource(new StubAgentSessionSource(catalog))
            .GetSessionsAsync();

        Assert.True(report.Capped);
        Assert.Equal(["Copilot"], report.Unreadable);

        // And the cap itself, so the sentence on screen names the number the Sessions
        // context actually stopped at rather than a copy of it kept beside the part.
        Assert.Equal(AgentSessionLimits.PerAgent, report.CapPerAssistant);
    }

    /// <summary>
    /// A missing agent folder is an unreadable source, not an unavailable one. Saying
    /// the source cannot answer would blank a part whose job is to report what half of
    /// the picture is there.
    /// </summary>
    [Fact]
    public async Task The_source_is_always_available_even_with_nothing_to_report()
    {
        var source = Source();

        var availability = await source.GetAvailabilityAsync();

        Assert.True(availability.IsAvailable);
        Assert.Empty((await source.GetSessionsAsync()).Sessions);
    }

    private static AgentSessionAssistantSessionSource Source(params AgentSession[] sessions) =>
        new(new StubAgentSessionSource(new AgentSessionCatalog(sessions, [], sessions.Length)));

    private static AgentSession Session(string id, AgentSessionKind kind, AgentSessionState state) =>
        new(
            Id: id,
            Kind: kind,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: id,
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: null,
            StartedAt: Noon.AddHours(-2),
            LastActivityAt: Noon.AddHours(-1),
            State: state);

    private sealed class StubAgentSessionSource(AgentSessionCatalog catalog) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(catalog);
    }
}
