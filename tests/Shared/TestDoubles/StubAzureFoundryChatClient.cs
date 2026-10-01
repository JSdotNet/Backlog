using Backlog.Infrastructure.AzureFoundry;

namespace Backlog.Tests;

/// <summary>Answers every question with the same line, and keeps what it was
/// asked, for the tests where which body the shell handed over is the thing
/// under test.</summary>
internal sealed class StubAzureFoundryChatClient : IAzureFoundryChatClient
{
    public List<AzureFoundryChatRequest> Requests { get; } = [];

    public Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(new AzureFoundryChatResponse("Not used in this test."));
    }

    public Task<AzureFoundryPlanResponse> DraftPlanAsync(AzureFoundryPlanRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AzureFoundryPlanResponse("# Not used in this test."));
}
