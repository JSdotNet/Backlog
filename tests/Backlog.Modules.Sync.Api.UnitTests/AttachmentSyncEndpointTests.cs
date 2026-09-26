using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The attachment store's one door (local ADR 0014): an upload is bounded by
/// the cap and the allowlist and checked against its digest, a download is the
/// caller's own or nothing, and a capture can only name what was uploaded — and
/// releases it when it is acknowledged.
/// </summary>
public sealed class AttachmentSyncEndpointTests : IDisposable
{
    /// <summary>Small, so a test can go over it without allocating 25 MB.</summary>
    private const long Cap = 1024;

    private readonly SyncServiceFactory _service = new()
    {
        Configuration = ($"{SyncAttachmentOptions.SectionName}:MaxBytes", Cap.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task An_upload_is_stored_and_streams_back_as_a_download()
    {
        var device = await Device();
        var id = Guid.CreateVersion7();
        var bytes = Bytes(300);

        var upload = await device.Upload(id, bytes, "image/jpeg");

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var stored = (await upload.Content.ReadFromJsonAsync<StoredAttachment>(Cancellation))!;
        Assert.Equal(new StoredAttachment(id, "image/jpeg", 300, Sha256(bytes)), stored);

        var download = await device.GetAsync(SyncRoutes.AttachmentFor(id), Cancellation);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(Sha256(bytes), Assert.Single(download.Headers.GetValues(SyncRoutes.AttachmentSha256Header)));
    }

    /// <summary>The retry a lost 201 forces: the same bytes under the same id
    /// are a 200 that writes nothing. Different bytes under it are a conflict,
    /// and the first file stays.</summary>
    [Fact]
    public async Task A_repeat_upload_is_a_200_and_different_bytes_under_the_same_id_a_409()
    {
        var device = await Device();
        var id = Guid.CreateVersion7();
        var bytes = Bytes(100);

        Assert.Equal(HttpStatusCode.Created, (await device.Upload(id, bytes, "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await device.Upload(id, bytes, "application/pdf")).StatusCode);

        var conflict = await device.Upload(id, Bytes(100, seed: 7), "application/pdf");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(SyncErrorCodes.AttachmentConflict, (await Problem(conflict)).Code);
        Assert.Equal(bytes, await (await device.GetAsync(SyncRoutes.AttachmentFor(id), Cancellation)).Content.ReadAsByteArrayAsync(Cancellation));
    }

    /// <summary>Over the cap, with the length declared up front: refused before
    /// the body is read, naming the field, and nothing stored.</summary>
    [Fact]
    public async Task An_upload_whose_Content_Length_passes_the_cap_is_a_413_naming_it()
    {
        var device = await Device();
        var id = Guid.CreateVersion7();

        var response = await device.Upload(id, Bytes((int)Cap + 1), "image/png");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal(SyncErrorCodes.AttachmentTooLarge, problem.Code);
        Assert.Contains("Content-Length", problem.Detail, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync(SyncRoutes.AttachmentFor(id), Cancellation)).StatusCode);
    }

    /// <summary>Over the cap with no length at all — a chunked body. Only
    /// reading it can tell, and the read stops where the cap is passed.</summary>
    [Fact]
    public async Task An_unsized_upload_that_runs_past_the_cap_is_a_413_and_stores_nothing()
    {
        var device = await Device();
        var id = Guid.CreateVersion7();
        var bytes = Bytes((int)Cap * 3);

        var response = await device.Upload(id, bytes, "image/png", unsized: true);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("Content-Length", (await Problem(response)).Detail, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync(SyncRoutes.AttachmentFor(id), Cancellation)).StatusCode);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/x-msdownload")]
    [InlineData("application/javascript")]
    public async Task A_type_outside_the_allowlist_is_a_415_naming_the_field_and_the_type(string contentType)
    {
        var device = await Device();

        var response = await device.Upload(Guid.CreateVersion7(), Bytes(10), contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal(SyncErrorCodes.AttachmentTypeNotAllowed, problem.Code);
        Assert.Contains("Content-Type", problem.Detail, StringComparison.Ordinal);
        Assert.Contains(contentType, problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>A charset or other parameter does not change what a type is.</summary>
    [Fact]
    public async Task An_allowed_type_with_parameters_is_accepted_and_served_as_declared()
    {
        var device = await Device();
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.Created, (await device.Upload(id, Bytes(10), "text/plain; charset=utf-8")).StatusCode);

        var download = await device.GetAsync(SyncRoutes.AttachmentFor(id), Cancellation);
        Assert.Equal("utf-8", download.Content.Headers.ContentType?.CharSet);
    }

    [Fact]
    public async Task Bytes_that_do_not_hash_to_the_declared_digest_are_refused_and_not_stored()
    {
        var device = await Device();
        var id = Guid.CreateVersion7();

        var response = await device.Upload(id, Bytes(50), "image/png", sha256: Sha256(Bytes(50, seed: 3)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal(SyncErrorCodes.AttachmentInvalid, problem.Code);
        Assert.Contains(SyncRoutes.AttachmentSha256Header, problem.Detail, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync(SyncRoutes.AttachmentFor(id), Cancellation)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-digest")]
    public async Task An_upload_without_a_well_formed_digest_is_a_400_naming_the_header(string sha256)
    {
        var device = await Device();

        var response = await device.Upload(Guid.CreateVersion7(), Bytes(10), "image/png", sha256: sha256);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(SyncRoutes.AttachmentSha256Header, (await Problem(response)).Detail, StringComparison.Ordinal);
    }

    /// <summary>Two owners, one id. The second owner's download is looked up
    /// under its own prefix and finds nothing — a 404, never the file and never
    /// a 403 that would confirm the id exists.</summary>
    [Fact]
    public async Task Another_owners_download_of_an_attachment_is_a_404()
    {
        var owner = await Device();
        var stranger = await _service.CreateClient().RegisteredDevice("Someone else's phone");
        var id = Guid.CreateVersion7();

        await owner.Upload(id, Bytes(20), "image/png");

        var response = await stranger.GetAsync(SyncRoutes.AttachmentFor(id), Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SyncErrorCodes.AttachmentNotFound, (await Problem(response)).Code);
    }

    [Fact]
    public async Task An_attachment_route_without_a_token_is_a_401()
    {
        var anonymous = _service.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(SyncRoutes.AttachmentFor(Guid.CreateVersion7()), Cancellation)).StatusCode);
    }

    /// <summary>The store down — locally, Azurite still starting — is the same
    /// 503 the replica gives, under the store's own code.</summary>
    [Fact]
    public async Task An_unreachable_store_is_a_503()
    {
        using var service = new SyncServiceFactory
        {
            TestServices = services => services.Replace(ServiceDescriptor.Singleton<IAttachmentStore>(new UnreachableAttachmentStore())),
        };
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Upload(Guid.CreateVersion7(), Bytes(10), "image/png");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(SyncErrorCodes.AttachmentStoreUnavailable, (await Problem(response)).Code);
    }

    [Fact]
    public async Task A_capture_naming_an_uploaded_attachment_carries_its_metadata_to_the_pull_and_the_list()
    {
        var device = await Device();
        var bytes = Bytes(120);
        var attachment = Metadata(Guid.CreateVersion7(), bytes, "slide.jpg", "image/jpeg");
        await device.Upload(attachment.Id, bytes, "image/jpeg");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox),
            new CaptureRequest("Keynote", "phone", Attachments: [attachment]),
            Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([attachment], (await response.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!.Attachments!);

        var document = Assert.Single((await device.PullTasks()).Tasks).Change;
        Assert.Equal([attachment], document.Task.Attachments!);

        var listed = Assert.Single((await device.GetFromJsonAsync<List<InboxItem>>(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation))!);
        Assert.Equal([attachment], listed.Attachments!);
    }

    /// <summary>A capture may only name what this owner has uploaded, as it
    /// claims it: an id never uploaded, and one uploaded with other bytes, are
    /// both named in the refusal, and no capture is written.</summary>
    [Fact]
    public async Task A_capture_naming_an_attachment_not_uploaded_is_a_400_naming_it()
    {
        var device = await Device();
        var uploaded = Bytes(40);
        var mismatched = Metadata(Guid.CreateVersion7(), Bytes(40, seed: 9), "b.pdf", "application/pdf");
        var never = Metadata(Guid.CreateVersion7(), Bytes(10), "a.png", "image/png");
        await device.Upload(mismatched.Id, uploaded, "application/pdf");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox),
            new CaptureRequest("Keynote", "phone", Attachments: [never, mismatched]),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal(SyncErrorCodes.CaptureAttachmentMissing, problem.Code);
        Assert.Contains(never.Id.ToString("D"), problem.Detail, StringComparison.Ordinal);
        Assert.Contains(mismatched.Id.ToString("D"), problem.Detail, StringComparison.Ordinal);
        Assert.Empty((await device.PullTasks()).Tasks);
    }

    /// <summary>Another owner's upload is not this owner's to name, however
    /// well the metadata matches.</summary>
    [Fact]
    public async Task A_capture_naming_another_owners_attachment_is_a_400()
    {
        var owner = await Device();
        var stranger = await _service.CreateClient().RegisteredDevice("Someone else's phone");
        var bytes = Bytes(40);
        var attachment = Metadata(Guid.CreateVersion7(), bytes, "a.png", "image/png");
        await owner.Upload(attachment.Id, bytes, "image/png");

        var response = await stranger.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox),
            new CaptureRequest("Keynote", "phone", Attachments: [attachment]),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.CaptureAttachmentMissing, (await Problem(response)).Code);
    }

    public static TheoryData<string, AttachmentMetadata> MalformedAttachments => new()
    {
        { "empty id", new AttachmentMetadata(Guid.Empty, "a.png", "image/png", 1, new string('a', 64)) },
        { "no name", new AttachmentMetadata(Guid.CreateVersion7(), " ", "image/png", 1, new string('a', 64)) },
        { "long name", new AttachmentMetadata(Guid.CreateVersion7(), new string('n', 256), "image/png", 1, new string('a', 64)) },
        { "no type", new AttachmentMetadata(Guid.CreateVersion7(), "a.png", "", 1, new string('a', 64)) },
        { "negative size", new AttachmentMetadata(Guid.CreateVersion7(), "a.png", "image/png", -1, new string('a', 64)) },
        { "short digest", new AttachmentMetadata(Guid.CreateVersion7(), "a.png", "image/png", 1, "abc") },
    };

    [Theory]
    [MemberData(nameof(MalformedAttachments))]
    public async Task A_malformed_attachment_on_a_capture_is_refused_at_the_edge(string because, AttachmentMetadata attachment)
    {
        var device = await Device();

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox),
            new CaptureRequest("Keynote", "phone", Attachments: [attachment]),
            Cancellation);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, because);
        Assert.Equal(SyncErrorCodes.CaptureInvalid, (await Problem(response)).Code);
    }

    /// <summary>The phone's own acknowledgement tombstones the capture and
    /// releases its files.</summary>
    [Fact]
    public async Task Acknowledging_a_capture_releases_its_attachments()
    {
        var device = await Device();
        var capture = await CaptureWithAttachment(device);

        var ack = await device.PostAsync(SyncRoutes.AcknowledgeInboxItemFor(capture.Id), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, ack.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync(SyncRoutes.AttachmentFor(capture.Attachments![0].Id), Cancellation)).StatusCode);
    }

    /// <summary>The desktop's acknowledgement is the same tombstone pushed
    /// through the task feed, and it releases the files just the same — read
    /// from the capture the service held, not from the tombstone, which here
    /// names no attachments at all.</summary>
    [Fact]
    public async Task A_pushed_capture_tombstone_releases_the_attachments_the_capture_named()
    {
        var phone = await Device();
        var capture = await CaptureWithAttachment(phone);
        var held = Assert.Single((await phone.PullTasks()).Tasks).Change;
        var later = held.UpdatedAt.AddMinutes(1);

        var push = await phone.PushChanges([held with { UpdatedAt = later, DeletedAt = later, Task = held.Task with { Attachments = null } }]);

        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync(SyncRoutes.AttachmentFor(capture.Attachments![0].Id), Cancellation)).StatusCode);
    }

    /// <summary>A tombstone the replica refuses as stale leaves the capture
    /// waiting, so its files stay too.</summary>
    [Fact]
    public async Task A_stale_pushed_tombstone_releases_nothing()
    {
        var phone = await Device();
        var capture = await CaptureWithAttachment(phone);
        var held = Assert.Single((await phone.PullTasks()).Tasks).Change;
        var earlier = held.UpdatedAt.AddMinutes(-1);

        await phone.PushChanges([held with { UpdatedAt = earlier, DeletedAt = earlier }]);

        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync(SyncRoutes.AttachmentFor(capture.Attachments![0].Id), Cancellation)).StatusCode);
    }

    /// <summary>The store failing to delete is the lifecycle rule's to clean
    /// up; the acknowledgement itself still succeeds and the capture leaves the
    /// inbox.</summary>
    [Fact]
    public async Task An_acknowledgement_succeeds_when_the_store_cannot_release()
    {
        var store = new ReleaseFailingAttachmentStore();
        using var service = new SyncServiceFactory
        {
            TestServices = services => services.Replace(ServiceDescriptor.Singleton<IAttachmentStore>(store)),
        };
        var device = await service.CreateClient().RegisteredDevice("Phone");
        var capture = await CaptureWithAttachment(device);

        var ack = await device.PostAsync(SyncRoutes.AcknowledgeInboxItemFor(capture.Id), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, ack.StatusCode);
        Assert.Equal(1, store.DeleteAttempts);
        Assert.Empty((await device.GetFromJsonAsync<List<InboxItem>>(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation))!);
    }

    private async Task<HttpClient> Device() => await _service.CreateClient().RegisteredDevice("Phone");

    private static async Task<InboxItem> CaptureWithAttachment(HttpClient device)
    {
        var bytes = Bytes(64);
        var attachment = Metadata(Guid.CreateVersion7(), bytes, "slide.png", "image/png");
        (await device.Upload(attachment.Id, bytes, "image/png")).EnsureSuccessStatusCode();

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox),
            new CaptureRequest("Keynote", "phone", Attachments: [attachment]),
            Cancellation);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!;
    }

    private static AttachmentMetadata Metadata(Guid id, byte[] bytes, string name, string contentType) =>
        new(id, name, contentType, bytes.Length, Sha256(bytes));

    private static byte[] Bytes(int length, int seed = 1)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    internal static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<ProblemBody> Problem(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))!;

    /// <summary>Every call fails the way an unreachable Azurite does.</summary>
    private sealed class UnreachableAttachmentStore : IAttachmentStore
    {
        public Task<StoredAttachment?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) => throw Unreachable();

        public Task<IStagedAttachment> Stage(OwnerId owner, Guid id, Stream content, CancellationToken cancellationToken = default) => throw Unreachable();

        public Task<AttachmentContent?> Open(OwnerId owner, Guid id, CancellationToken cancellationToken = default) => throw Unreachable();

        public Task Delete(OwnerId owner, Guid id, CancellationToken cancellationToken = default) => throw Unreachable();

        internal static SyncReplicaException Unreachable() =>
            new(SyncErrorCodes.AttachmentStoreUnavailable, "The attachment store is not reachable yet.");
    }

    /// <summary>Stores and serves like the real thing, and fails only the
    /// delete — the one call the acknowledgement makes.</summary>
    private sealed class ReleaseFailingAttachmentStore : IAttachmentStore
    {
        private readonly Adapters.InMemoryAttachmentStore _inner = new();

        internal int DeleteAttempts { get; private set; }

        public Task<StoredAttachment?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) => _inner.Find(owner, id, cancellationToken);

        public Task<IStagedAttachment> Stage(OwnerId owner, Guid id, Stream content, CancellationToken cancellationToken = default) => _inner.Stage(owner, id, content, cancellationToken);

        public Task<AttachmentContent?> Open(OwnerId owner, Guid id, CancellationToken cancellationToken = default) => _inner.Open(owner, id, cancellationToken);

        public Task Delete(OwnerId owner, Guid id, CancellationToken cancellationToken = default)
        {
            DeleteAttempts++;
            throw UnreachableAttachmentStore.Unreachable();
        }
    }
}

/// <summary>An upload in one line.</summary>
internal static class AttachmentClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <param name="sha256">The digest to declare; the bytes' own when
    /// null.</param>
    /// <param name="unsized">Send with no Content-Length, as a chunked body,
    /// so only reading it can tell how large it is.</param>
    internal static Task<HttpResponseMessage> Upload(
        this HttpClient client,
        Guid id,
        byte[] bytes,
        string contentType,
        string? sha256 = null,
        bool unsized = false)
    {
        HttpContent content = unsized ? new UnsizedContent(bytes) : new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        var request = new HttpRequestMessage(HttpMethod.Put, SyncRoutes.AttachmentFor(id)) { Content = content };
        request.Headers.TryAddWithoutValidation(SyncRoutes.AttachmentSha256Header, sha256 ?? AttachmentSyncEndpointTests.Sha256(bytes));

        return client.SendAsync(request, Cancellation);
    }

    private sealed class UnsizedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
