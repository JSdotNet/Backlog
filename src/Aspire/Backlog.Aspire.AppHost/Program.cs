using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Foundry;

using Backlog.Aspire.AppHost;

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

// Foundry Local — a real small model on this machine, beside the stand-in above
// rather than instead of it. The two answer different questions: the harness is
// deterministic and always there, this one is what the app actually meets.
//
// Registered only where it can run. WithExplicitStart is on it as well, but it is
// not what holds it back and cannot be: registering this unconditionally on a
// machine without the CLI was tried, and RunAsFoundryLocal launched `foundry` as
// the app model came up — the resource sat at FailedToStart from the first second
// of every run, carrying no start command of its own, while every other resource
// came up healthy around it. A permanently red resource is exactly what this
// repository tells a QA orchestration to read as a broken startup, so the guard is
// what keeps a run honest. AddFoundry also brings Azure provisioning in with it,
// and that is not something to add to a run that was never going to reach a model.
//
// The cost is an app model that differs per machine, which is why the README table
// and both orchestration briefs say so rather than listing a resource most runs
// never show.
if (IsOnPath("foundry"))
{
    // AddDeployment returns a resource of its own rather than the Foundry resource
    // it hangs off, so the WithExplicitStart above does not reach it: without the
    // second one the deployment carries no explicit-start annotation at all. Whether
    // the CLI launch described above pre-empts that anyway is unverified — no machine
    // in the run that wrote this had `foundry` on PATH — but an unannotated resource
    // is not the shape this registration intends in either case.
    builder.AddFoundry("foundry-local")
        .RunAsFoundryLocal()
        .WithExplicitStart()
        .AddDeployment("chat", FoundryModel.Local.Phi4)
        .WithExplicitStart();
}

