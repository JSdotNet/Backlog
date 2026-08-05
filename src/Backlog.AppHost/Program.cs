var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject("desktop", "..\\Backlog.Desktop\\Backlog.Desktop.csproj");

builder.Build().Run();
