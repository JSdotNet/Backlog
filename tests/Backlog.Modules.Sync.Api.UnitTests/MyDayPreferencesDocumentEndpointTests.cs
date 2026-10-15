using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The My Day preferences ride the task feed as an ordinary task document with a type the
/// service does not interpret (local ADR 0032, §6). Nothing in the service changed for
/// them; these pin that it did not need to (Verification 8) — the payload comes back whole
/// and the inbox listing never offers it.
/// </summary>
public sealed class MyDayPreferencesDocumentEndpointTests : IDisposable
{
    private const string PreferencesJson = """{ "updatedAt": "2026-10-15T08:00:00Z", "clearsOvernight": false }""";

    private static readonly Guid PreferencesId = Guid.Parse("4d7a9c2e-5b1f-4e3a-8c6d-2f0e1a3b5c01");
    private static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_preferences_document_comes_back_from_the_feed_whole()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushChanges([Preferences(Morning)]);

        var arrived = Assert.Single((await device.PullTasks()).Tasks);
        Assert.Equal(PreferencesId, arrived.Change.Id);
        Assert.Equal("my-day-preferences", arrived.Change.Task.Type);
        Assert.Equal("My Day preferences", arrived.Change.Task.Title);
        Assert.Equal(PreferencesJson, arrived.Change.Task.ContentMd);
        Assert.Null(arrived.Change.Task.InMyDayOn);
        Assert.Equal(Morning, arrived.Change.UpdatedAt);
    }

    /// <summary>The inbox listing the phone and the IDE read filters on <c>capture</c>, so
    /// the preferences are never offered as one.</summary>
    [Fact]
    public async Task The_inbox_lists_no_my_day_preferences()
    {
        var device = await _service.CreateClient().RegisteredDevice("Phone");
        var inbox = SyncRoutes.Absolute(SyncRoutes.Inbox);

        await device.PushChanges([Preferences(Morning)]);
        await device.PostAsJsonAsync(inbox, new CaptureRequest("Call the dentist", "phone"), Cancellation);

        var items = (await device.GetFromJsonAsync<List<InboxItem>>(inbox, Cancellation))!;

        Assert.Equal("Call the dentist", Assert.Single(items).Title);
    }

    private static TaskChange Preferences(DateTimeOffset updatedAt) =>
        new(PreferencesId, updatedAt, DeletedAt: null, TaskSyncClientExtensions.Payload("My Day preferences") with
        {
            ContentMd = PreferencesJson,
            Type = "my-day-preferences",
            InMyDayOn = null,
        });
}
