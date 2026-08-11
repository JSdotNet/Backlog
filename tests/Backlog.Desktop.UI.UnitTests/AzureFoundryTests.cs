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
            client.AskAsync(new AzureFoundryChatRequest("content", "question")));

        Assert.Contains("Configure Azure Foundry", ex.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Empty_question_and_content_are_rejected()
    {
        var client = BuildConfiguredClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        await Assert.ThrowsAsync<AzureFoundryException>(() => client.AskAsync(new AzureFoundryChatRequest("", "question")));
        await Assert.ThrowsAsync<AzureFoundryException>(() => client.AskAsync(new AzureFoundryChatRequest("content", "")));
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

        var response = await client.AskAsync(new AzureFoundryChatRequest("# Item", "What matters?"));

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
            client.AskAsync(new AzureFoundryChatRequest("content", "question")));

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

