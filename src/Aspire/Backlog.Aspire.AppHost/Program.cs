var builder = DistributedApplication.CreateBuilder(args);

// Cosmos DB — the replica store the sync service reads and writes (local ADR 0005).
// Deployed it is a serverless account provisioned by infra/sync/main.bicep; here it
// is the emulator, so the sync path builds and runs with no Azure account and no
// subscription cost.
//
// The database and both containers are declared so the local shape matches the
// deployed one: one database, `tasks` and `sessions`, both partitioned on /ownerId.
// The two TTLs and the sessions indexing policy are NOT expressed here — the
// emulator honours neither, and duplicating them would create a second place for
// them to drift from infra/sync/main.bicep, which is where ADR 0005 puts them.
#pragma warning disable ASPIRECOSMOSDB001 // RunAsPreviewEmulator is experimental; see comment below.
// The preview (vNext) emulator rather than the original: it is the smaller image,
// it does not need its self-signed certificate trusted on the host first, and it
// ships the Data Explorer. It is still not quick — the image is ~2.5 GB and a cold
// start takes a couple of minutes — which is why nothing below waits on it.
// The API is still marked experimental, so the suppression is scoped to this call
// rather than added to the project's NoWarn.
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsPreviewEmulator(emulator => emulator
        .WithDataExplorer()
        // Persistent, so the container survives between AppHost runs and only the
        // first one on a machine pays that cold start.
        .WithLifetime(ContainerLifetime.Persistent));
#pragma warning restore ASPIRECOSMOSDB001

var cosmosDatabase = cosmos.AddCosmosDatabase("backlog");
cosmosDatabase.AddContainer("tasks", "/ownerId");
cosmosDatabase.AddContainer("sessions", "/ownerId");

// Sync service — the thin cloud-side sync layer (Azure Container Apps in production).
//
// Referenced, not waited on. The service holds its capture state in memory today
// (SyncStore, which ADR 0005 retires when the Cosmos-backed store lands), so it has
// nothing to wait for — and mobile-web-harness waits on `sync`, so a WaitFor here
// would put the emulator's startup in front of an unrelated harness on every run.
var sync = builder.AddProject("sync", "..\\..\\Modules\\Sync\\Backlog.Modules.Sync.Api\\Backlog.Modules.Sync.Api.csproj")
    .WithReference(cosmosDatabase);

// --- Test harnesses (src/Harness/) ---------------------------------------
// The projects below are NOT shipped channels. They are development-only hosts
// and local doubles so Aspire and Playwright can exercise app behavior without
// deploying cloud dependencies or MAUI heads.

// Local Azure Foundry-compatible endpoint used only by Aspire development runs.
var azureFoundryTest = builder.AddProject("azure-foundry-test", "..\\..\\Harness\\Backlog.AzureFoundry.TestService\\Backlog.AzureFoundry.TestService.csproj");

// Desktop UI in the browser: hosts Backlog.Desktop.UI, the same components the
// MAUI Blazor Hybrid desktop head renders in its WebView.
builder.AddProject("desktop-web-harness", "..\\..\\Harness\\Backlog.Desktop.WebHarness\\Backlog.Desktop.WebHarness.csproj")
    .WithReference(sync)
    .WithReference(azureFoundryTest)
    .WithEnvironment("BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT", azureFoundryTest.GetEndpoint("http"))
    .WaitFor(azureFoundryTest);

// Mobile UI in the browser. The Android head needs an emulator, so this harness
// hosts the same Razor components (Backlog.Mobile.UI) at phone width.
builder.AddProject("mobile-web-harness", "..\\..\\Harness\\Backlog.Mobile.WebHarness\\Backlog.Mobile.WebHarness.csproj")
    .WithReference(sync)
    .WaitFor(sync);

// Component storybook: the shared Backlog.UI.Components library on its own, with
// no application or sync dependency, so the components can be reviewed and
// Playwright-driven independently of the app.
builder.AddProject("ui-storybook", "..\\..\\Harness\\Backlog.UI.Storybook\\Backlog.UI.Storybook.csproj");

// --- Shipped channels (src/App) ---------------------------------------------

// Desktop channel — .NET MAUI Blazor Hybrid (Windows). Registered so it shows up in
// the app model, but never auto-started: launch it from the dashboard or the IDE.
builder.AddProject("desktop", "..\\..\\App\\Backlog.Desktop\\Backlog.Desktop.csproj")
    .WithReference(sync)
    .WithExplicitStart();

// Mobile channel — .NET MAUI Blazor Hybrid (Android). Aspire cannot run an Android
// head as a project resource, so deploy and launch it on the running emulator or
// attached device via the MSBuild Run target.
builder.AddExecutable(
        "mobile-android",
        "dotnet",
        "..\\..\\App\\Backlog.Mobile",
        "build", "Backlog.Mobile.csproj", "-t:Run", "-f", "net10.0-android")
    .WithEnvironment("BACKLOG_SYNC_URL", sync.GetEndpoint("http"))
    .WithExplicitStart();

// IDE channel — VS Code extension. The watch build keeps out/extension.js current.
builder.AddExecutable("ide-vscode-build", "npm", "..\\..\\App\\Backlog.Ide.VsCode", "run", "watch")
    .WithExplicitStart();

// Launches a VS Code Extension Development Host with the extension side-loaded.
builder.AddExecutable(
        "ide-vscode-host",
        "code",
        "..\\..\\App\\Backlog.Ide.VsCode",
        "--extensionDevelopmentPath=.", "--new-window", ".")
    .WithExplicitStart();

builder.Build().Run();

