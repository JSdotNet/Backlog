using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// A pull cursor is the owner's: replayed by another owner's device it is refused
/// as not theirs, on every synced type's pull route. One case per type, each
/// pushing one of its own and replaying the cursor the pull returned.
/// <para>
/// The session and task routes assert more than the refusal — that a proper pull
/// still shows the other owner nothing — so they keep their own copy of this test.
/// </para>
/// </summary>
public sealed class SyncCursorOwnerEndpointTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(SyncRoutes.Annotations)]
    [InlineData(SyncRoutes.DevbookSubjects)]
    [InlineData(SyncRoutes.KnowledgeEvents)]
    [InlineData(SyncRoutes.Notifications)]
    public async Task A_cursor_minted_for_another_owner_is_refused(string route)
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        var myCursor = await PushOneAndPullCursor(mine, route);

        var replayed = await theirs.GetAsync(
            $"{SyncRoutes.Absolute(route)}?since={Uri.EscapeDataString(myCursor)}", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, replayed.StatusCode);
        var problem = await replayed.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, problem?.Code);
    }

    private static async Task<string> PushOneAndPullCursor(HttpClient mine, string route)
    {
        switch (route)
        {
            case SyncRoutes.Annotations:
                await mine.PushAnnotation(Guid.CreateVersion7(), "My private note.");
                return (await mine.PullAnnotations()).Since;

            case SyncRoutes.DevbookSubjects:
                await mine.PushSubjects([DevbookSubjectSyncEndpointTests.Debt(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "planned")]);
                return (await mine.PullSubjects()).Since;

            case SyncRoutes.KnowledgeEvents:
                await mine.PushEvents([KnowledgeEventSyncEndpointTests.Event(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "Planned")]);
                return (await mine.PullEvents()).Since;

            case SyncRoutes.Notifications:
                await mine.PushNotifications(NotificationSyncEndpointTests.Change(Guid.CreateVersion7(), NotificationStates.Open, "Private."));
                return (await mine.PullNotifications()).Since;

            default:
                throw new ArgumentOutOfRangeException(nameof(route), route, "No synced type is pushed on this route.");
        }
    }
}
