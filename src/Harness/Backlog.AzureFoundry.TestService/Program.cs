using System.Text.Json;
using Backlog.AzureFoundry.TestService;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// The clock the answers are stamped from (app-defaults ADR 0005), registered here rather
// than left to whatever the web host happens to add.
builder.Services.TryAddSingleton(TimeProvider.System);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "Backlog Azure Foundry local test service" }));

app.MapPost("/openai/deployments/{deployment}/chat/completions", async (string deployment, AzureFoundryChatCompletionRequest request, TimeProvider time, CancellationToken cancellationToken) =>
{
    // A slow model on request - delay:15s in the question - so the desktop
    // client's timeout budget can be exercised from the browser.
    var delay = LocalAzureFoundryCompletion.RequestedDelay(request.Messages);
    if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);

    var answer = LocalAzureFoundryCompletion.CreateAnswer(request.Messages);

    return Results.Ok(new
    {
        id = $"local-{Guid.NewGuid():N}",
        @object = "chat.completion",
        created = time.GetUtcNow().ToUnixTimeSeconds(),
        model = deployment,
        choices = new[]
        {
            new
            {
                index = 0,
                finish_reason = "stop",
                message = new { role = "assistant", content = answer }
            }
        }
    });
});

// The transcription stand-in the sync service relays a phone's dictation to under Aspire
// (local ADR 0045 §5). Mapped before the catch-all below, which binds a JSON body and would
// refuse the multipart one this route takes.
app.MapPost(
    "/openai/deployments/{deployment}/audio/transcriptions",
    (string deployment, HttpRequest request, CancellationToken cancellationToken) =>
        LocalAzureFoundryTranscription.Answer(deployment, request, cancellationToken))
    .DisableAntiforgery();

// Cost Management's query, at any scope: the desktop client posts to
// {scope}/providers/Microsoft.CostManagement/query, and the scope is a path of
// its own, so the route is a catch-all checked for that suffix.
app.MapPost("/{**path}", (string path, JsonElement body) =>
{
    if (!path.EndsWith("/providers/Microsoft.CostManagement/query", StringComparison.OrdinalIgnoreCase))
    {
        return Results.NotFound();
    }

    var window = LocalAzureFoundryCost.ReadWindow(body);
    return window is null
        ? Results.BadRequest(new { error = new { code = "BadRequest", message = "A Custom timeframe needs a timePeriod with from and to." } })
        : Results.Ok(LocalAzureFoundryCost.CreateResponse(window.Value));
});

app.Run();
