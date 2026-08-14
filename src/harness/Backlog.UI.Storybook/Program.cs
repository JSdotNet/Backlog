using Backlog.UI.Storybook.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Nothing else is registered on purpose: a storybook that needed the
// application's services would stop being evidence that the components run
// without it.

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// The other harnesses serve the library's wwwroot through the static web assets
// manifest, so this one does the same and behaves identically under Aspire.
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
