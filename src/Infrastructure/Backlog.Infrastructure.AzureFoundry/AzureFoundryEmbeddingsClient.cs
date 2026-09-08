using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// What to embed, and with which deployment.
/// </summary>
/// <param name="Deployment">The embedding deployment's name — the one
/// <c>infra/foundry/main.bicep</c> creates behind <c>includeEmbeddingModel</c>,
/// which is <c>text-embedding-3-small</c>.
/// <para>
/// On the request rather than read from <see cref="AzureFoundrySettings"/>,
/// because that setting is the <i>chat</i> deployment: one account carries both,
/// and an embedding call that quietly used the chat deployment would fail with a
/// message about a model rather than about a configuration. The caller knows
/// which model its vectors are pinned to — see
/// <c>KnowledgeEmbeddingModel.Default</c> — and says so.
/// </para></param>
/// <param name="Inputs">The texts, in the order the vectors come back in.</param>
public sealed record AzureFoundryEmbeddingRequest(string Deployment, IReadOnlyList<string> Inputs);

/// <summary>
/// The vectors, and the model that actually produced them.
/// </summary>
/// <param name="Model">As the service reported it, not as it was asked for. This
/// is what a caller records beside the vectors, because a database is pinned to
/// whatever produced its rows and a deployment can be repointed at a different
/// model without its name changing.</param>
public sealed record AzureFoundryEmbeddingResponse(string Model, IReadOnlyList<float[]> Vectors);

/// <summary>
/// Turns text into vectors through an Azure AI Foundry embedding deployment.
///
/// <para><b>Nothing in this change calls it.</b> Local ADR 0004's semantic tier
/// is wired and dormant: the table exists, the port exists, the brute-force
/// cosine reader exists, the deployment is in bicep behind a parameter that is
/// off by default, and the feature flag is <c>Dev</c> and off. What is missing on
/// purpose is the thing that would join them up — and it is missing on purpose
/// because <b>the Node generator is the only writer</b>. If embeddings are ever
/// computed for the corpus, the generator computes them and writes them; this
/// client existing does not make the app a second writer of
/// <c>_meta/knowledge.db</c>, and nothing here opens that file at all.</para>
///
/// <para>The shape follows <see cref="AzureFoundryChatClient"/> deliberately —
/// same settings store, same <c>api-key</c> header, same api-version, same
/// failure type — so the two are configured by one screen and fail in one
/// vocabulary.</para>
/// </summary>
public interface IAzureFoundryEmbeddingsClient
{
    Task<AzureFoundryEmbeddingResponse> EmbedAsync(AzureFoundryEmbeddingRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAzureFoundryEmbeddingsClient"/>
public sealed class AzureFoundryEmbeddingsClient(HttpClient httpClient, AzureFoundrySettingsStore settingsStore) : IAzureFoundryEmbeddingsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AzureFoundryEmbeddingResponse> EmbedAsync(AzureFoundryEmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Deployment))
        {
            throw new AzureFoundryException("Name the embedding deployment to send this to.");
        }

        var inputs = request.Inputs?.Where(input => !string.IsNullOrWhiteSpace(input)).ToList() ?? [];
        if (inputs.Count == 0)
        {
            throw new AzureFoundryException("There is no text to embed.");
        }

        var settings = settingsStore.Current;

        // The chat deployment is not required for this call, so the configured
        // check is not IsConfigured: an account with an embedding deployment and
        // no chat one is a perfectly ordinary thing to have, and refusing it would
        // be this client inheriting a requirement that is not its own.
        if (string.IsNullOrWhiteSpace(settings.Endpoint) || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new AzureFoundryException("Configure Azure Foundry in Settings before embedding knowledge.");
        }

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new AzureFoundryException("The Azure Foundry endpoint in Settings is not a valid URL.");
        }

        var requestUri = new Uri(
            endpoint,
            $"/openai/deployments/{Uri.EscapeDataString(request.Deployment)}/embeddings?api-version={Uri.EscapeDataString(settings.ApiVersion)}");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new EmbeddingRequest(inputs), options: JsonOptions)
        };
        httpRequest.Headers.Add("api-key", settings.ApiKey);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AzureFoundryException($"Azure Foundry returned {(int)response.StatusCode}: {TrimForMessage(payload)}");
        }

        var embeddings = JsonSerializer.Deserialize<EmbeddingResponse>(payload, JsonOptions);
        var vectors = embeddings?.Data
            .OrderBy(item => item.Index)
            .Select(item => item.Embedding ?? [])
            .Where(vector => vector.Length > 0)
            .ToList() ?? [];

        if (vectors.Count != inputs.Count)
        {
            throw new AzureFoundryException("Azure Foundry returned a different number of vectors than there were texts.");
        }

        return new AzureFoundryEmbeddingResponse(embeddings?.Model ?? request.Deployment, vectors);
    }

    private static string TrimForMessage(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "...";
    }

    private sealed record EmbeddingRequest(IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingDatum> Data);

    private sealed record EmbeddingDatum(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}

/// <summary>What a host registers where the embedding deployment is not
/// available, matching <see cref="UnavailableAzureFoundryChatClient"/>: a caller
/// gets the same exception type it would get from a misconfiguration rather than
/// a missing service.</summary>
public sealed class UnavailableAzureFoundryEmbeddingsClient : IAzureFoundryEmbeddingsClient
{
    public Task<AzureFoundryEmbeddingResponse> EmbedAsync(AzureFoundryEmbeddingRequest request, CancellationToken cancellationToken = default) =>
        throw new AzureFoundryException("Azure Foundry embedding support is not registered in this build.");
}
