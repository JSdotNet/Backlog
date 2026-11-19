using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// <c>PUT /api/sync/inbox/{id}</c>: the phone rewording a capture the desktop
/// has not decided yet. The new title and body reach the document the desktop
/// pulls; a capture already decided answers 404 with the code the phone reads
/// as "already dealt with"; and the desktop's tombstone still wins the race
/// against an edit that landed first.
/// </summary>
public sealed class InboxCaptureEditEndpointTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Inbox => SyncRoutes.Absolute(SyncRoutes.Inbox);

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task An_edit_answers_the_reworded_capture_and_the_desktop_pulls_it()
    {
        var device = await Device();
        var id = await Captured(device, new CaptureRequest("Call the dentist", "phone", Guid.CreateVersion7(), "before noon", ["health"], "sam"));

        var response = await device.PutAsJsonAsync(
            SyncRoutes.EditInboxItemFor(id), new CaptureEditRequest("Call the dentist about the crown", "Ask for Thursday."), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!;
        Assert.Equal(id, item.Id);
        Assert.Equal("Call the dentist about the crown", item.Title);
        Assert.Equal("Ask for Thursday.", item.BodyMd);
        Assert.Equal("phone", item.Source);
        Assert.Equal(["health"], item.Tags!);
        Assert.Equal("sam", item.Person);

        var document = Assert.Single((await device.PullTasks()).Tasks).Change;
        Assert.Equal("Call the dentist about the crown", document.Task.Title);
        Assert.Equal("Ask for Thursday.", document.Task.ContentMd);
        Assert.Equal("Call the dentist about the crown", Assert.Single((await device.GetFromJsonAsync<List<InboxItem>>(Inbox, Cancellation))!).Title);
    }

    public static TheoryData<string, CaptureEditRequest> OutOfBounds => new()
    {
        { "empty title", new CaptureEditRequest(string.Empty, null) },
        { "blank title", new CaptureEditRequest("   ", "body") },
        { "title too long", new CaptureEditRequest(new string('x', SyncRequestLimits.MaximumCaptureTitle + 1), null) },
        { "body too long", new CaptureEditRequest("Call", new string('x', SyncRequestLimits.MaximumCaptureBody + 1)) },
    };

    /// <summary>The same bounds a capture meets when it is made, and nothing
    /// reaches the store.</summary>
    [Theory]
    [MemberData(nameof(OutOfBounds))]
    public async Task An_edit_out_of_bounds_is_refused_and_writes_nothing(string field, CaptureEditRequest request)
    {
        var device = await Device();
        var id = await Captured(device, new CaptureRequest("Call the dentist", "phone", Guid.CreateVersion7()));

        var response = await device.PutAsJsonAsync(SyncRoutes.EditInboxItemFor(id), request, Cancellation);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{field}: {response.StatusCode}");
        Assert.Equal(SyncErrorCodes.CaptureInvalid, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Equal("Call the dentist", Assert.Single((await device.PullTasks()).Tasks).Change.Task.Title);
    }

    [Fact]
    public async Task An_edit_at_the_bounds_is_taken()
    {
        var device = await Device();
        var id = await Captured(device, new CaptureRequest("Call the dentist", "phone", Guid.CreateVersion7()));

        var response = await device.PutAsJsonAsync(
            SyncRoutes.EditInboxItemFor(id),
            new CaptureEditRequest(new string('x', SyncRequestLimits.MaximumCaptureTitle), new string('y', SyncRequestLimits.MaximumCaptureBody)),
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The desktop decided first: the phone hears the code it treats as
    /// delivered, and the tombstone stays.</summary>
    [Fact]
    public async Task An_edit_of_an_acknowledged_capture_is_not_found_with_the_inbox_code()
    {
        var device = await Device();
        var id = await Captured(device, new CaptureRequest("Call the dentist", "phone", Guid.CreateVersion7()));
        await device.PostAsync(SyncRoutes.AcknowledgeInboxItemFor(id), content: null, Cancellation);

        var response = await device.PutAsJsonAsync(SyncRoutes.EditInboxItemFor(id), new CaptureEditRequest("Reworded", null), Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SyncErrorCodes.InboxItemNotFound, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.NotNull(Assert.Single((await device.PullTasks()).Tasks).Change.DeletedAt);
    }

    [Fact]
    public async Task An_edit_of_another_owners_capture_is_not_found_and_changes_nothing()
    {
        var mine = await Device();
        var theirs = await Device();
        var id = await Captured(mine, new CaptureRequest("Mine", "phone", Guid.CreateVersion7()));

        var response = await theirs.PutAsJsonAsync(SyncRoutes.EditInboxItemFor(id), new CaptureEditRequest("Theirs now", null), Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SyncErrorCodes.InboxItemNotFound, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Equal("Mine", Assert.Single((await mine.GetFromJsonAsync<List<InboxItem>>(Inbox, Cancellation))!).Title);
    }

    [Fact]
    public async Task An_edit_without_a_token_is_a_401()
    {
        var anonymous = _service.CreateClient();

        var response = await anonymous.PutAsJsonAsync(
            SyncRoutes.EditInboxItemFor(Guid.CreateVersion7()), new CaptureEditRequest("Reworded", null), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The race: the desktop decided before the edit landed, and its
    /// tombstone — stamped at the decision, before the edit — arrives last. The
    /// capture still ends tombstoned and off the phone's list.</summary>
    [Fact]
    public async Task The_desktops_tombstone_stamped_before_an_edit_still_takes_the_capture_out_of_the_inbox()
    {
        var device = await Device();
        var id = await Captured(device, new CaptureRequest("Call the dentist", "phone", Guid.CreateVersion7()));
        var held = Assert.Single((await device.PullTasks()).Tasks).Change;
        var decidedAt = held.UpdatedAt.AddTicks(1);

        (await device.PutAsJsonAsync(SyncRoutes.EditInboxItemFor(id), new CaptureEditRequest("Reworded", null), Cancellation)).EnsureSuccessStatusCode();

        var push = await device.PushChanges([held with { UpdatedAt = decidedAt, DeletedAt = decidedAt }]);

        push.EnsureSuccessStatusCode();
        Assert.Empty((await device.GetFromJsonAsync<List<InboxItem>>(Inbox, Cancellation))!);
        Assert.NotNull(Assert.Single((await device.PullTasks()).Tasks).Change.DeletedAt);
    }

    private static async Task<Guid> Captured(HttpClient device, CaptureRequest request)
    {
        var response = await device.PostAsJsonAsync(Inbox, request, Cancellation);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!.Id;
    }

    private async Task<HttpClient> Device() => await _service.CreateClient().RegisteredDevice("Phone");
}
