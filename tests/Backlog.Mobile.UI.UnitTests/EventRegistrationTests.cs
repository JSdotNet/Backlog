using System.Net;

using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Events;
using Backlog.Mobile.UI.Outbox;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>What <c>AddDeviceOutbox</c> gives both phone hosts for knowledge events: the view
/// over the device file, the projection the Events screens read, the attach service the note
/// page calls, and the outbox kind that sends its requests.</summary>
public sealed class EventRegistrationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"event-registration-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void The_device_outbox_composes_the_event_view_the_attach_service_and_their_outbox_kind()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TestCloudSync.Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        services.AddSingleton<IDeviceCredentialStore>(TestDevices.Paired());
        services.AddDeviceOutbox(Path.Combine(_folder, "backlog-mobile.db"));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<SqliteEventViewStore>(provider.GetRequiredService<IEventViewStore>());
        Assert.Same(provider.GetRequiredService<EventViewProjection>(), provider.GetRequiredService<EventViewProjection>());
        Assert.NotNull(provider.GetRequiredService<EventNoteAttachments>());
        Assert.Contains(provider.GetServices<IOutboxKind>(), kind => kind.Kind == EventAttachmentRequestOutboxKind.Token);
    }
}
