using System.Net;
using System.Text;
using System.Text.Json;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.AzureFoundry.TestService;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class AzureFoundrySettingsStoreTests : IDisposable
{
    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is null) continue;

            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Settings_are_normalized_and_survive_restart()
    {
        var path = NewSettingsPath();
        var store = new AzureFoundrySettingsStore(path);

        var error = store.SetConnection(" https://foundry.example.com/ ", " chat ", " secret ", " 2024-10-21 ");
        var restarted = new AzureFoundrySettingsStore(path);

        Assert.Null(error);
        Assert.Equal("https://foundry.example.com", restarted.Current.Endpoint);
        Assert.Equal("chat", restarted.Current.Deployment);
        Assert.Equal("secret", restarted.Current.ApiKey);
        Assert.Equal("2024-10-21", restarted.Current.ApiVersion);
        Assert.True(restarted.Current.IsConfigured);
    }

    [Fact]
    public void Connection_updates_keep_existing_api_key_unless_replaced()
    {
        var store = new AzureFoundrySettingsStore(NewSettingsPath());
        store.SetConnection("https://foundry.example.com", "chat", "secret", null);

        store.SetConnection("https://next.example.com", "next", null, "2024-10-21");

        Assert.Equal("https://next.example.com", store.Current.Endpoint);
        Assert.Equal("next", store.Current.Deployment);
        Assert.Equal("secret", store.Current.ApiKey);
    }

    [Fact]
    public void Api_key_can_be_forgotten()
    {
        var store = new AzureFoundrySettingsStore(NewSettingsPath());
        store.SetConnection("https://foundry.example.com", "chat", "secret", null);

        store.ClearApiKey();

        Assert.Null(store.Current.ApiKey);
        Assert.False(store.Current.IsConfigured);
    }

    private string NewSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-foundry-settings", Guid.NewGuid().ToString("n"), "azure-foundry.json");
        _paths.Add(path);
        return path;
    }
}

