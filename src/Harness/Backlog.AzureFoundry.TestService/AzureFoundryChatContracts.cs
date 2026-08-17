namespace Backlog.AzureFoundry.TestService;

public sealed record AzureFoundryChatCompletionRequest(IReadOnlyList<AzureFoundryChatMessage> Messages);

public sealed record AzureFoundryChatMessage(string Role, string Content);
