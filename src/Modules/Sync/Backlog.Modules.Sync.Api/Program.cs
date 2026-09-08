using System.Diagnostics;
using Backlog.Infrastructure.Cosmos.Extensions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Endpoints;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Extensions;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Services;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Security first, because everything below is registered behind it: the
// fallback policy denies any endpoint that does not open itself.
var generatedDevelopmentSigningKey = builder.AddSyncAuthentication();

// Every non-success response is RFC 7807 (inherited ADR 0017), including the
// ones the framework produces rather than an endpoint: a 401 from the bearer
// handler and a 500 from an unhandled exception come back in the same shape as
// a 409 from a handler.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] =
        Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
});

builder.Services.AddOpenApi();

// Before AddSyncModule, so the concrete replicas win: the module registers its
// in-memory stand-ins with TryAdd, which no-op once this has registered the
// Cosmos-backed ones for both containers. And this call itself no-ops when there
// is no Cosmos connection string, which is what lets the endpoint tests and a
// bare `dotnet run` work with no emulator anywhere.
builder.AddCosmosReplicas();

builder.Services.AddSyncModule();

// The module declares the port and the host implements it, so the signing key
// and the validation parameters that check it stay together.
builder.Services.AddSingleton<IDeviceTokenIssuer, JwtDeviceTokenIssuer>();

// Same reason, and the same key: the pull cursor is signed with a key derived
// from the token signing key, so minting and verifying stay in the one place
// that holds it.
builder.Services.AddSingleton<ISyncCursorCodec, HmacSyncCursorCodec>();

// The module's own activities and counters (inherited ADR 0010). ServiceDefaults
// only listens to the source named for the application, and these are named for
// the module so the signal reads the same wherever the handlers run.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(SyncTelemetry.Name))
    .WithMetrics(metrics => metrics.AddMeter(SyncTelemetry.Name));

var app = builder.Build();

if (generatedDevelopmentSigningKey)
{
    app.WarnAboutEphemeralSigningKey();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

// Development only. The document names every route and its shape, which is a
// map of the surface handed to anyone who asks; a deployed sync service has no
// reason to publish one anonymously.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

var sync = app.MapGroup(SyncRoutes.Base);

sync.MapDeviceEndpoints();
sync.MapInboxEndpoints();
sync.MapTaskSyncEndpoints();
sync.MapSessionSyncEndpoints();

// The service saying what it is. No owner, no data, nothing to protect.
app.MapGet("/", () => Results.Ok(new { service = "Backlog Sync", role = "thin sync layer" }))
    .AllowAnonymous();

app.Run();

/// <summary>
/// Named so <c>WebApplicationFactory&lt;Program&gt;</c> can find the entry point
/// of a top-level-statements host. The endpoints are worth testing against a
/// real pipeline: what a 401 looks like is decided by middleware, not by a
/// handler.
/// </summary>
public partial class Program;
