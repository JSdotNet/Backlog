using System.Net;
using System.Text;
using System.Text.Json;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.AzureFoundry.TestService;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;
using Polly.Timeout;

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

    /// <summary>The resilience pipeline on the client reports its budget
    /// running out as Polly's own exception, not as the cancellation the
    /// callers were written for. It is the client's to translate: a slow
    /// answer is one of the ways a completion fails, and the callers already
    /// show every <see cref="AzureFoundryException"/> as a toast or a failed
    /// plan rather than letting it reach the error boundary.</summary>
    [Fact]
    public async Task A_pipeline_timeout_is_reported_as_a_foundry_failure()
    {
        var client = BuildConfiguredClient(new RecordingHandler(_ => throw new TimeoutRejectedException()));

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.AskAsync(new AzureFoundryChatRequest("content", "question"), TestContext.Current.CancellationToken));

        Assert.Equal("Azure Foundry did not answer before the request timed out.", ex.Message);
        Assert.IsType<TimeoutRejectedException>(ex.InnerException);
    }

    /// <summary>Same failure on the plan route: both calls complete through
    /// the one method, so a translation that covered only the question would
    /// leave the Inbox's plan button with the exception the page just lost.</summary>
    [Fact]
    public async Task A_pipeline_timeout_on_a_plan_request_is_reported_as_a_foundry_failure()
    {
        var client = BuildConfiguredClient(new RecordingHandler(_ =>
            throw new TimeoutRejectedException("The operation didn't complete within the allowed timeout of '00:00:30'.")));

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.DraftPlanAsync(new AzureFoundryPlanRequest("Fix the login page", "", null, "text", [], [], "fix-login"), TestContext.Current.CancellationToken));

        Assert.Equal("Azure Foundry did not answer before the request timed out.", ex.Message);
    }

    /// <summary>HttpClient's own timeout is a cancellation on a token the caller
    /// never cancelled. That is the same bad answer in a different exception.</summary>
    [Fact]
    public async Task The_clients_own_timeout_is_reported_as_a_foundry_failure_when_the_caller_did_not_cancel()
    {
        var client = BuildConfiguredClient(new RecordingHandler(_ =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.")));
        using var callerCancellation = new CancellationTokenSource();

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.AskAsync(new AzureFoundryChatRequest("content", "question"), callerCancellation.Token));

        Assert.Equal("Azure Foundry did not answer before the request timed out.", ex.Message);
    }

    /// <summary>The endpoint could not be reached at all: refused, unresolved,
    /// reset. HttpClient throws rather than answering, and the caller hears it
    /// in the same exception as every other refusal.</summary>
    [Fact]
    public async Task An_unreachable_endpoint_is_reported_as_a_foundry_failure()
    {
        var client = BuildConfiguredClient(new RecordingHandler(_ =>
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.")));

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.AskAsync(new AzureFoundryChatRequest("content", "question"), TestContext.Current.CancellationToken));

        Assert.Equal("Could not reach Azure Foundry: No connection could be made because the target machine actively refused it.", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    /// <summary>The caller's own cancellation is not dressed up as a failed
    /// answer: it propagates so whoever asked can tell they stopped it.</summary>
    [Fact]
    public async Task The_callers_cancellation_propagates_through_the_transport()
    {
        using var callerCancellation = new CancellationTokenSource();
        var client = BuildConfiguredClient(new RecordingHandler(_ =>
        {
            callerCancellation.Cancel();
            throw new OperationCanceledException(callerCancellation.Token);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.AskAsync(new AzureFoundryChatRequest("content", "question"), callerCancellation.Token));
    }

    /// <summary>The plan request travels the same route with the same header as
    /// a question, and the two messages are the prompt's: the system message
    /// opens with the marker the harness keys on, and the user message carries
    /// the item under the labels the harness reads back.</summary>
    [Fact]
    public async Task A_plan_request_posts_the_plan_prompt_and_the_item_as_labelled_sections()
    {
        var handler = new RecordingHandler(_ => Completion("# Step one\n`prompt` `!ready` `#fix-login` `id:step-1` `effort:2`\n\nDo it."));
        var client = BuildConfiguredClient(handler);

        var response = await client.DraftPlanAsync(PlanRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("# Step one\n`prompt` `!ready` `#fix-login` `id:step-1` `effort:2`\n\nDo it.", response.PlanMarkdown);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://foundry.example.com/openai/deployments/chat/chat/completions?api-version=2024-10-21", handler.Request.RequestUri!.ToString());
        Assert.Equal("secret", Assert.Single(handler.Request.Headers.GetValues("api-key")));

        using var document = JsonDocument.Parse(handler.Body!);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());

        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        var system = messages[0].GetProperty("content").GetString()!;
        Assert.StartsWith(AzureFoundryPlanPrompt.Marker, system, StringComparison.Ordinal);
        Assert.Equal(AzureFoundryPlanPrompt.Text, system);

        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        var user = messages[1].GetProperty("content").GetString()!;
        Assert.Contains("Item: Fix the login page", user, StringComparison.Ordinal);
        Assert.Contains("Kind: article", user, StringComparison.Ordinal);
        Assert.Contains("Source: https://example.com/login-bug", user, StringComparison.Ordinal);
        Assert.Contains("Tags: auth, ui", user, StringComparison.Ordinal);
        Assert.Contains("Repositories:\nacme/web\nacme/api\n", user, StringComparison.Ordinal);
        Assert.Contains("Plan tag: fix-login", user, StringComparison.Ordinal);
        Assert.EndsWith("Content:\nThe login page 500s on submit.", user, StringComparison.Ordinal);
    }

    /// <summary>An item with nothing to say still gets a well-formed message:
    /// the empty lists are written as "(none)" rather than as a label with
    /// nothing after it, and the source line is left out rather than left
    /// blank.</summary>
    [Fact]
    public async Task A_plan_request_with_no_source_tags_or_repositories_says_so()
    {
        var handler = new RecordingHandler(_ => Completion("# Step one\n`prompt` `!ready` `#note` `id:step-1` `effort:2`\n\nDo it."));
        var client = BuildConfiguredClient(handler);

        await client.DraftPlanAsync(new AzureFoundryPlanRequest("A note", "", null, "text", [], [], "note"), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(handler.Body!);
        var user = document.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.DoesNotContain("Source:", user, StringComparison.Ordinal);
        Assert.Contains("Tags: (none)", user, StringComparison.Ordinal);
        Assert.Contains("Repositories: (none)", user, StringComparison.Ordinal);
        Assert.EndsWith("Content:\n", user, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("```markdown\n# Step one\n`prompt` `!ready` `#fix-login` `id:step-1` `effort:2`\n\nDo it.\n```")]
    [InlineData("```\n# Step one\n`prompt` `!ready` `#fix-login` `id:step-1` `effort:2`\n\nDo it.\n```")]
    [InlineData("  ```md\r\n# Step one\r\n`prompt` `!ready` `#fix-login` `id:step-1` `effort:2`\r\n\r\nDo it.\r\n```  ")]
    public async Task A_fence_wrapped_around_the_whole_plan_is_taken_off(string fenced)
    {
        var client = BuildConfiguredClient(new RecordingHandler(_ => Completion(fenced)));

        var response = await client.DraftPlanAsync(PlanRequest(), TestContext.Current.CancellationToken);

        Assert.StartsWith("# Step one", response.PlanMarkdown, StringComparison.Ordinal);
        Assert.EndsWith("Do it.", response.PlanMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("```", response.PlanMarkdown, StringComparison.Ordinal);
    }

    /// <summary>A fence that is part of an entry — a code block in a body — is
    /// not a wrapper, and an answer that only opens one is left as it came so
    /// the import can say what it makes of it.</summary>
    [Fact]
    public async Task A_fence_inside_the_plan_is_left_alone()
    {
        const string plan = "# Step one\n`prompt` `!ready` `#fix-login` `id:step-1` `effort:2`\n\n```bash\ndotnet build\n```\n\n# Step two\n`prompt` `!ready` `#fix-login` `id:step-2` `after:step-1` `effort:2`\n\nThen this.";
        var client = BuildConfiguredClient(new RecordingHandler(_ => Completion(plan)));

        var response = await client.DraftPlanAsync(PlanRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(plan, response.PlanMarkdown);
    }

    [Fact]
    public async Task A_plan_request_without_settings_is_refused_before_any_http_request()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new AzureFoundryChatClient(new HttpClient(handler), new AzureFoundrySettingsStore(NewSettingsPath()));

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.DraftPlanAsync(PlanRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("Configure Azure Foundry in Settings to create plans.", ex.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task A_plan_request_with_no_title_or_tag_is_refused_before_any_http_request()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = BuildConfiguredClient(handler);

        await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.DraftPlanAsync(PlanRequest() with { ItemTitle = " " }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.DraftPlanAsync(PlanRequest() with { PlanTag = "" }, TestContext.Current.CancellationToken));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task A_plan_request_that_fails_upstream_reports_the_status()
    {
        var client = BuildConfiguredClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("busy")
        }));

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.DraftPlanAsync(PlanRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("Azure Foundry returned 503: busy", ex.Message);
    }

    [Theory]
    [InlineData("""{ "choices": [ { "message": { "content": "" } } ] }""")]
    [InlineData("""{ "choices": [ { "message": { "content": "   " } } ] }""")]
    [InlineData("""{ "choices": [] }""")]
    [InlineData("""{ "choices": [ { "message": { "content": "```markdown\n```" } } ] }""")]
    public async Task An_empty_plan_is_refused(string payload)
    {
        var client = BuildConfiguredClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }));

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            client.DraftPlanAsync(PlanRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("empty answer", ex.Message);
    }

    [Fact]
    public async Task The_unavailable_client_refuses_a_plan_the_way_it_refuses_a_question()
    {
        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() =>
            new UnavailableAzureFoundryChatClient().DraftPlanAsync(PlanRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("not registered", ex.Message);
    }

    private static AzureFoundryPlanRequest PlanRequest() => new(
        "Fix the login page",
        "The login page 500s on submit.",
        "https://example.com/login-bug",
        "article",
        ["auth", "ui"],
        ["acme/web", "acme/api"],
        "fix-login");

    private static HttpResponseMessage Completion(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }), Encoding.UTF8, "application/json")
    };

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

/// <summary>
/// The adapter between the Inbox's plan-drafter port and the Foundry client.
/// Two translations and one property: the DTO becomes the client's request,
/// a thrown <see cref="AzureFoundryException"/> becomes the Inbox's
/// <c>inbox.plan.failed</c>, and availability is read off the settings store
/// so "Create plan" can be disabled with a reason rather than fail on click.
/// </summary>
public sealed class AzureFoundryInboxPlanDrafterTests : IDisposable
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
    public void Unconfigured_settings_read_as_unavailable_with_a_reason_that_points_at_settings()
    {
        var drafter = new AzureFoundryInboxPlanDrafter(new StubChatClient(), new AzureFoundrySettingsStore(NewSettingsPath()));

        Assert.False(drafter.IsAvailable);
        Assert.Equal("Configure Azure Foundry in Settings to create plans.", drafter.UnavailableReason);
    }

    /// <summary>Read live, not captured at construction: a key entered in
    /// Settings enables the control without a restart.</summary>
    [Fact]
    public void Configuring_settings_makes_the_drafter_available_without_a_new_instance()
    {
        var settings = new AzureFoundrySettingsStore(NewSettingsPath());
        var drafter = new AzureFoundryInboxPlanDrafter(new StubChatClient(), settings);
        Assert.False(drafter.IsAvailable);

        settings.SetConnection("https://foundry.example.com", "chat", "secret", "2024-10-21");

        Assert.True(drafter.IsAvailable);
        Assert.Null(drafter.UnavailableReason);
    }

    [Fact]
    public async Task The_item_is_handed_to_the_client_fact_for_fact_and_the_plan_comes_back()
    {
        var chat = new StubChatClient { Plan = "# Step one\n`prompt` `!ready` `#fix-login` `id:step-1` `effort:2`\n\nDo it." };
        var drafter = new AzureFoundryInboxPlanDrafter(chat, ConfiguredSettings());
        var itemId = Guid.NewGuid();

        var result = await drafter.DraftAsync(
            new InboxPlanDraftRequestDto(itemId, "Fix the login page", "The login page 500s.", "https://example.com/bug", "article", ["auth"], ["acme/web"], "fix-login"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(chat.Plan, result.Value.PlanMarkdown);

        var request = Assert.Single(chat.Requests);
        Assert.Equal("Fix the login page", request.ItemTitle);
        Assert.Equal("The login page 500s.", request.ItemContent);
        Assert.Equal("https://example.com/bug", request.SourceUrl);
        Assert.Equal("article", request.KindSlug);
        Assert.Equal(["auth"], request.Tags);
        Assert.Equal(["acme/web"], request.Repositories);
        Assert.Equal("fix-login", request.PlanTag);
    }

    [Fact]
    public async Task A_client_failure_comes_back_as_a_failed_result_carrying_the_clients_words()
    {
        var chat = new StubChatClient { Failure = new AzureFoundryException("Azure Foundry returned 503: busy") };
        var drafter = new AzureFoundryInboxPlanDrafter(chat, ConfiguredSettings());

        var result = await drafter.DraftAsync(
            new InboxPlanDraftRequestDto(Guid.NewGuid(), "Fix the login page", "", null, "text", [], [], "fix-login"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.plan.failed", result.Error.Code);
        Assert.Equal("Azure Foundry returned 503: busy", result.Error.Message);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    /// <summary>A refused or unresolved endpoint is what HttpClient throws
    /// rather than answers; the pane shows it like any other failed plan
    /// instead of losing the circuit.</summary>
    [Fact]
    public async Task An_unreachable_endpoint_comes_back_as_a_failed_result_naming_the_transport_error()
    {
        var chat = new StubChatClient { Failure = new HttpRequestException("No connection could be made because the target machine actively refused it.") };
        var drafter = new AzureFoundryInboxPlanDrafter(chat, ConfiguredSettings());

        var result = await drafter.DraftAsync(
            new InboxPlanDraftRequestDto(Guid.NewGuid(), "Fix the login page", "", null, "text", [], [], "fix-login"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.plan.failed", result.Error.Code);
        Assert.Equal("Could not reach Azure Foundry: No connection could be made because the target machine actively refused it.", result.Error.Message);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    /// <summary>HttpClient reports its own timeout as a cancellation on a
    /// token the caller never cancelled. That is a bad answer, not a request
    /// to stop.</summary>
    [Fact]
    public async Task A_timeout_comes_back_as_a_failed_result_when_the_caller_did_not_cancel()
    {
        var chat = new StubChatClient { Failure = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.") };
        var drafter = new AzureFoundryInboxPlanDrafter(chat, ConfiguredSettings());
        using var callerCancellation = new CancellationTokenSource();

        var result = await drafter.DraftAsync(
            new InboxPlanDraftRequestDto(Guid.NewGuid(), "Fix the login page", "", null, "text", [], [], "fix-login"),
            callerCancellation.Token);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.plan.failed", result.Error.Code);
        Assert.Equal("Azure Foundry did not answer before the request timed out.", result.Error.Message);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    /// <summary>The caller's own cancellation is not dressed up as a failed
    /// plan: it propagates so whoever asked can tell they stopped it.</summary>
    [Fact]
    public async Task The_callers_cancellation_propagates()
    {
        using var callerCancellation = new CancellationTokenSource();
        await callerCancellation.CancelAsync();
        var chat = new StubChatClient { Failure = new OperationCanceledException(callerCancellation.Token) };
        var drafter = new AzureFoundryInboxPlanDrafter(chat, ConfiguredSettings());

        await Assert.ThrowsAsync<OperationCanceledException>(() => drafter.DraftAsync(
            new InboxPlanDraftRequestDto(Guid.NewGuid(), "Fix the login page", "", null, "text", [], [], "fix-login"),
            callerCancellation.Token));
    }

    /// <summary>Only the client's own exception and the transport's are bad
    /// answers. Anything else is a bug and is not dressed up as one.</summary>
    [Fact]
    public async Task Any_other_exception_is_left_to_surface()
    {
        var chat = new StubChatClient { Failure = new InvalidOperationException("boom") };
        var drafter = new AzureFoundryInboxPlanDrafter(chat, ConfiguredSettings());

        await Assert.ThrowsAsync<InvalidOperationException>(() => drafter.DraftAsync(
            new InboxPlanDraftRequestDto(Guid.NewGuid(), "Fix the login page", "", null, "text", [], [], "fix-login"),
            TestContext.Current.CancellationToken));
    }

    private AzureFoundrySettingsStore ConfiguredSettings()
    {
        var settings = new AzureFoundrySettingsStore(NewSettingsPath());
        settings.SetConnection("https://foundry.example.com", "chat", "secret", "2024-10-21");
        return settings;
    }

    private string NewSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-foundry-drafter", Guid.NewGuid().ToString("n"), "azure-foundry.json");
        _paths.Add(path);
        return path;
    }

    private sealed class StubChatClient : IAzureFoundryChatClient
    {
        public string Plan { get; init; } = "# Step one\n`prompt` `!ready` `#plan` `id:step-1` `effort:2`\n\nDo it.";

        public Exception? Failure { get; init; }

        public List<AzureFoundryPlanRequest> Requests { get; } = [];

        public Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The drafter never asks a question.");

        public Task<AzureFoundryPlanResponse> DraftPlanAsync(AzureFoundryPlanRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Failure is not null) throw Failure;
            return Task.FromResult(new AzureFoundryPlanResponse(Plan));
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
/// generator is the only thing that writes <c>_meta/devbook.db</c> — this client
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

