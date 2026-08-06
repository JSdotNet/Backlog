var builder = DistributedApplication.CreateBuilder(args);

// Cloud service — thin sync layer (Azure Container Apps in production).
var cloud = builder.AddProject("cloud", "..\\Backlog.Cloud\\Backlog.Cloud.csproj");

// Web channel — Blazor Server host of the shared UI.
builder.AddProject("web", "..\\Backlog.Web\\Backlog.Web.csproj")
    .WithReference(cloud);

// Desktop channel — .NET MAUI Blazor Hybrid (Windows). Registered so it shows up in
// the app model, but never auto-started: launch it from the dashboard or the IDE.
builder.AddProject("desktop", "..\\Backlog.Desktop\\Backlog.Desktop.csproj")
    .WithReference(cloud)
    .WithExplicitStart();

// Mobile channel — .NET MAUI Blazor Hybrid (Android). Needs an emulator/device, so it
// is explicit-start too; `dotnet build -t:Run` deploys it.
builder.AddProject("mobile", "..\\Backlog.Mobile\\Backlog.Mobile.csproj")
    .WithReference(cloud)
    .WithExplicitStart();

// IDE channel — VS Code extension. Aspire runs the TypeScript watch build.
builder.AddExecutable("ide-vscode", "npm", "..\\Backlog.Ide.VsCode", "run", "watch")
    .WithExplicitStart();

builder.Build().Run();
