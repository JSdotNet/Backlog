using Backlog.Infrastructure.FileSystem;
using Backlog.Desktop.UI.Components;
using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;
using Backlog.Desktop.WebHarness.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<BacklogStore>();
builder.Services.AddSingleton(_ =>
{
    var settingsPath = builder.Configuration["Backlog:GitHubSettingsPath"];
    return string.IsNullOrWhiteSpace(settingsPath)
        ? new GitHubSettingsStore()
        : new GitHubSettingsStore(settingsPath);
});
builder.Services.AddSingleton(sp => new ResolvingGitHubTransport(sp.GetRequiredService<GitHubSettingsStore>()));
builder.Services.AddSingleton<IGitHubConnectionProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
builder.Services.AddSingleton<IGitHubClient>(sp => new GitHubClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
builder.Services.AddSingleton<GitHubIntegration>();
builder.Services.AddSingleton<KnowledgeFolderSource>();
builder.Services.AddSingleton<KnowledgeBacklog>();
builder.Services.AddSingleton<TechnologyKnowledgeService>();
builder.Services.AddSingleton<ICopilotToolService, UnsupportedCopilotToolService>();
builder.Services.AddScoped<BacklogDesktopState>();

// The web host never distributes or updates the desktop app, so it always
// reports updates as unsupported.
builder.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Routes).Assembly);

app.Run();
