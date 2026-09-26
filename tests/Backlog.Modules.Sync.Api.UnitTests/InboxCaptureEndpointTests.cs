using System.Net;
using System.Net.Http.Json;
using System.Text;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The widened capture: a body, tags, a person and a client id, each optional
/// and each bounded at the edge — and the two-field capture every existing
/// client sends, answered exactly as before.
/// </summary>
public sealed class InboxCaptureEndpointTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Inbox => SyncRoutes.Absolute(SyncRoutes.Inbox);

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The desktop pane, the editor extension and an older phone send
    /// <c>{ title, source }</c> and nothing else. Sent as raw JSON, so nothing
    /// the widened record defaults can hide a difference: 201, a service-minted
    /// id, and a document with no body and no tags.</summary>
    [Fact]
    public async Task A_two_field_capture_is_answered_exactly_as_before()
    {
        var device = await Device();

        var response = await device.PostAsync(
            Inbox,
            new StringContent("""{"title":"Call the dentist","source":"phone"}""", Encoding.UTF8, "application/json"),
            Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var item = (await response.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!;
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal("Call the dentist", item.Title);
        Assert.Equal("phone", item.Source);

        var document = Assert.Single((await device.PullTasks()).Tasks).Change;
        Assert.Equal(item.Id, document.Id);
        Assert.Equal("capture", document.Task.Type);
        Assert.Equal(string.Empty, document.Task.ContentMd);
        Assert.Empty(document.Task.Tags);
        Assert.Null(document.Task.Attachments);
        Assert.Null(item.Attachments);
    }

    /// <summary>Everything the phone can now say lands on the document the
    /// desktop pulls: the body as its content, the tags as sent, and the person
    /// as one <c>@name</c> tag the desktop's intake reads back as the person.</summary>
    [Theory]
    [InlineData("alex")]
    [InlineData("@alex")]
    public async Task A_full_capture_writes_its_body_tags_and_person(string person)
    {
        var device = await Device();
        var id = Guid.CreateVersion7();

        var response = await device.PostAsJsonAsync(
            Inbox,
            new CaptureRequest("Ask about the offsite", "phone", id, "Dates, budget, who drives.", ["planning", "#team"], person),
            Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(id, (await response.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!.Id);

        var document = Assert.Single((await device.PullTasks()).Tasks).Change;
        Assert.Equal(id, document.Id);
        Assert.Equal("capture", document.Task.Type);
        Assert.Equal("Dates, budget, who drives.", document.Task.ContentMd);
        Assert.Equal(["planning", "#team", "@alex"], document.Task.Tags);
    }

    /// <summary>The list hands the phone what it needs to show a capture whole:
    /// the body, the tags, and the person split back out of its <c>@name</c> tag
    /// — so no reader has to know that is how the person travels.</summary>
    [Fact]
    public async Task The_inbox_lists_a_capture_with_its_body_tags_and_person()
    {
        var device = await Device();

        await device.PostAsJsonAsync(
            Inbox,
            new CaptureRequest("Ask about the offsite", "phone", Guid.CreateVersion7(), "Dates, budget.", ["planning"], "@alex"),
            Cancellation);
        await device.PostAsJsonAsync(Inbox, new CaptureRequest("Call the dentist", "phone"), Cancellation);

        var items = (await device.GetFromJsonAsync<List<InboxItem>>(Inbox, Cancellation))!;

        var full = Assert.Single(items, item => item.Title == "Ask about the offsite");
        Assert.Equal("Dates, budget.", full.BodyMd);
        Assert.Equal(["planning"], full.Tags!);
        Assert.Equal("alex", full.Person);

        var bare = Assert.Single(items, item => item.Title == "Call the dentist");
        Assert.Null(bare.BodyMd);
        Assert.Empty(bare.Tags!);
        Assert.Null(bare.Person);
    }

    /// <summary>The retry a timed-out 201 forces. The second post carries the
    /// same client id; it answers 200 with the capture already stored, and the
    /// replica holds one document, not two — even when the retry's text
    /// differs, because the first write is what the person was told about.</summary>
    [Fact]
    public async Task A_retry_with_the_same_id_answers_the_stored_capture_and_writes_nothing()
    {
        var device = await Device();
        var id = Guid.CreateVersion7();

        var first = await device.PostAsJsonAsync(Inbox, new CaptureRequest("Call the dentist", "phone", id), Cancellation);
        var stored = (await first.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!;

        var retry = await device.PostAsJsonAsync(Inbox, new CaptureRequest("Call the dentist!", "phone", id), Cancellation);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        // Equivalent rather than Equal: the record carries its tags as a list,
        // and two lists read off two responses are never the same instance.
        Assert.Equivalent(stored, await retry.Content.ReadFromJsonAsync<InboxItem>(Cancellation), strict: true);

        var document = Assert.Single((await device.PullTasks()).Tasks).Change;
        Assert.Equal("Call the dentist", document.Task.Title);
        Assert.Single((await device.GetFromJsonAsync<List<InboxItem>>(Inbox, Cancellation))!);
    }

    /// <summary>A retry that arrives after the capture was already triaged is
    /// still the same capture. Writing it again would restamp the tombstone
    /// live and put a dismissed thought back in every inbox.</summary>
    [Fact]
    public async Task A_retry_after_the_capture_was_acknowledged_does_not_bring_it_back()
    {
        var device = await Device();
        var id = Guid.CreateVersion7();

        await device.PostAsJsonAsync(Inbox, new CaptureRequest("Call the dentist", "phone", id), Cancellation);
        await device.PostAsync(SyncRoutes.AcknowledgeInboxItemFor(id), content: null, Cancellation);

        var retry = await device.PostAsJsonAsync(Inbox, new CaptureRequest("Call the dentist", "phone", id), Cancellation);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Empty((await device.GetFromJsonAsync<List<InboxItem>>(Inbox, Cancellation))!);
        Assert.NotNull(Assert.Single((await device.PullTasks()).Tasks).Change.DeletedAt);
    }

    /// <summary>An id that already names one of the owner's tasks is not a
    /// retry: writing the capture over it would replace the task everywhere.</summary>
    [Fact]
    public async Task A_client_id_that_names_a_task_is_a_conflict()
    {
        var device = await Device();
        var id = Guid.CreateVersion7();
        await device.PushTask(id, "Ship the release");

        var response = await device.PostAsJsonAsync(Inbox, new CaptureRequest("Call the dentist", "phone", id), Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SyncErrorCodes.CaptureIdTaken, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Equal("Ship the release", Assert.Single((await device.PullTasks()).Tasks).Change.Task.Title);
    }

    /// <summary>One id, two owners: the lookup starts from the caller, so the
    /// other owner's capture is not a retry of this one and each gets its own.</summary>
    [Fact]
    public async Task The_same_client_id_under_another_owner_is_a_capture_of_its_own()
    {
        var mine = await Device();
        var theirs = await Device();
        var id = Guid.CreateVersion7();

        await mine.PostAsJsonAsync(Inbox, new CaptureRequest("Mine", "phone", id), Cancellation);
        var response = await theirs.PostAsJsonAsync(Inbox, new CaptureRequest("Theirs", "phone", id), Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Mine", Assert.Single((await mine.GetFromJsonAsync<List<InboxItem>>(Inbox, Cancellation))!).Title);
        Assert.Equal("Theirs", Assert.Single((await theirs.GetFromJsonAsync<List<InboxItem>>(Inbox, Cancellation))!).Title);
    }

    public static TheoryData<string, CaptureRequest> OutOfBounds => new()
    {
        { "empty id", new CaptureRequest("Call", "phone", Id: Guid.Empty) },
        { "body too long", new CaptureRequest("Call", "phone", BodyMd: new string('x', SyncRequestLimits.MaximumCaptureBody + 1)) },
        { "too many tags", new CaptureRequest("Call", "phone", Tags: [.. Enumerable.Range(0, SyncRequestLimits.MaximumCaptureTags + 1).Select(index => $"tag{index}")]) },
        { "tag too long", new CaptureRequest("Call", "phone", Tags: [new string('x', SyncRequestLimits.MaximumCaptureTag + 1)]) },
        { "blank tag", new CaptureRequest("Call", "phone", Tags: ["planning", " "]) },
        { "person as a tag", new CaptureRequest("Call", "phone", Tags: ["@bob"]) },
        { "person as a hashed tag", new CaptureRequest("Call", "phone", Tags: ["#@bob"]) },
        { "person too long", new CaptureRequest("Call", "phone", Person: new string('x', SyncRequestLimits.MaximumCapturePerson + 1)) },
        { "person with a space", new CaptureRequest("Call", "phone", Person: "alex smith") },
    };

    /// <summary>Each new field is bounded at the edge, as the title is: a 400
    /// with the capture code, and nothing reaches the store.</summary>
    [Theory]
    [MemberData(nameof(OutOfBounds))]
    public async Task A_capture_field_out_of_bounds_is_refused(string field, CaptureRequest request)
    {
        var device = await Device();

        var response = await device.PostAsJsonAsync(Inbox, request, Cancellation);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{field}: {response.StatusCode}");
        Assert.Equal(SyncErrorCodes.CaptureInvalid, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Empty((await device.PullTasks()).Tasks);
    }

    /// <summary>Each bound is inclusive: a field exactly at its limit is taken.</summary>
    [Fact]
    public async Task A_capture_at_every_bound_is_taken()
    {
        var device = await Device();

        var response = await device.PostAsJsonAsync(
            Inbox,
            new CaptureRequest(
                "Call",
                "phone",
                Guid.CreateVersion7(),
                new string('b', SyncRequestLimits.MaximumCaptureBody),
                [.. Enumerable.Range(0, SyncRequestLimits.MaximumCaptureTags - 1).Select(index => $"tag{index}"), new string('t', SyncRequestLimits.MaximumCaptureTag)],
                new string('p', SyncRequestLimits.MaximumCapturePerson)),
            Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<HttpClient> Device() => await _service.CreateClient().RegisteredDevice("Phone");
}
