using Backlog.Desktop.UI.InProgress;
using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Integrations;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The In progress pane's session cards carry whether the Claude desktop app still
/// holds a linked session open or has archived it — from the session record where
/// it lists the session, and from what the task list last read where it no longer
/// does — so the card colours a finished session the way the Tasks badge does.
/// </summary>
public sealed class WorkLinksProjectionSessionPresenceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, IntegrationSessionPresence.Archived)]
    [InlineData(false, IntegrationSessionPresence.Open)]
    [InlineData(null, IntegrationSessionPresence.Unknown)]
    public void A_listed_session_carries_whether_the_desktop_app_holds_it_open(bool? archived, IntegrationSessionPresence expected)
    {
        var row = Row("listed");
        var projection = Projection(row, [Session("listed") with { Archived = archived }], _ => null);

        var card = Assert.Single(projection.SessionsOf(row));

        Assert.Equal(IntegrationSessionState.Finished, card.State);
        Assert.Equal(expected, card.Presence);
    }

    [Theory]
    [InlineData(true, IntegrationSessionPresence.Archived)]
    [InlineData(false, IntegrationSessionPresence.Open)]
    [InlineData(null, IntegrationSessionPresence.Unknown)]
    public void A_session_the_record_no_longer_lists_carries_what_the_task_list_last_read(bool? archived, IntegrationSessionPresence expected)
    {
        var row = Row("gone");
        var projection = Projection(row, [], id => new LinkedSession(LinkedSessionState.Finished, archived));

        var card = Assert.Single(projection.SessionsOf(row));

        Assert.Equal(IntegrationSessionState.Finished, card.State);
        Assert.Equal(expected, card.Presence);
    }

    private static EntryRow Row(string sessionId) => new()
    {
        Id = Guid.NewGuid(),
        SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", sessionId)]
    };

    private static WorkLinksProjection Projection(EntryRow row, IReadOnlyList<AgentSession> sessions, Func<string, LinkedSession?> linked) =>
        new([row], sessions, [], _ => null, _ => null, linked, Noon);

    private static AgentSession Session(string id) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: id,
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: null,
            StartedAt: Noon.AddHours(-2),
            LastActivityAt: Noon.AddHours(-1),
            State: AgentSessionState.Finished,
            TurnCount: null,
            Origin: AgentSessionOrigin.Local);
}