static bool IsOnPath(string command)
{
    // The bare name counts on a Unix host; on Windows only the PATHEXT variants do.
    var names = (Environment.GetEnvironmentVariable("PATHEXT") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(extension => command + extension)
        .Prepend(command);

    return (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .SelectMany(folder => names.Select(name => Path.Combine(folder, name)))
        .Any(File.Exists);
}

// Desktop UI in the browser: hosts Backlog.Desktop.UI, the same components the
// MAUI Blazor Hybrid desktop head renders in its WebView.
//
// It also carries the reset-local-data command, because this is the resource that
// reads the local store. The blast radius is wider than this run: every git
// worktree of this repository resolves to the same per-user workspace, so a reset
// here is felt by every session running beside it. That is why it is a command
// someone chooses rather than anything a start does.
//
// The folder is resolved once here, as the app model is built, so the confirmation
// can name the folder the reset would actually delete from. That is not always the
// default one: the settings screen accepts any rooted path and persists it, and a
// dialog promising `Backlog.Debug` while the command deletes a real backlog is
// worse than no dialog at all. Run re-resolves and refuses if the two have parted.
var resetRoot = LocalDataReset.ResolveRoot();

builder.AddProject("desktop-web-harness", "..\\..\\Harness\\Backlog.Desktop.WebHarness\\Backlog.Desktop.WebHarness.csproj")
    .WithReference(sync)
    .WithReference(azureFoundryTest)
    .WithEnvironment("BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT", azureFoundryTest.GetEndpoint("http"))
    .WaitFor(azureFoundryTest)
    .WithCommand(
        "reset-local-data",
        "Reset local data",
        context => Task.FromResult(LocalDataReset.Run(context, resetRoot)),
        commandOptions: new CommandOptions
        {
            Description = "Removes the task database and the workspace settings, returning the local store to "
                          + "first-run state.",
            ConfirmationMessage = $"Delete the task database and the workspace settings in {resetRoot}? Every git "
                                  + "worktree of this repository shares this workspace, so every session on this "
                                  + "machine is reset with it."
        });

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

// Mobile channel — .NET MAUI Blazor Hybrid (Android), deployed and launched on an
// already-running emulator or attached device through the MSBuild Run target.
//
// The target framework and the device used to be pinned in the argument list, so
// running the head anywhere else meant editing this file. Aspire 13.5 resource
// commands take arguments, so both are chosen in the dashboard instead and read
// on the next start of the resource.
var mobileTargetFramework = "net10.0-android";
var mobileDevice = "";

builder.AddExecutable(
        "mobile-android",
        "dotnet",
        "..\\..\\App\\Backlog.Mobile",
        "build", "Backlog.Mobile.csproj", "-t:Run")
    .WithArgs(context =>
    {
        context.Args.Add("-f");
        context.Args.Add(mobileTargetFramework);

        // Left off entirely when no device was named, so MSBuild keeps its own
        // default of "the only attached device".
        if (!string.IsNullOrWhiteSpace(mobileDevice)) context.Args.Add($"-p:AdbTarget=-s {mobileDevice}");
    })
    .WithEnvironment("BACKLOG_SYNC_URL", sync.GetEndpoint("http"))
    .WithCommand(
        "set-run-target",
        "Set run target",
        context =>
        {
            // GetString answers null when the dialog sent no value for an input.
            // The framework is Required so it should always arrive, but a null
            // assigned here would reach context.Args as a null argument on the next
            // start; keeping the previous choice fails the safe way instead.
            var chosenFramework = context.Arguments.GetString("targetFramework");
            if (!string.IsNullOrWhiteSpace(chosenFramework)) mobileTargetFramework = chosenFramework;

            mobileDevice = context.Arguments.GetString("device") ?? "";

            return Task.FromResult(CommandResults.Success(
                $"Next start builds {mobileTargetFramework}"
                + (string.IsNullOrWhiteSpace(mobileDevice) ? "." : $" on {mobileDevice}.")));
        },
        commandOptions: new CommandOptions
        {
            Description = "Chooses the head and the device the next start deploys to.",
            Arguments =
            [
                new InteractionInput
                {
                    Name = "targetFramework",
                    Label = "Target framework",
                    InputType = InputType.Choice,
                    Required = true,
                    AllowCustomChoice = true,
                    Value = mobileTargetFramework,
                    Options = [KeyValuePair.Create("net10.0-android", "Android (net10.0-android)")]
                },
                new InteractionInput
                {
                    Name = "device",
                    Label = "Device serial",
                    Description = "An `adb devices` serial, such as emulator-5554. Leave empty for the only "
                                  + "attached device.",
                    InputType = InputType.Text,
                    Required = false,
                    Value = mobileDevice
                }
            ]
        })
    .WithExplicitStart();

// The same Android head, registered through Aspire.Hosting.Maui rather than as a
// process. That resource knows how to bring an emulator up and how to point the
// head's telemetry back at the dashboard, which the MSBuild Run target cannot;
// the executable above is still the shorter path to an already-attached device,
// so both are registered and both start on demand.
//
// The emulator reaches the developer machine over its own loopback, not this one,
// so sync is published through a tunnel before the head can see it at all. That
// takes both halves of the reference: the tunnel is told which endpoints to open
// a port for, and the head is told to resolve sync through the tunnel rather than
// through localhost. Without the first half the tunnel forwards nothing and the
// head's reference has no tunnel endpoint to resolve.
//
// The tunnel needs the `devtunnel` CLI and an account, so it starts on demand
// like the head does. Start it first: referencing a tunnel holds the referencing
// resource until the tunnel's endpoint is allocated, so starting the head on its
// own waits for a tunnel nobody started.
var mobileTunnel = builder.AddDevTunnel("mobile-tunnel")
    .WithReference(sync)
    .WithAnonymousAccess()
    .WithExplicitStart();

// AddMauiProject registers a parent container that builds nothing on its own —
// builds are deferred until one of its platform children starts. AddAndroidEmulator
// is what adds that child, and it returns the child rather than the parent, so the
// reference, the telemetry tunnel and the dashboard's start button all belong to it
// and not to `mobile-maui`. Aspire names it after the parent, so the resource that
// actually appears in the dashboard is `mobile-maui-android-emulator` — which is the
// name README.md and both orchestration briefs give, and the reason this variable is
// not called `mobileMaui`.
var mobileAndroidEmulator = builder
    .AddMauiProject("mobile-maui", "..\\..\\App\\Backlog.Mobile\\Backlog.Mobile.csproj")
    .AddAndroidEmulator()
    .WithReference(sync, mobileTunnel)
    .WithExplicitStart();

// Telemetry from the head takes a second tunnel, and WithOtlpDevTunnel resolves
// the dashboard's OTLP port while the app model is being built rather than when
// the head starts. Every endpoint in this repository binds localhost:0 so parallel
// worktree runs never collide, and port 0 is not a port it can forward: it throws,
// and it takes the whole AppHost down with it — including every resource that has
// nothing to do with the Android head. So it is added only when a run pins that
// port, which a run that wants telemetry off the emulator has to do anyway.
// It goes on the platform child, not the parent: WithOtlpDevTunnel configures the
// resource that runs the head, which is the emulator.
if (HasFixedDashboardOtlpPort()) mobileAndroidEmulator.WithOtlpDevTunnel();

bool HasFixedDashboardOtlpPort() =>
    new[] { "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "DOTNET_DASHBOARD_OTLP_ENDPOINT_URL" }
        .Select(key => builder.Configuration[key])
        .Any(url => Uri.TryCreate(url, UriKind.Absolute, out var endpoint) && endpoint.Port > 0);

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
