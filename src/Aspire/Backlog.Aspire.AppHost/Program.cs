var builder = DistributedApplication.CreateBuilder(args);

// Cloud service — thin sync layer (Azure Container Apps in production).
var cloud = builder.AddProject("cloud", "..\\..\\Cloud\\Backlog.Cloud\\Backlog.Cloud.csproj");

// --- Test harnesses (src/harness/) ---------------------------------------
// The two projects below are NOT shipped channels. They are Blazor Server hosts
// that exist purely so the shared Razor components can be exercised in Aspire
// and driven by Playwright — the MAUI heads cannot be automated that way.

// Desktop UI in the browser: hosts Backlog.Desktop.UI, the same components the
// MAUI Blazor Hybrid desktop head renders in its WebView.
builder.AddProject("desktop-web-harness", "..\\..\\harness\\Backlog.Desktop.WebHarness\\Backlog.Desktop.WebHarness.csproj")
    .WithReference(cloud);

// Mobile UI in the browser. The Android head needs an emulator, so this harness
// hosts the same Razor components (Backlog.Mobile.UI) at phone width.
builder.AddProject("mobile-web-harness", "..\\..\\harness\\Backlog.Mobile.WebHarness\\Backlog.Mobile.WebHarness.csproj")
    .WithReference(cloud)
    .WaitFor(cloud);

// --- Shipped channels (src/App) ---------------------------------------------

// Desktop channel — .NET MAUI Blazor Hybrid (Windows). Registered so it shows up in
// the app model, but never auto-started: launch it from the dashboard or the IDE.
builder.AddProject("desktop", "..\\..\\App\\Backlog.Desktop\\Backlog.Desktop.csproj")
    .WithReference(cloud)
    .WithExplicitStart();

// Mobile channel — .NET MAUI Blazor Hybrid (Android). Aspire cannot run an Android
// head as a project resource, so deploy and launch it on the running emulator or
// attached device via the MSBuild Run target.
builder.AddExecutable(
        "mobile-android",
        "dotnet",
        "..\\..\\App\\Backlog.Mobile",
        "build", "Backlog.Mobile.csproj", "-t:Run", "-f", "net10.0-android")
    .WithEnvironment("BACKLOG_CLOUD_URL", cloud.GetEndpoint("http"))
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
