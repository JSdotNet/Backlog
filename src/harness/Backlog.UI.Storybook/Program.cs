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

// A URL that matches no page is 404'd by routing before the Blazor Router ever
// runs, so the Router's own NotFound template never fires for a typed or
// bookmarked address — the response was an empty document with no title, no
// chrome and no way back. Re-executing onto /not-found renders the same page
// the Router shows, while keeping the 404 status the client asked about.
app.UseStatusCodePagesWithReExecute("/not-found");

// The other harnesses serve the library's wwwroot through the static web assets
// manifest, so this one does the same and behaves identically under Aspire.
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
