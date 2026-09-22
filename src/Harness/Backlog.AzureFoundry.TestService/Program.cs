using System.Text.Json;
using Backlog.AzureFoundry.TestService;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "Backlog Azure Foundry local test service" }));

app.MapPost("/openai/deployments/{deployment}/chat/completions", async (string deployment, AzureFoundryChatCompletionRequest request, CancellationToken cancellationToken) =>
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
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
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
