using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.AzureFoundry;

public sealed record AzureFoundryChatRequest(string Content, string Question);

public sealed record AzureFoundryChatResponse(string Answer);

public interface IAzureFoundryChatClient
{
    Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default);
}

public sealed class AzureFoundryChatClient(HttpClient httpClient, AzureFoundrySettingsStore settingsStore) : IAzureFoundryChatClient
{
    private const string SystemPrompt = "You answer questions about the supplied Backlog content. Use only the supplied content. If the content does not contain the answer, say you do not know from the content.";

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

        var settings = settingsStore.Current;
        if (!settings.IsConfigured)
        {
            throw new AzureFoundryException("Configure Azure Foundry in Settings before asking AI questions.");
        }

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new AzureFoundryException("The Azure Foundry endpoint in Settings is not a valid URL.");
        }

        var requestUri = new Uri(endpoint, $"/openai/deployments/{Uri.EscapeDataString(settings.Deployment!)}/chat/completions?api-version={Uri.EscapeDataString(settings.ApiVersion)}");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new ChatCompletionRequest(
            [
                new ChatMessage("system", SystemPrompt),
                new ChatMessage("user", $"Content:\n{request.Content.Trim()}\n\nQuestion:\n{request.Question.Trim()}")
            ]), options: JsonOptions)
        };
        httpRequest.Headers.Add("api-key", settings.ApiKey);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
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

        return new AzureFoundryChatResponse(answer.Trim());
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
}