public sealed class AzureFoundryChatClientTests : IDisposable
{
    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is null) continue;

            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Missing_settings_are_reported_before_http_request()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new AzureFoundryChatClient(new HttpClient(handler), new AzureFoundrySettingsStore(NewSettingsPath()));

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.AskAsync(new AzureFoundryChatRequest("content", "question"), TestContext.Current.CancellationToken));

        Assert.Contains("Configure Azure Foundry", ex.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Empty_question_and_content_are_rejected()
    {
        var client = BuildConfiguredClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        await Assert.ThrowsAsync<AzureFoundryException>(() => client.AskAsync(new AzureFoundryChatRequest("", "question"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<AzureFoundryException>(() => client.AskAsync(new AzureFoundryChatRequest("content", ""), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sends_chat_completion_request_to_configured_deployment()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "choices": [
                    { "message": { "content": "Use the first backlog item." } }
                  ]
                }
                """, Encoding.UTF8, "application/json")
        });
        var client = BuildConfiguredClient(handler);

        var response = await client.AskAsync(new AzureFoundryChatRequest("# Item", "What matters?"), TestContext.Current.CancellationToken);

        Assert.Equal("Use the first backlog item.", response.Answer);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://foundry.example.com/openai/deployments/chat/chat/completions?api-version=2024-10-21", handler.Request.RequestUri!.ToString());
        Assert.Equal("secret", Assert.Single(handler.Request.Headers.GetValues("api-key")));

        using var document = JsonDocument.Parse(handler.Body!);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Contains("# Item", messages[1].GetProperty("content").GetString());
        Assert.Contains("What matters?", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Http_errors_include_status_code_and_trimmed_body()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(new string('x', 400))
        });
        var client = BuildConfiguredClient(handler);

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.AskAsync(new AzureFoundryChatRequest("content", "question"), TestContext.Current.CancellationToken));

        Assert.Contains("Azure Foundry returned 400", ex.Message);
        Assert.EndsWith("...", ex.Message);
    }

    private AzureFoundryChatClient BuildConfiguredClient(RecordingHandler handler)
    {
        var settings = new AzureFoundrySettingsStore(NewSettingsPath());
        settings.SetConnection("https://foundry.example.com", "chat", "secret", "2024-10-21");
        return new AzureFoundryChatClient(new HttpClient(handler), settings);
    }

    private string NewSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-foundry-client", Guid.NewGuid().ToString("n"), "azure-foundry.json");
        _paths.Add(path);
        return path;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}

public sealed class AzureFoundryLocalTestServiceTests
{
    [Fact]
    public void Creates_deterministic_answer_from_chat_prompt()
    {
        var answer = LocalAzureFoundryCompletion.CreateAnswer(
        [
            new AzureFoundryChatMessage("system", "test"),
            new AzureFoundryChatMessage("user", "Content:\n# Important backlog item\n\nQuestion:\nWhat matters?")
        ]);

        Assert.Contains("What matters?", answer);
        Assert.Contains("Important backlog item", answer);
        Assert.StartsWith("Local Azure Foundry test response:", answer);
    }

    [Fact]
    public void Handles_missing_question_without_throwing()
    {
        var answer = LocalAzureFoundryCompletion.CreateAnswer([]);

        Assert.Contains("no question", answer);
    }
}

/// <summary>
/// The embedding client behind local ADR 0004's semantic tier.
///
/// <para><b>Nothing in the product calls it, and that is the first thing these
/// pin.</b> The tier is wired and dormant: the table exists, the port exists, the
/// cosine reader exists, the deployment sits in bicep behind a parameter that is
/// off, and the feature flag is <c>Dev</c> and off. What is deliberately missing
/// is the step that would compute vectors for the corpus, because the Node
/// generator is the only thing that writes <c>_meta/knowledge.db</c> — this client
/// existing must not turn the app into a second writer, and nothing here opens
/// that file at all.</para>
///
/// <para>So what is tested is the shape and the guards: the request goes to the
/// <em>embedding</em> deployment rather than the chat one, and every refusal
/// happens before a request is sent.</para>
/// </summary>
public sealed class AzureFoundryEmbeddingsClientTests : IDisposable
{
    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is null) continue;

            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Missing_settings_are_reported_before_any_http_request()
    {
        var handler = new CountingHandler();
        var client = new AzureFoundryEmbeddingsClient(new HttpClient(handler), new AzureFoundrySettingsStore(NewSettingsPath()));

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.EmbedAsync(new AzureFoundryEmbeddingRequest("text-embedding-3-small", ["a chapter"]), TestContext.Current.CancellationToken));

        Assert.Contains("Configure Azure Foundry", ex.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// An account with an embedding deployment and no chat one is ordinary, so
    /// this client does not inherit the chat client's completeness check. Asserted
    /// because the easy mistake is to reuse <c>IsConfigured</c> and refuse a
    /// perfectly usable configuration.
    /// </summary>
    [Fact]
    public async Task A_configuration_with_no_chat_deployment_is_still_usable()
    {
        var handler = new CountingHandler(_ => Json("""
            { "model": "text-embedding-3-small", "data": [ { "index": 0, "embedding": [0.5, -0.25] } ] }
            """));
        var settings = new AzureFoundrySettingsStore(NewSettingsPath());
        settings.SetConnection("https://foundry.example.com", deployment: null, "secret", "2024-10-21");

        var response = await new AzureFoundryEmbeddingsClient(new HttpClient(handler), settings)
            .EmbedAsync(new AzureFoundryEmbeddingRequest("text-embedding-3-small", ["a chapter"]), TestContext.Current.CancellationToken);

        Assert.Equal("text-embedding-3-small", response.Model);
        Assert.Equal([0.5f, -0.25f], Assert.Single(response.Vectors));
    }

    /// <summary>
    /// The deployment comes from the request, not from the settings. That
    /// separation is the point: <c>AzureFoundrySettings.Deployment</c> is the chat
    /// deployment, and an embedding call that quietly used it would fail with a
    /// message about a model rather than about a configuration.
    /// </summary>
    [Fact]
    public async Task The_request_goes_to_the_embedding_deployment_and_not_the_chat_one()
    {
        var handler = new CountingHandler(_ => Json("""
            { "model": "text-embedding-3-small", "data": [ { "index": 0, "embedding": [1.0] } ] }
            """));

        await BuildClient(handler).EmbedAsync(
            new AzureFoundryEmbeddingRequest("text-embedding-3-small", ["a chapter"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://foundry.example.com/openai/deployments/text-embedding-3-small/embeddings?api-version=2024-10-21",
            handler.Request!.RequestUri!.ToString());
        Assert.Equal("secret", Assert.Single(handler.Request.Headers.GetValues("api-key")));
    }

    [Fact]
    public async Task Nothing_to_embed_is_refused_before_any_http_request()
    {
        var handler = new CountingHandler();
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.EmbedAsync(new AzureFoundryEmbeddingRequest("text-embedding-3-small", []), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.EmbedAsync(new AzureFoundryEmbeddingRequest("text-embedding-3-small", ["   "]), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.EmbedAsync(new AzureFoundryEmbeddingRequest(" ", ["a chapter"]), TestContext.Current.CancellationToken));

        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>A short answer is not a partial answer: pairing vectors to inputs
    /// by position only works if there are as many of one as the other, and
    /// guessing which chapter was skipped would file a vector against the wrong
    /// one.</summary>
    [Fact]
    public async Task Fewer_vectors_than_texts_is_refused_rather_than_paired_up()
    {
        var handler = new CountingHandler(_ => Json("""
            { "model": "text-embedding-3-small", "data": [ { "index": 0, "embedding": [1.0] } ] }
            """));

        await Assert.ThrowsAsync<AzureFoundryException>(() => BuildClient(handler).EmbedAsync(
            new AzureFoundryEmbeddingRequest("text-embedding-3-small", ["one", "two"]),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_unavailable_client_says_so_rather_than_pretending()
    {
        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            new UnavailableAzureFoundryEmbeddingsClient().EmbedAsync(
                new AzureFoundryEmbeddingRequest("text-embedding-3-small", ["a chapter"]),
                TestContext.Current.CancellationToken));

        Assert.Contains("not registered", ex.Message);
    }

    private static HttpResponseMessage Json(string payload) =>
        new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private AzureFoundryEmbeddingsClient BuildClient(CountingHandler handler)
    {
        var settings = new AzureFoundrySettingsStore(NewSettingsPath());
        settings.SetConnection("https://foundry.example.com", "chat", "secret", "2024-10-21");
        return new AzureFoundryEmbeddingsClient(new HttpClient(handler), settings);
    }

    private string NewSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-foundry-embeddings", Guid.NewGuid().ToString("n"), "azure-foundry.json");
        _paths.Add(path);
        return path;
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;

            // No responder means the test expects no call at all, and a
            // NotImplementedException here names that louder than a count
            // assertion after the fact.
            return Task.FromResult(respond is null ? throw new NotImplementedException("No HTTP call was expected.") : respond(request));
        }
    }
}

