using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// A sprint series rides the task feed as an ordinary task document with a type the
/// service does not interpret, one document per series, as the roadmap's plan does
/// (local ADR 0018). Nothing in the service changed for it; these pin that it did not
/// need to — the payload comes back whole between two devices, the replica's precedence
/// applies per series, and the inbox listing never offers one.
/// </summary>
public sealed class SprintSeriesDocumentEndpointTests : IDisposable
{
    private const string SeriesJson =
        """{"version":1,"id":"5a0c7a51-6f4e-4f2b-8d7e-1c2b3a4d5e60","name":"Platform","repositories":["backlog"],"sprints":[{"number":1,"start":"2026-10-12","end":"2026-10-25","goal":"Ship sync","isOpen":true,"items":[{"taskId":"0b6e3f0e-1d7c-4f55-9a55-7c5f7a2f4a10","addedDirectly":true,"isGoal":true,"note":"Waiting on review","openQuestion":true}]}]}""";

    private static readonly Guid SeriesId = Guid.Parse("5a0c7a51-6f4e-4f2b-8d7e-1c2b3a4d5e60");
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A series comes back from the feed byte for byte, at the stamp it was
    /// pushed with. Two paired desktops making the same trip through the real client are
    /// <see cref="SprintSyncBetweenDevicesTests"/>.</summary>
    [Fact]
    public async Task A_series_document_comes_back_from_the_feed_whole()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushChanges([Series(SeriesId, SeriesJson, Morning)]);

        var arrived = Assert.Single((await device.PullTasks()).Tasks);
        Assert.Equal(SeriesId, arrived.Change.Id);
        Assert.Equal("sprint-series", arrived.Change.Task.Type);
        Assert.Equal("Platform", arrived.Change.Task.Title);
        Assert.Equal(SeriesJson, arrived.Change.Task.ContentMd);
        Assert.Equal(Morning, arrived.Change.UpdatedAt);
    }

    /// <summary>The newer write wins per series: an older copy is refused like any stale
    /// task, and the later one stays.</summary>
    [Fact]
    public async Task An_older_copy_of_a_series_is_refused_and_the_later_one_stays()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushChanges([Series(SeriesId, """{"version":1,"name":"Renamed"}""", Morning.AddMinutes(5))]);
        var stale = await device.PushChanges([Series(SeriesId, SeriesJson, Morning)]);

        Assert.Equal(0, (await stale.Content.ReadFromJsonAsync<PushTasksResponse>(Cancellation))!.Accepted);
        var held = Assert.Single((await device.PullTasks()).Tasks);
        Assert.Equal(Morning.AddMinutes(5), held.Change.UpdatedAt);
        Assert.Contains("Renamed", held.Change.Task.ContentMd, StringComparison.Ordinal);
    }

    /// <summary>Two series are two documents: an edit to one never displaces the
    /// other.</summary>
    [Fact]
    public async Task Two_series_are_kept_apart()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");
        var other = Guid.NewGuid();

        await device.PushChanges([Series(SeriesId, SeriesJson, Morning)]);
        await device.PushChanges([Series(other, """{"version":1,"name":"Docs"}""", Morning.AddMinutes(1))]);

        var all = (await device.PullTasks()).Tasks;

        Assert.Equal(new[] { SeriesId, other }.Order(), all.Select(record => record.Change.Id).Order());
    }

    /// <summary>The inbox listing the phone and the IDE read filters on <c>capture</c>, so
    /// a series is never offered as one.</summary>
    [Fact]
    public async Task The_inbox_lists_no_sprint_series()
    {
        var device = await _service.CreateClient().RegisteredDevice("Phone");
        var inbox = SyncRoutes.Absolute(SyncRoutes.Inbox);

        await device.PushChanges([Series(SeriesId, SeriesJson, Morning)]);
        await device.PostAsJsonAsync(inbox, new CaptureRequest("Call the dentist", "phone"), Cancellation);

        var items = (await device.GetFromJsonAsync<List<InboxItem>>(inbox, Cancellation))!;

        Assert.Equal("Call the dentist", Assert.Single(items).Title);
    }

    private static TaskChange Series(Guid id, string content, DateTimeOffset updatedAt) =>
        new(id, updatedAt, DeletedAt: null, TaskSyncClientExtensions.Payload("Platform") with
        {
            ContentMd = content,
            Type = "sprint-series",
        });
}
