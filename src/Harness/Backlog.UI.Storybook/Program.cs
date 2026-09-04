using Backlog.UI.Components.Diagrams;
using Backlog.UI.Components.Feedback;
using Backlog.UI.Storybook.Components;
using Backlog.UI.Storybook.Components.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Two exceptions, and neither is an application service.
//
// The first is the library's own type: ToastTray resolves IToastChannel to know
// what to draw, so a storybook with nothing registered would render an empty
// tray forever and prove nothing about it. Registering the real ToastChannel
// means the story drives the same queue and the same three-at-once cap the app
// does. Scoped, because a circuit here is one visitor and a singleton would
// broadcast one reader's toasts to everyone else looking at the page.
builder.Services.AddScoped<ToastChannel>();
builder.Services.AddScoped<IToastChannel>(sp => sp.GetRequiredService<ToastChannel>());

// The second is a fixture: a committed Archify artifact for one known fence, so
// the Diagrams page can show DiagramView's artifact mode. It answers null for
// every other diagram, which leaves every other page rendering exactly as it
// would with nothing registered.
builder.Services.AddSingleton<IDiagramArtifactSource, StorybookDiagramArtifacts>();

// Nothing else is registered on purpose: a storybook that needed the
// application's services would stop being evidence that the components run
// without it. The fixture above does not bend that — no module, adapter or
// application project is referenced, and UiLibraryBoundaryTests holds the line.

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
//
// With one addition: UseStaticFiles sends ETag and Last-Modified but no
// Cache-Control, which leaves the browser free to invent a freshness window from
// how long the file had gone unmodified — commonly a tenth of it. On a
// stylesheet that had sat still for a week, that is hours during which an edit
// to components.css or app.css is served from cache and the page silently shows
// the old CSS. A storybook whose whole job is to show what the CSS currently
// does cannot afford that, so every response asks to be revalidated. The ETag
// survives, so the cost of a miss is a 304 rather than a re-download.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
        context.Context.Response.Headers.CacheControl = "no-cache, must-revalidate"
});
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
