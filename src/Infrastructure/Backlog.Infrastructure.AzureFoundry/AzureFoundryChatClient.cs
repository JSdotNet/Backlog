using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly.Timeout;

namespace Backlog.Infrastructure.AzureFoundry;

public sealed record AzureFoundryChatRequest(string Content, string Question);

public sealed record AzureFoundryChatResponse(string Answer);

/// <summary>What the model is given to draft an import plan from: an inbox
/// item's facts, the repositories it may name, and the tag every entry of the
/// plan must carry. The words are the model's (<c>Repositories</c>, not
/// <c>RepoIds</c>): this record is the request as the prompt phrases it, and
/// the Inbox's DTO is mapped onto it by the adapter that knows both.</summary>
public sealed record AzureFoundryPlanRequest(
    string ItemTitle,
    string ItemContent,
    string? SourceUrl,
    string KindSlug,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Repositories,
    string PlanTag);

/// <summary>The drafted plan as entry text, trimmed and unfenced — whatever the
/// model wrapped it in, what comes back is what Tasks' import can read.</summary>
public sealed record AzureFoundryPlanResponse(string PlanMarkdown);

public interface IAzureFoundryChatClient
{
    Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>Asks for an import plan about one item. Same route, same
    /// headers and the same failures as <see cref="AskAsync"/>; only the two
    /// messages differ — see <see cref="AzureFoundryPlanPrompt"/>.</summary>
    Task<AzureFoundryPlanResponse> DraftPlanAsync(AzureFoundryPlanRequest request, CancellationToken cancellationToken = default);
}

public sealed class AzureFoundryChatClient(HttpClient httpClient, AzureFoundrySettingsStore settingsStore) : IAzureFoundryChatClient
{
    private const string SystemPrompt = "You answer questions about the supplied Backlog content. Use only the supplied content. If the content does not contain the answer, say you do not know from the content.";

    /// <summary>The sentence a plan request fails with when nothing is
    /// configured. Shared with <see cref="AzureFoundryInboxPlanDrafter"/>, which
    /// shows it as the disabled control's reason, so the two cannot disagree.</summary>
    internal const string PlanNotConfiguredMessage = "Configure Azure Foundry in Settings to create plans.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new AzureFoundryException("There is no content for AI to answer from.");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new AzureFoundryException("Ask a question before sending it to AI.");
        }

        var answer = await CompleteAsync(
            "Configure Azure Foundry in Settings before asking AI questions.",
            [
                new ChatMessage("system", SystemPrompt),
                new ChatMessage("user", $"Content:\n{request.Content.Trim()}\n\nQuestion:\n{request.Question.Trim()}")
            ],
            cancellationToken).ConfigureAwait(false);

        return new AzureFoundryChatResponse(answer);
    }

    public async Task<AzureFoundryPlanResponse> DraftPlanAsync(AzureFoundryPlanRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ItemTitle))
        {
            throw new AzureFoundryException("There is no item for AI to plan from.");
        }

        if (string.IsNullOrWhiteSpace(request.PlanTag))
        {
            throw new AzureFoundryException("A plan needs a tag before AI can write it.");
        }

        var answer = await CompleteAsync(
            PlanNotConfiguredMessage,
            [
                new ChatMessage("system", AzureFoundryPlanPrompt.Text),
                new ChatMessage("user", AzureFoundryPlanPrompt.User(request))
            ],
            cancellationToken).ConfigureAwait(false);

        var plan = StripFence(answer);
        if (plan.Length == 0)
        {
            // A fence with nothing inside it passed the empty check above on the
            // strength of its own backticks.
            throw new AzureFoundryException("Azure Foundry returned an empty answer.");
        }

        return new AzureFoundryPlanResponse(plan);
    }

    /// <summary>One chat completion, start to finish: the configuration checks,
    /// the deployment route, the key header, and the three ways an answer can
    /// fail to be one. Both calls go through here so they cannot drift on any of
    /// it; what they say differs only in the messages and in which Settings
    /// sentence a missing configuration points at.</summary>
    private async Task<string> CompleteAsync(string notConfiguredMessage, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var settings = settingsStore.Current;
        if (!settings.IsConfigured)
        {
            throw new AzureFoundryException(notConfiguredMessage);
        }

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new AzureFoundryException("The Azure Foundry endpoint in Settings is not a valid URL.");
        }

        var requestUri = new Uri(endpoint, $"/openai/deployments/{Uri.EscapeDataString(settings.Deployment!)}/chat/completions?api-version={Uri.EscapeDataString(settings.ApiVersion)}");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new ChatCompletionRequest(messages), options: JsonOptions)
        };
        httpRequest.Headers.Add("api-key", settings.ApiKey);

        using var response = await SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AzureFoundryException($"Azure Foundry returned {(int)response.StatusCode}: {TrimForMessage(payload)}");
        }

        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(payload, JsonOptions);
        var answer = completion?.Choices.FirstOrDefault()?.Message.Content;
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new AzureFoundryException("Azure Foundry returned an empty answer.");
        }

        return answer.Trim();
    }

    /// <summary>The send, with the one failure the pipeline throws in its own
    /// words translated. Its budget (<see cref="AzureFoundryRegistration"/>
    /// sets it) running out is Polly's exception, not the cancellation
    /// HttpClient's own timeout would be; a slow answer is one of the ways a
    /// completion fails, so it is translated here beside the other failures
    /// rather than caught by every caller.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException)
        {
            throw new AzureFoundryException("Azure Foundry did not answer before the request timed out.");
        }
    }

    /// <summary>
    /// Takes off a code fence wrapped around the whole answer. The prompt asks
    /// for none, and a model that adds one anyway usually labels it
    /// <c>```markdown</c>; a fenced plan would reach Tasks' import as one block
    /// the splitter is told to skip, so this is the one bit of tidying that
    /// stands between a good answer and "nothing parsed". An answer that opens
    /// with a fence and does not close with one is left alone — that fence is
    /// part of an entry, not a wrapper.
    /// </summary>
    internal static string StripFence(string answer)
    {
        var text = answer.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstLineEnd = text.IndexOf('\n', StringComparison.Ordinal);
        if (firstLineEnd < 0)
        {
            return text;
        }

        var body = text[(firstLineEnd + 1)..^3];
        return body.Trim();
    }

    private static string TrimForMessage(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "...";
    }

    private sealed record ChatCompletionRequest(IReadOnlyList<ChatMessage> Messages);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(IReadOnlyList<ChatChoice> Choices);

    private sealed record ChatChoice(ChatMessageContent Message);

    private sealed record ChatMessageContent([property: JsonPropertyName("content")] string? Content);
}

public sealed class AzureFoundryException : Exception
{
    public AzureFoundryException(string message)
        : base(message)
    {
    }
}

public sealed class UnavailableAzureFoundryChatClient : IAzureFoundryChatClient
{
    public Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default) =>
        throw new AzureFoundryException("Azure Foundry AI support is not registered in this build.");

    public Task<AzureFoundryPlanResponse> DraftPlanAsync(AzureFoundryPlanRequest request, CancellationToken cancellationToken = default) =>
        throw new AzureFoundryException("Azure Foundry AI support is not registered in this build.");
}
