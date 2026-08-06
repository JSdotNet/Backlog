var builder = DistributedApplication.CreateBuilder(args);

// Cloud service — thin sync layer (Azure Container Apps in production).
var cloud = builder.AddProject("cloud", "..\\Backlog.Cloud\\Backlog.Cloud.csproj");

// Web channel — Blazor Server host of the shared UI.
builder.AddProject("web", "..\\Backlog.Web\\Backlog.Web.csproj")
    .WithReference(cloud);

// Mobile UI in the browser. The Android head needs an emulator, so this harness
// hosts the same Razor components (Backlog.Mobile.UI) at phone width — the same
// split the desktop channel uses with Backlog.UI + Backlog.Web.
builder.AddProject("mobile-web", "..\\Backlog.Mobile.Web\\Backlog.Mobile.Web.csproj")
    .WithReference(cloud)
    .WaitFor(cloud);

// Desktop channel — .NET MAUI Blazor Hybrid (Windows). Registered so it shows up in
// the app model, but never auto-started: launch it from the dashboard or the IDE.
builder.AddProject("desktop", "..\\Backlog.Desktop\\Backlog.Desktop.csproj")
    .WithReference(cloud)
    .WithExplicitStart();

// Mobile channel — .NET MAUI Blazor Hybrid (Android). Aspire cannot run an Android
// head as a project resource, so deploy and launch it on the running emulator or
// attached device via the MSBuild Run target.
builder.AddExecutable(
        "mobile-android",
        "dotnet",
        "..\\Backlog.Mobile",
        "build", "Backlog.Mobile.csproj", "-t:Run", "-f", "net10.0-android")
    .WithEnvironment("BACKLOG_CLOUD_URL", cloud.GetEndpoint("http"))
    .WithExplicitStart();

// IDE channel — VS Code extension. The watch build keeps out/extension.js current.
builder.AddExecutable("ide-vscode-build", "npm", "..\\Backlog.Ide.VsCode", "run", "watch")
    .WithExplicitStart();

// Launches a VS Code Extension Development Host with the extension side-loaded.
builder.AddExecutable(
        "ide-vscode-host",
        "code",
        "..\\Backlog.Ide.VsCode",
        "--extensionDevelopmentPath=.", "--new-window", ".")
    .WithExplicitStart();

builder.Build().Run();
