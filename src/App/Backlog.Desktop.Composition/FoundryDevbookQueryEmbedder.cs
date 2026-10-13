using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.Composition;

/// <summary>
/// <see cref="IDevbookQueryEmbedder"/> over the Azure AI Foundry embedding
/// deployment the devbook databases are pinned to
/// (<see cref="DevbookEmbeddingModel.Default"/>).
/// <para>
/// Here, in the composition, because it is the one place that knows both sides:
/// the Devbook module asks for a vector and must not learn about Foundry, and the
/// Foundry client must not learn what a devbook is. Every failure the client can
/// report — not configured, refused, unreachable — is the ordinary "no model"
/// answer, null, because search by meaning is additive and its absence is not an
/// error the reader has to read.
/// </para>
/// <para>
/// The client is asked for per question rather than held: it is a typed
/// <c>HttpClient</c> client, and one kept by this singleton would pin its handler
/// for the life of the app.
/// </para>
/// </summary>
internal sealed class FoundryDevbookQueryEmbedder(Func<IAzureFoundryEmbeddingsClient> client) : IDevbookQueryEmbedder
{
    public async Task<IReadOnlyList<float>?> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            var response = await client()
                .EmbedAsync(new AzureFoundryEmbeddingRequest(DevbookEmbeddingModel.Default, [text]), cancellationToken)
                .ConfigureAwait(false);

            return response.Vectors is [var vector, ..] && vector.Length > 0 ? vector : null;
        }
        catch (AzureFoundryException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
