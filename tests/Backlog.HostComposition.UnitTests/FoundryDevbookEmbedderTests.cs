using Backlog.Desktop.Composition;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The devbook's embedder over Foundry: no call without a configured deployment,
/// one call — not one per typing pause — to learn a deployment is not there, and
/// nothing recorded under the wrong model.
/// </summary>
public sealed class FoundryDevbookEmbedderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "backlog-foundry-embedder", Guid.NewGuid().ToString("N"));
    private readonly AzureFoundrySettingsStore _settings;
    private readonly RecordingClient _client = new();

    public FoundryDevbookEmbedderTests()
    {
        _settings = new AzureFoundrySettingsStore(Path.Combine(_folder, "azure-foundry.json"));
    }

    private FoundryDevbookEmbedder Embedder() => new(() => _client, _settings);

    private void Configure() =>
        _settings.SetConnection("https://example.invalid", deployment: null, apiKey: "key", apiVersion: null);

    [Fact]
    public async Task With_no_endpoint_or_key_nothing_is_called()
    {
        var embedder = Embedder();

        Assert.False(embedder.IsAvailable);
        Assert.False(((IDevbookQueryEmbedder)embedder).IsAvailable);
        Assert.Null(await embedder.EmbedAsync("a question", TestContext.Current.CancellationToken));
        Assert.Null(await embedder.EmbedAsync(["a chapter"], TestContext.Current.CancellationToken));
        Assert.Equal(0, _client.Calls);
    }

    [Fact]
    public async Task A_configured_account_embeds_with_the_pinned_deployment()
    {
        Configure();
        var embedder = Embedder();

        var vectors = await embedder.EmbedAsync(["one", "two"], TestContext.Current.CancellationToken);

        Assert.Equal(2, vectors!.Count);
        Assert.Equal(DevbookEmbeddingModel.Default, _client.LastDeployment);
        Assert.Equal("text-embedding-3-small", embedder.Model);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(401)]
    [InlineData(403)]
    public async Task A_deployment_that_is_not_there_is_asked_once_until_the_settings_change(int status)
    {
        Configure();
        var embedder = Embedder();
        _client.Fail = new AzureFoundryException("refused", status);

        Assert.Null(await embedder.EmbedAsync("first pause", TestContext.Current.CancellationToken));
        Assert.False(embedder.IsAvailable);
        Assert.Null(await embedder.EmbedAsync("second pause", TestContext.Current.CancellationToken));
        Assert.Equal(1, _client.Calls);

        _client.Fail = null;
        Configure();

        Assert.True(embedder.IsAvailable);
        Assert.NotNull(await embedder.EmbedAsync("after the fix", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_busy_service_is_asked_again()
    {
        Configure();
        var embedder = Embedder();
        _client.Fail = new AzureFoundryException("busy", 429);

        Assert.Null(await embedder.EmbedAsync("question", TestContext.Current.CancellationToken));

        Assert.True(embedder.IsAvailable);
    }

    [Fact]
    public async Task A_deployment_answering_with_another_model_is_refused_rather_than_mislabelled()
    {
        Configure();
        var embedder = Embedder();
        _client.Model = "text-embedding-3-large";

        Assert.Null(await embedder.EmbedAsync(["chapter"], TestContext.Current.CancellationToken));
        Assert.False(embedder.IsAvailable);
    }

    [Fact]
    public async Task A_versioned_model_name_is_the_same_model()
    {
        Configure();
        var embedder = Embedder();
        _client.Model = "text-embedding-3-small-1";

        Assert.NotNull(await embedder.EmbedAsync(["chapter"], TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless.
        }
    }

    private sealed class RecordingClient : IAzureFoundryEmbeddingsClient
    {
        public int Calls { get; private set; }

        public string? LastDeployment { get; private set; }

        public AzureFoundryException? Fail { get; set; }

        public string Model { get; set; } = "text-embedding-3-small";

        public Task<AzureFoundryEmbeddingResponse> EmbedAsync(AzureFoundryEmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastDeployment = request.Deployment;
            if (Fail is not null) throw Fail;

            return Task.FromResult(new AzureFoundryEmbeddingResponse(Model, [.. request.Inputs.Select(_ => new[] { 1f, 0f })]));
        }
    }
}
