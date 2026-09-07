using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.WebHarness.Components;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The device half of cloud sync. The credential lives under this harness's own
// content root, which is what makes it a different device from the desktop
// harness rather than the same one seen twice — the pair of them is how the
// pairing flow is exercised at all without two machines. The override variable
// is this harness's own for the same reason: one shared name would let a single
// setting collapse the pair back into one device.
builder.Services.AddSingleton(_ => DeviceCredentialStoreFactory.CreateLocalDevelopmentStore(
    builder.Environment.ContentRootPath,
    "BACKLOG_MOBILE_DEVICE_CREDENTIAL_PATH",
    Path.Combine("obj", "local-development", "device-credential.json")));

// "https+http://sync" is resolved by Aspire service discovery, so the browser
// harness always talks to the sync service of this AppHost run.
//
// Pairing and tokens only. Task replication is AddTaskSyncClient, and it is not
// called here on purpose: it needs an ITaskRepository, and the mobile head has
// none - it carries the Inbox, not a local task database. Registering it anyway
// is not a dormant feature, it is a host that cannot start.
builder.Services.AddSyncClient(new Uri("https+http://sync"));

// The inbox endpoints are bearer-only, so the data client leaves carrying the
// same short-lived token the pairing client mints. The handler is chained here
// rather than inside AddSyncClient because which of a host's clients are sync
// clients is the host's to know.
builder.Services.AddHttpClient<CloudSyncClient>(client =>
    client.BaseAddress = new Uri("https+http://sync"))
    .AddHttpMessageHandler<SyncAuthenticationHandler>();

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
