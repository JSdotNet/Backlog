using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api;
using Backlog.Modules.Sync.Api.Endpoints;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Extensions;
using Backlog.Modules.Sync.Services;

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

builder.Services.AddSyncModule();

// The module declares the port and the host implements it, so the signing key
// and the validation parameters that check it stay together.
builder.Services.AddSingleton<IDeviceTokenIssuer, JwtDeviceTokenIssuer>();

builder.Services.AddSingleton<SyncStore>();

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
