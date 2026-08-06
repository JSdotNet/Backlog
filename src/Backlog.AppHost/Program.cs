var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject("desktop", "..\\Backlog.Desktop\\Backlog.Desktop.csproj");
builder.AddProject("web", "..\\Backlog.Web\\Backlog.Web.csproj");

builder.Build().Run();
