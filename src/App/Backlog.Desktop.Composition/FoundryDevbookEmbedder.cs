using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.Composition;

/// <summary>
/// The devbook's two embedding ports over the Azure AI Foundry embedding
/// deployment the databases are pinned to (<see cref="DevbookEmbeddingModel.Default"/>):
/// <see cref="IDevbookQueryEmbedder"/> for the search panel's question and
/// <see cref="IDevbookChapterEmbedder"/> for the refresher's chapters, so both
/// sides embed with one model and stop asking together.
/// <para>
/// Here, in the composition, because it is the one place that knows both sides:
/// the Devbook must not learn about Foundry, and the Foundry client must not learn
/// what a devbook is.
/// </para>
/// <para>
/// <b>No call without a deployment.</b> <see cref="IsAvailable"/> is false while
/// the settings name no endpoint or key, and after the service answered that the
/// deployment is not there or the key is not accepted (404, 401, 403) — until the
/// settings change. A busy or unreachable service is asked again next time; a
/// missing deployment is asked once. Every failure reads as "no model": search by
/// meaning is additive, and its absence is not an error the reader has to read.
/// </para>
/// <para>
/// The client is asked for per call rather than held: it is a typed
/// <c>HttpClient</c> client, and one kept by this singleton would pin its handler
/// for the life of the app.
/// </para>
/// </summary>
public sealed class FoundryDevbookEmbedder : IDevbookQueryEmbedder, IDevbookChapterEmbedder
{
    private readonly Func<IAzureFoundryEmbeddingsClient> _client;
    private readonly AzureFoundrySettingsStore _settings;
    private volatile bool _refused;

    /// <summary>Bumped on every settings change, so an answer to a request sent
    /// under the old settings cannot refuse the new ones.</summary>
    private int _generation;

    public FoundryDevbookEmbedder(Func<IAzureFoundryEmbeddingsClient> client, AzureFoundrySettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);

        _client = client;
        _settings = settings;
        _settings.Changed += () =>
        {
            Interlocked.Increment(ref _generation);
            _refused = false;
        };
    }

    public string Model => DevbookEmbeddingModel.Default;

    public bool IsAvailable =>
        !_refused
        && !string.IsNullOrWhiteSpace(_settings.Current.Endpoint)
        && !string.IsNullOrWhiteSpace(_settings.Current.ApiKey);

    public async Task<IReadOnlyList<float>?> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        return await EmbedAsync([text], cancellationToken).ConfigureAwait(false) is [var vector, ..] ? vector : null;
    }

    public async Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (!IsAvailable || texts.Count == 0 || texts.Any(string.IsNullOrWhiteSpace)) return null;

        var generation = Volatile.Read(ref _generation);

        try
        {
            var response = await _client()
                .EmbedAsync(new AzureFoundryEmbeddingRequest(Model, texts), cancellationToken)
                .ConfigureAwait(false);

            // A deployment repointed at another model keeps its name; its vectors
            // would be recorded as this model's and compared across coordinate
            // spaces, which ranks nonsense. Refuse rather than mislabel.
            if (!IsThisModel(response.Model))
            {
                Refuse(generation);
                return null;
            }

            return response.Vectors.Count == texts.Count && response.Vectors.All(vector => vector.Length > 0)
                ? response.Vectors
                : null;
        }
        catch (AzureFoundryException exception)
        {
            if (exception.StatusCode is 401 or 403 or 404) Refuse(generation);
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout, not the caller's cancellation.
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private void Refuse(int generation)
    {
        if (Volatile.Read(ref _generation) == generation) _refused = true;
    }

    /// <summary>The service names the model it ran, sometimes with a version
    /// suffix (<c>text-embedding-3-small-1</c>); the deployment name is what an
    /// account without a model field answers with.</summary>
    private bool IsThisModel(string? reported) =>
        string.IsNullOrWhiteSpace(reported) || reported.StartsWith(Model, StringComparison.OrdinalIgnoreCase);
}
