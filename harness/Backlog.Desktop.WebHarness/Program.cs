using Backlog.Infrastructure.FileSystem;
using Backlog.Desktop.UI.Components;
using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;
using Backlog.Desktop.WebHarness.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(_ => new BacklogStore(builder.Configuration["Backlog:SettingsRoot"]));
builder.Services.AddSingleton<GitHubSettingsStore>();
builder.Services.AddSingleton(sp => new ResolvingGitHubTransport(sp.GetRequiredService<GitHubSettingsStore>()));
builder.Services.AddSingleton<IGitHubConnectionProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
builder.Services.AddSingleton<IGitHubClient>(sp => new GitHubClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
builder.Services.AddSingleton<GitHubIntegration>();
builder.Services.AddSingleton<DesignKnowledgeProvider>();
builder.Services.AddSingleton<KnowledgeBacklog>();
builder.Services.AddScoped<BacklogDesktopState>();
builder.Services.AddSingleton<DomainKnowledgeStore>();
builder.Services.AddSingleton<Arc42KnowledgeStore>();

// The web host never distributes or updates the desktop app, so it always
// reports updates as unsupported.
builder.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
builder.Services.AddSingleton<ICopilotToolService, UnsupportedCopilotToolService>();

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
