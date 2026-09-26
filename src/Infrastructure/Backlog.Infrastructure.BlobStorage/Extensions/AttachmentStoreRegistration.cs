using Backlog.Modules.Sync.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backlog.Infrastructure.BlobStorage.Extensions;

/// <summary>
/// Wires the blob-backed attachment store into a host — or, in Development with
/// nothing configured, deliberately does nothing, exactly as
/// <c>AddCosmosReplicas</c> beside it does.
/// </summary>
public static class AttachmentStoreRegistration
{
    /// <summary>
    /// The Aspire resource whose connection names the container. It is the
    /// container rather than the account, which is what
    /// <c>WithReference(attachments)</c> in the AppHost publishes — locally
    /// Azurite's connection string, deployed
    /// <c>Endpoint=&lt;blob endpoint&gt;;ContainerName=attachments</c>, reached with
    /// the managed identity.
    /// </summary>
    public const string ConnectionName = "attachments";

    /// <summary>
    /// Registers the container client and the store that writes to it.
    /// <para>
    /// <strong>In Development it no-ops when the connection is missing</strong>,
    /// so the module's in-memory <c>TryAddSingleton</c> stands and a bare
    /// <c>dotnet run</c> or an endpoint test uploads with no Azurite.
    /// <strong>Anywhere else that silence is a defect, so it throws</strong>: a
    /// deployed service holding attachments in process memory would accept every
    /// upload and lose them all on the next scale-in, and nothing would look
    /// wrong until a desktop failed to fetch one (inherited ADR 0018).
    /// </para>
    /// <para>
    /// Call it before <c>AddSyncModule()</c>; the module's registration is a
    /// <c>TryAdd</c>, so whichever runs first wins.
    /// </para>
    /// </summary>
    public static TBuilder AddAttachmentBlobStore<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(ConnectionName)))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"The attachment store is not configured. Set the '{ConnectionName}' connection string. Only a "
                    + "Development run may fall back to the in-memory store, which loses every upload when the process ends.");
            }

            return builder;
        }

        builder.AddAzureBlobContainerClient(ConnectionName);

        // Add, not TryAdd: this is the concrete registration the in-memory
        // stand-in steps aside for.
        builder.Services.AddSingleton<IAttachmentStore, BlobAttachmentStore>();

        return builder;
    }
}
