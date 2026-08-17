using Backlog.AzureFoundry.TestService;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "Backlog Azure Foundry local test service" }));

app.MapPost("/openai/deployments/{deployment}/chat/completions", (string deployment, AzureFoundryChatCompletionRequest request) =>
{
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

app.Run();
