using Backlog.Cloud;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<SyncStore>();

var app = builder.Build();

var sync = app.MapGroup("/api/sync");

sync.MapGet("/inbox", (SyncStore store) => Results.Ok(store.All()));

sync.MapPost("/inbox", (SyncStore store, CaptureRequest request) =>
{
    var item = store.Capture(request.Title, request.Source);
    return Results.Created($"/api/sync/inbox/{item.Id}", item);
});

sync.MapPost("/inbox/{id:guid}/ack", (SyncStore store, Guid id) =>
    store.Acknowledge(id) ? Results.NoContent() : Results.NotFound());

app.MapGet("/", () => Results.Ok(new { service = "Backlog Cloud", role = "thin sync layer" }));

app.Run();
