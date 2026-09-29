using Backlog.Infrastructure.BlobStorage.Extensions;
using Backlog.Modules.Sync.Extensions;
using Backlog.Modules.Sync.Ports;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backlog.Infrastructure.BlobStorage.UnitTests;

/// <summary>
/// What happens when nobody has configured the attachment container: nothing in
/// a development run, where the module's in-memory store stands, and a refusal
/// to start anywhere else, where the same silence would lose every upload on
/// the next restart (inherited ADR 0018).
/// </summary>
public class AttachmentStoreRegistrationTests
{
    /// <summary>A container nothing ever connects to. Registration reads the
    /// connection and registers a client factory; no call is made here.</summary>
    private const string UnreachableContainer =
        "Endpoint=https://unreachable.blob.core.windows.net/;ContainerName=attachments";

    [Fact]
    public void A_development_run_with_no_container_registers_nothing()
    {
        var builder = Builder(Environments.Development);

        builder.AddAttachmentBlobStore();

        Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(IAttachmentStore));
    }

    [Fact]
    public void Anywhere_but_development_a_missing_container_stops_the_start()
    {
        var builder = Builder(Environments.Production);

        var refusal = Assert.Throws<InvalidOperationException>(builder.AddAttachmentBlobStore);

        Assert.Contains(AttachmentStoreRegistration.ConnectionName, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_container_registers_the_blob_store_ahead_of_the_module()
    {
        var builder = Builder(Environments.Production);
        builder.Configuration[$"ConnectionStrings:{AttachmentStoreRegistration.ConnectionName}"] = UnreachableContainer;

        builder.AddAttachmentBlobStore();
        builder.Services.AddSyncModule();

        var store = Assert.Single(builder.Services, service => service.ServiceType == typeof(IAttachmentStore));
        Assert.Equal(typeof(BlobAttachmentStore), store.ImplementationType);
    }

    private static HostApplicationBuilder Builder(string environment) =>
        Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
}
