using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.WebHarness;
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
// same short-lived token the pairing client mints, and stops with the rest when
// the service is newer than the app. The pipeline is chained here rather than
// inside AddSyncClient because which of a host's clients are sync clients is
// the host's to know. The version compared is the harness's own assembly
// version, the AddSyncClient default.
builder.Services.AddHttpClient<CloudSyncClient>(client =>
    client.BaseAddress = new Uri("https+http://sync"))
    .AddSyncPipeline();

// The browser half of ISpeechTranscriber. The MAUI head registers the Android
// recogniser against the same abstraction; neither implementation runs in the
// other's host, which is why there are two registrations rather than one.
builder.Services.AddScoped<ISpeechTranscriber, WebSpeechTranscriber>();

// The browser half of IAttachmentPicker: hidden <InputFile> elements on the
// Note page, which Playwright can hand a fixture to. The MAUI head registers the
// Android camera and pickers against the same abstraction.
builder.Services.AddScoped<IAttachmentPicker, WebAttachmentPicker>();

// The browser half of ISharedContentReceiver: a share arrives as ?shared=&subject=
// because a browser cannot be given an Android intent. The MAUI head registers the
// share target against the same abstraction; neither implementation runs in the
// other's host, which is why there are two registrations rather than one. Scoped,
// because it reads the address the current circuit was opened on.
builder.Services.AddScoped<ISharedContentReceiver, QuerySharedContentReceiver>();

// The outbox and the cached inbox, under this harness's own content root beside
// its device credential, for the same reason: it is this device's, not the
// desktop harness's. The override variable is this harness's own.
var deviceStorePath = Environment.GetEnvironmentVariable("BACKLOG_MOBILE_OUTBOX_PATH");
builder.Services.AddDeviceOutbox(string.IsNullOrWhiteSpace(deviceStorePath)
    ? Path.Combine(builder.Environment.ContentRootPath, "obj", "local-development", "mobile-outbox.db")
    : deviceStorePath);

// The shell's own state: the capture draft that outlives a tab switch, and the
// sync status the app bar reads. The MAUI head calls the same method.
builder.Services.AddMobileShell();

var app = builder.Build();

// A stand-up to look at without a desktop that has goals on: goals, impediments, a
// stand-up context and the tasks they count, written into this device's views before
// anything reads them, and only while they hold no goal. Off unless the configuration
// turns it on, which appsettings.Development.json does.
if (app.Configuration.GetValue("Harness:SeedStandup", false))
{
    await StandupSeed.SeedAsync(app.Services, app.Services.GetRequiredService<TimeProvider>());
}

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
