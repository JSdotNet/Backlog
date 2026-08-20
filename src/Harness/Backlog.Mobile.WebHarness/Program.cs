using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.WebHarness.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// "https+http://sync" is resolved by Aspire service discovery, so the browser
// harness always talks to the sync service of this AppHost run.
builder.Services.AddHttpClient<CloudSyncClient>(client =>
    client.BaseAddress = new Uri("https+http://sync"));

// The browser half of ISpeechTranscriber. The MAUI head registers the Android
// recogniser against the same abstraction; neither implementation runs in the
// other's host, which is why there are two registrations rather than one.
builder.Services.AddScoped<ISpeechTranscriber, WebSpeechTranscriber>();

// The browser half of ISharedContentReceiver: a share arrives as ?shared=&subject=
// because a browser cannot be given an Android intent. The MAUI head registers the
// share target against the same abstraction; neither implementation runs in the
// other's host, which is why there are two registrations rather than one. Scoped,
// because it reads the address the current circuit was opened on.
builder.Services.AddScoped<ISharedContentReceiver, QuerySharedContentReceiver>();

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
