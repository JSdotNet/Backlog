var builder = DistributedApplication.CreateBuilder(args);

// Desktop (MAUI) is not started by Aspire for now — run/debug it separately.
// builder.AddProject("desktop", "..\\Backlog.Desktop\\Backlog.Desktop.csproj");
builder.AddProject("web", "..\\Backlog.Web\\Backlog.Web.csproj");

builder.Build().Run();
