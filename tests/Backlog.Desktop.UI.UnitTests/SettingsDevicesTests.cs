using System.Net;
using System.Text;

using Bunit;

using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devices panel: what a device is, and the two ways it gets to be one.
///
/// <para>The panel drives the real <see cref="DevicePairingClient"/> against a
/// scripted service rather than a stand-in for it, because the interesting
/// behaviour is the client's as much as the screen's — that a credential is
/// stored, that a ProblemDetails code becomes a sentence somebody can act on.
/// A seam between the two would have tested neither.</para>
/// </summary>
public sealed class SettingsDevicesTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Device = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void The_devices_tab_is_not_offered_until_the_feature_is_on()
    {
        using var context = RenderSettings(devicePairingEnabled: false);

        Assert.DoesNotContain("Devices", SettingsTabs(context.Component));
    }

    [Fact]
    public void An_unpaired_device_is_offered_registering_and_pairing_but_not_a_code_to_hand_out()
    {
        using var context = RenderSettings(devicePairingEnabled: true);

        Assert.Contains("Devices", SettingsTabs(context.Component));
        OpenDevicesTab(context.Component);

        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='devices-register']"));
            Assert.Single(context.Component.FindAll("[data-testid='devices-pair']"));
        });

        // Nothing to hand out: only a device that is already in may invite
        // another one, and this one has no token to ask with.
        Assert.Empty(context.Component.FindAll("[data-testid='devices-generate-code']"));
        Assert.Single(context.Component.FindAll("[data-testid='devices-empty']"));
    }

    [Fact]
    public void The_status_card_says_where_the_credential_would_be_kept_before_there_is_one()
    {
        using var context = RenderSettings(devicePairingEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-status']")));

        var status = context.Component.Find("[data-testid='devices-status']").TextContent;
        Assert.Contains(context.Credentials.StorePath, status, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_stores_the_credential_and_the_status_card_says_who_this_device_is()
    {
        using var context = RenderSettings(devicePairingEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-register']")));

        var name = context.Component.Find("[data-testid='devices-name']");
        name.Input("Workshop PC");

        context.Component.Find("[data-testid='devices-register-button']").Click();

        context.Component.WaitForAssertion(() =>
        {
            Assert.Contains("Workshop PC", context.Component.Find("[data-testid='devices-paired']").TextContent, StringComparison.Ordinal);
            Assert.Contains(Owner.ToString(), context.Component.Find("[data-testid='devices-identity']").TextContent, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Equal("Workshop PC", context.Credentials.Current?.DeviceName);

        // And the panel has swapped states: a registered device offers a code
        // rather than the two ways of getting in.
        Assert.Single(context.Component.FindAll("[data-testid='devices-generate-code']"));
        Assert.Empty(context.Component.FindAll("[data-testid='devices-register']"));
        Assert.Contains("Registered", context.Component.Find("[data-testid='devices-message']").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The code is shown in the format a person reads out — two groups of four —
    /// while what the clipboard gets is the code itself, because the hyphen is
    /// not part of it.
    /// </summary>
    [Fact]
    public void Generating_a_code_shows_it_grouped_with_the_time_it_has_left()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-generate-code']")));

        context.Component.Find("[data-testid='devices-generate']").Click();

        context.Component.WaitForAssertion(() =>
            Assert.Equal("K7MN-9PQR", context.Component.Find("[data-testid='devices-code-display'] code").TextContent.Trim()));

        Assert.Contains("Expires in", context.Component.Find("[data-testid='devices-code-expiry']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Pairing_sends_the_code_normalized_and_stores_what_comes_back()
    {
        using var context = RenderSettings(devicePairingEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-pair']")));

        var code = context.Component.Find("[data-testid='devices-code']");
        code.Input("k7mn-9pqr");

        context.Component.Find("[data-testid='devices-pair-button']").Click();

        context.Component.WaitForAssertion(() =>
            Assert.Contains("Paired", context.Component.Find("[data-testid='devices-message']").TextContent, StringComparison.OrdinalIgnoreCase));

        Assert.Contains("/api/sync/devices/pair", context.Service.Paths);
        Assert.Contains("\"K7MN9PQR\"", context.Service.LastBody, StringComparison.Ordinal);
        Assert.NotNull(context.Credentials.Current);
    }

    /// <summary>
    /// A code that has already let a device in comes back as a 409 with a code of
    /// its own. bUnit swallows what an event handler throws, so this asserts on
    /// the sentence the failure has to produce rather than on the absence of an
    /// exception.
    /// </summary>
    [Fact]
    public void A_code_that_was_already_used_says_so_and_leaves_the_device_unpaired()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true,
            respond: (_, _) => Problem(HttpStatusCode.Conflict, SyncErrorCodes.PairingCodeUsed, "That code has already paired a device."));

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-pair']")));

        var code = context.Component.Find("[data-testid='devices-code']");
        code.Input("K7MN9PQR");

        context.Component.Find("[data-testid='devices-pair-button']").Click();

        context.Component.WaitForAssertion(() =>
        {
            var message = context.Component.Find("[data-testid='devices-message']");
            Assert.Contains("already paired", message.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("alert", message.GetAttribute("role"));
        });

        Assert.Null(context.Credentials.Current);
        Assert.Single(context.Component.FindAll("[data-testid='devices-pair']"));
    }

    // --- Task sync ----------------------------------------------------------

    /// <summary>
    /// Its own feature from device pairing, because a person can have paired
    /// devices and still not want their tasks leaving the machine.
    /// </summary>
    [Fact]
    public void The_sync_section_is_not_offered_until_the_task_sync_feature_is_on()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: true, taskSyncEnabled: false);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-status']")));

        Assert.Empty(context.Component.FindAll("[data-testid='devices-sync']"));
    }

    /// <summary>An unpaired device has no owner to replicate under, so there is
    /// nothing for the section to do.</summary>
    [Fact]
    public void An_unpaired_device_is_not_offered_the_sync_section()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: false, taskSyncEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-register']")));

        Assert.Empty(context.Component.FindAll("[data-testid='devices-sync']"));
    }

    /// <summary>A head that registered no sync-state store cannot compose the
    /// loop, and hides the section rather than offering a button that cannot
    /// work.</summary>
    [Fact]
    public void A_host_without_a_session_hides_the_section_rather_than_failing()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true, paired: true, taskSyncEnabled: true, registerTaskSync: false);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-status']")));

        Assert.Empty(context.Component.FindAll("[data-testid='devices-sync']"));
    }

    /// <summary>
    /// The harder half of the same rule. AddTaskSyncClient registers the worker
    /// for a head that opts in and deliberately registers no ITaskSyncStateStore,
    /// so on a head that has not chosen one the worker is registered and
    /// unconstructable - and asking for it throws rather than answering null. The
    /// screen still has to open.
    /// </summary>
    [Fact]
    public void A_session_that_cannot_be_constructed_hides_the_section_rather_than_failing()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true, paired: true, taskSyncEnabled: true, sessionMissingItsStore: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-status']")));

        Assert.Empty(context.Component.FindAll("[data-testid='devices-sync']"));
    }

    [Fact]
    public void Syncing_reports_what_the_exchange_did()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: true, taskSyncEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-sync']")));

        Assert.Contains(
            "not synced yet",
            context.Component.Find("[data-testid='devices-sync-result']").TextContent,
            StringComparison.OrdinalIgnoreCase);

        context.Component.Find("[data-testid='devices-sync-now']").Click();

        context.Component.WaitForAssertion(() =>
        {
            var line = context.Component.Find("[data-testid='devices-sync-result']").TextContent;
            Assert.Contains("Sent 1", line, StringComparison.Ordinal);
            Assert.Contains("received 0", line, StringComparison.Ordinal);
        });

        Assert.Contains("/api/sync/tasks", context.Service.Paths);
    }

    /// <summary>
    /// bUnit swallows what an event handler throws, so this asserts on the state
    /// a failure has to produce - the shared devices alert saying so, and the
    /// status line still on its untouched text - rather than on an exception.
    /// </summary>
    [Fact]
    public void A_replica_that_is_not_reachable_says_so_and_leaves_the_line_alone()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true,
            paired: true,
            taskSyncEnabled: true,
            respond: (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/tasks", StringComparison.Ordinal)
                ? Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "The replica is not reachable yet.")
                : Token());

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-sync']")));

        context.Component.Find("[data-testid='devices-sync-now']").Click();

        context.Component.WaitForAssertion(() =>
        {
            var message = context.Component.Find("[data-testid='devices-message']");
            Assert.Contains("not reachable", message.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("alert", message.GetAttribute("role"));
        });

        Assert.Contains(
            "not synced yet",
            context.Component.Find("[data-testid='devices-sync-result']").TextContent,
            StringComparison.OrdinalIgnoreCase);
    }

    // --- Starting over --------------------------------------------------------

    /// <summary>
    /// Both of these cost a long exchange over somebody's connection, so neither
    /// happens on one press. Cancelling has to leave the progress exactly where
    /// it was - a reset that happened anyway would be the one thing the question
    /// exists to prevent.
    /// </summary>
    [Fact]
    public void Starting_over_is_asked_before_it_is_done()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: true, taskSyncEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-sync-republish']")));

        var watermark = context.SyncState.Current.PushWatermark;

        context.Component.Find("[data-testid='devices-sync-republish']").Click();

        context.Component.WaitForAssertion(() =>
            Assert.Contains(
                "sent to the cloud again",
                context.Component.Find("[data-testid='devices-sync-reset-dialog']").TextContent,
                StringComparison.OrdinalIgnoreCase));

        context.Component.Find("[data-testid='devices-sync-reset-cancel']").Click();

        Assert.Equal(watermark, context.SyncState.Current.PushWatermark);
    }

    /// <summary>Confirming forgets how far this device has pushed, which is what
    /// makes every task on the machine - tombstones included - eligible again.
    /// <para>
    /// Asserted against the states the store was given rather than the one it
    /// ended on, for the reason
    /// <see cref="ForgetfulTaskSyncStateStore.Saved"/> gives: the cycle that
    /// carries the reset out immediately pushes what the reset made eligible,
    /// and advances the watermark again in doing so.
    /// </para></summary>
    [Fact]
    public void Republishing_resets_the_push_watermark()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: true, taskSyncEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-sync-republish']")));

        context.Component.Find("[data-testid='devices-sync-republish']").Click();
        context.Component.Find("[data-testid='devices-sync-reset-confirm']").Click();

        context.Component.WaitForAssertion(() =>
            Assert.Contains(context.SyncState.Saved, state => state.PushWatermark == DateTimeOffset.MinValue));
    }

    /// <summary>And confirming the other one forgets the feed position, which is
    /// what makes a machine that was emptied or restored read the owner's changes
    /// from the beginning again. Asserted the same way and for the same reason -
    /// the pull the reset asks for takes a fresh cursor the moment it
    /// finishes.</summary>
    [Fact]
    public void Rehydrating_clears_the_pull_cursor()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: true, taskSyncEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-sync-rehydrate']")));

        Assert.Equal("cursor-1", context.SyncState.Current.PullCursor);

        context.Component.Find("[data-testid='devices-sync-rehydrate']").Click();
        context.Component.Find("[data-testid='devices-sync-reset-confirm']").Click();

        context.Component.WaitForAssertion(() =>
            Assert.Contains(context.SyncState.Saved, state => state.PullCursor is null));
    }

    // --- A credential the service no longer knows ----------------------------

    /// <summary>
    /// The reproduction, end to end through the pipeline the app actually runs:
    /// the sync service was restarted, so it no longer knows a device this
    /// machine still holds a credential for. The token endpoint says so; every
    /// bearer call after it goes out unauthenticated and earns a bare 401 that
    /// says nothing. Before this, the panel went on reporting a healthy pairing
    /// and offered no way back but deleting the credential file by hand.
    /// </summary>
    [Fact]
    public void A_device_the_service_no_longer_knows_is_told_so_and_offered_a_way_back()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true,
            paired: true,
            withTokenPipeline: true,
            respond: (request, _) => IsTokenRequest(request)
                ? Problem(HttpStatusCode.Unauthorized, SyncErrorCodes.DeviceCredentialInvalid, "That device id and credential do not match a registered device.")
                : new HttpResponseMessage(HttpStatusCode.Unauthorized));

        OpenDevicesTab(context.Component);

        context.Component.WaitForAssertion(() =>
        {
            var notice = context.Component.Find("[data-testid='devices-unregistered']");
            Assert.Contains("no longer", notice.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("alert", notice.GetAttribute("role"));
        });

        // Both ways back in, from a device that still has a credential - which
        // is the gate that used to be StoredDevice is null.
        Assert.Single(context.Component.FindAll("[data-testid='devices-register']"));
        Assert.Single(context.Component.FindAll("[data-testid='devices-pair']"));

        // And nothing that cannot work: a device the service refuses cannot
        // invite another one.
        Assert.Empty(context.Component.FindAll("[data-testid='devices-generate-code']"));

        // The credential is still on disk. Saying the service has forgotten this
        // device is not the same as forgetting it, and only the person decides
        // the second one.
        Assert.NotNull(context.Credentials.Current);
    }

    /// <summary>
    /// The failure mode the recovery must not create. A service that cannot be
    /// reached has said nothing about this credential, and a panel that offered
    /// to re-register over it would be telling a person their pairing is gone
    /// every time a laptop woke up on a dead network.
    /// </summary>
    [Fact]
    public void A_service_that_cannot_be_reached_leaves_the_device_paired()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true,
            paired: true,
            withTokenPipeline: true,
            respond: (_, _) => throw new HttpRequestException("No route to host."));

        OpenDevicesTab(context.Component);

        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-paired']")));

        Assert.Empty(context.Component.FindAll("[data-testid='devices-unregistered']"));
        Assert.Empty(context.Component.FindAll("[data-testid='devices-register']"));
        Assert.NotNull(context.Credentials.Current);
    }

    /// <summary>
    /// The other route to the same verdict, and the one a Cosmos-backed registry
    /// will make ordinary: the token is fine, but the device behind it is gone,
    /// so the status call itself answers 401 with a code. Panel reads the code
    /// rather than the status line, because a bare 401 means something else.
    /// </summary>
    [Fact]
    public void A_status_call_that_says_the_device_is_gone_offers_a_way_back()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true,
            paired: true,
            respond: (_, _) => Problem(
                HttpStatusCode.Unauthorized,
                SyncErrorCodes.DeviceCredentialInvalid,
                "That device is no longer registered. Register or pair this device again."));

        OpenDevicesTab(context.Component);

        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-unregistered']")));

        Assert.Single(context.Component.FindAll("[data-testid='devices-register']"));
    }

    /// <summary>
    /// A bare 401 is what a request with no token at all earns, which is also
    /// what a service that was merely unreachable a moment ago produces. It is
    /// not a verdict on the credential and must not read as one.
    /// </summary>
    [Fact]
    public void A_status_call_that_merely_fails_does_not_unpair_anything()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true,
            paired: true,
            respond: (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        OpenDevicesTab(context.Component);

        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-paired']")));

        Assert.Empty(context.Component.FindAll("[data-testid='devices-unregistered']"));
        Assert.NotNull(context.Credentials.Current);
    }

    /// <summary>
    /// The escape hatch that does not depend on the service saying anything -
    /// for a credential that is stale for a reason nobody will ever be told,
    /// like a service that moved or is never coming back. Destructive, so it is
    /// asked first.
    /// </summary>
    [Fact]
    public void Forgetting_this_device_asks_first_and_then_unpairs_it()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-forget']")));

        context.Component.Find("[data-testid='devices-forget']").Click();

        // Nothing is gone yet: the question is up and the credential is intact.
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-forget-dialog']")));
        Assert.NotNull(context.Credentials.Current);

        context.Component.Find("[data-testid='devices-forget-confirm']").Click();

        context.Component.WaitForAssertion(() =>
        {
            Assert.Null(context.Credentials.Current);
            Assert.Single(context.Component.FindAll("[data-testid='devices-empty']"));
            Assert.Single(context.Component.FindAll("[data-testid='devices-register']"));
        });

        Assert.Empty(context.Component.FindAll("[data-testid='devices-generate-code']"));
    }

    [Fact]
    public void Forgetting_this_device_can_be_called_off()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-forget']")));

        context.Component.Find("[data-testid='devices-forget']").Click();
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-forget-dialog']")));

        context.Component.Find("[data-testid='devices-forget-cancel']").Click();

        context.Component.WaitForAssertion(() =>
            Assert.Empty(context.Component.FindAll("[data-testid='devices-forget-dialog']")));

        Assert.NotNull(context.Credentials.Current);
        Assert.Single(context.Component.FindAll("[data-testid='devices-paired']"));
    }

    /// <summary>
    /// An unpaired device has no credential to forget, so it is not offered the
    /// control - the only thing on the panel that could destroy something.
    /// </summary>
    [Fact]
    public void An_unpaired_device_is_not_offered_a_credential_to_forget()
    {
        using var context = RenderSettings(devicePairingEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-empty']")));

        Assert.Empty(context.Component.FindAll("[data-testid='devices-forget']"));
    }

    /// <summary>
    /// The reproduction as it actually happens after a restart, which is not
    /// quite what the test above covers. The panel opens holding a token that
    /// has not expired, so nothing goes near the token endpoint; the service
    /// signs with a new key now, so the bearer call comes back 401 with no code
    /// at all. Nothing in that exchange is a verdict on the credential.
    /// <para>
    /// So the panel asks. A status call that failed while this device holds a
    /// credential is followed by one question to the token endpoint - the only
    /// place that can answer it - and that is what produces the state.
    /// </para>
    /// </summary>
    [Fact]
    public void A_stale_token_is_chased_back_to_the_credential_that_minted_it()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true,
            paired: true,
            withTokenPipeline: true,
            respond: (_, index) => index switch
            {
                // The token cached from before the restart: minted happily.
                0 => Token(),

                // The bearer call, refused because the signing key moved. Bare:
                // no code, nothing to read.
                1 => new HttpResponseMessage(HttpStatusCode.Unauthorized),

                // The question, and the answer that settles it.
                _ => Problem(
                    HttpStatusCode.Unauthorized,
                    SyncErrorCodes.DeviceCredentialInvalid,
                    "That device id and credential do not match a registered device."),
            });

        OpenDevicesTab(context.Component);

        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-unregistered']")));

        Assert.Single(context.Component.FindAll("[data-testid='devices-register']"));
        Assert.NotNull(context.Credentials.Current);
    }

    /// <summary>
    /// The same opening move, but the service is simply gone by the time the
    /// question is asked. It has said nothing about the credential, so the
    /// device stays paired - this is the guard on the extra call the test above
    /// introduced.
    /// </summary>
    [Fact]
    public void A_stale_token_chased_to_a_service_that_is_gone_leaves_the_device_paired()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true,
            paired: true,
            withTokenPipeline: true,
            respond: (_, index) => index switch
            {
                0 => Token(),
                1 => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                _ => throw new HttpRequestException("No route to host."),
            });

        OpenDevicesTab(context.Component);

        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-paired']")));

        Assert.Empty(context.Component.FindAll("[data-testid='devices-unregistered']"));
        Assert.NotNull(context.Credentials.Current);
    }

    private static bool IsTokenRequest(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.EndsWith("/devices/token", StringComparison.Ordinal) == true;

    private static string[] SettingsTabs(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Select(button => button.TextContent.Trim()).ToArray();

    private static void OpenDevicesTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Devices").Click();

    /// <summary>The token answer, so a script that only covers the route under
    /// test still lets the authentication handler mint one.</summary>
    private static HttpResponseMessage Token() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"accessToken":"a-token","expiresAt":"{{DateTimeOffset.UtcNow.AddMinutes(30):O}}","tokenType":"Bearer"}""",
                Encoding.UTF8,
                "application/json")
        };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
        new(status)
        {
            Content = new StringContent(
                $$"""
                {"type":"https://backlog.jsdotnet.dev/problems/{{code}}","title":"Pairing failed","status":{{(int)status}},"detail":"{{detail}}","code":"{{code}}"}
                """,
                Encoding.UTF8,
                "application/problem+json")
        };

    private static SettingsRenderContext RenderSettings(
        bool devicePairingEnabled,
        bool paired = false,
        bool taskSyncEnabled = false,
        bool registerTaskSync = true,
        bool sessionMissingItsStore = false,
        Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null,
        bool withTokenPipeline = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-devices-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);
        _ = features.SetEnabled(SyncFeatures.DevicePairing, devicePairingEnabled);
        _ = features.SetEnabled(SyncFeatures.TaskSync, taskSyncEnabled);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
        _ = githubSettings.SetRepositories(repositories);
        var github = new GitHubIntegration(githubSettings, new StubGitHubClient(), new StubProbe());

        var credentials = paired
            ? new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", "a-registration-credential"))
            : new InMemoryDeviceCredentialStore();

        var service = new ScriptedSyncService(respond);

        // The real provider and handler in front of the scripted service, the
        // way the app composes them - opt-in, because every test written before
        // the credential-rejected state wants the panel's client to talk to the
        // service unauthenticated and never reach the token endpoint.
        HttpMessageHandler pipeline = service;
        SyncTokenProvider? tokens = null;
        ServiceProvider? tokenServices = null;

        if (withTokenPipeline)
        {
            var tokenCollection = new ServiceCollection();
            tokenCollection
                .AddHttpClient(SyncTokenProvider.HttpClientName, client => client.BaseAddress = new Uri("https://sync.test"))
                .ConfigurePrimaryHttpMessageHandler(() => service);

            tokenServices = tokenCollection.BuildServiceProvider();
            tokens = new SyncTokenProvider(
                tokenServices.GetRequiredService<IHttpClientFactory>(),
                credentials,
                TimeProvider.System);

            pipeline = new SyncAuthenticationHandler(tokens) { InnerHandler = service };
        }

        var http = new HttpClient(pipeline) { BaseAddress = new Uri("https://sync.test") };

        var testContext = new BunitContext();

        // CopyButton beside the pairing code reaches for the clipboard through JS.
        // Nothing here presses it, but a strict interop would fail the render.
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;

        testContext.Services.AddSingleton(store);
        testContext.Services.AddSingleton<IAppFeatureSettings>(features);
        testContext.Services.AddSingleton<ITasksRefreshSettings>(
            new TasksRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json")));
        testContext.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        testContext.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        testContext.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        testContext.Services.AddSingleton(github);
        testContext.Services.AddSingleton<FeedbackReporter>();
        testContext.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        testContext.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));
        testContext.Services.AddSingleton(new KnowledgeSourceSelection(githubSettings, new StubBranchCatalog()));
        testContext.Services.AddSingleton<IDeviceCredentialStore>(credentials);
        testContext.Services.AddSingleton(new DevicePairingClient(http, credentials));
        if (tokens is not null) testContext.Services.AddSingleton(tokens);

        // Somewhere for the session to keep a watermark, and something for the
        // two starting-over actions to be asserted against. Seeded with progress
        // rather than left at nothing, because "the watermark was reset" is only
        // visible against a watermark that was somewhere - and seeded in the past
        // rather than the future, because a watermark ahead of the one seeded
        // task would leave every push with nothing to send.
        var syncState = new ForgetfulTaskSyncStateStore(new TaskSyncState(DateTimeOffset.UnixEpoch, "cursor-1"));

        // The real session and the real worker over the same scripted wire, for
        // the reason the pairing client is real here: the interesting behaviour
        // is the exchange's as much as the screen's, and a seam between the two
        // would have tested neither.
        if (registerTaskSync)
        {
            var tasks = new OneTaskRepository();

            testContext.Services.AddSingleton(_ => new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(tasks),
                tasks,
                syncState,
                TimeProvider.System));

            // Registered by its factory rather than as an instance in the
            // unconstructable case, because that is the shape AddTaskSyncClient
            // leaves behind on a head with no sync-state store: the worker
            // resolves, its store does not, and the container throws.
            testContext.Services.AddSingleton(sp => new TaskSyncWorker(
                sp,
                features,
                credentials,
                sessionMissingItsStore
                    ? sp.GetRequiredService<ITaskSyncStateStore>()
                    : syncState,
                new FakeTimeProvider()));
        }

        var component = testContext.Render<Settings>();
        return new SettingsRenderContext(
            root, testContext, component, credentials, service, http, syncState, tokens, tokenServices);
    }

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component,
        InMemoryDeviceCredentialStore Credentials,
        ScriptedSyncService Service,
        HttpClient Http,
        ForgetfulTaskSyncStateStore SyncState,
        SyncTokenProvider? Tokens = null,
        ServiceProvider? TokenServices = null) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();
            Http.Dispose();
            Tokens?.Dispose();
            TokenServices?.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The sync service the panel talks to: registration and pairing
    /// hand back a credential, codes hand back a code, and everything else is the
    /// device's own status.</summary>
    internal sealed class ScriptedSyncService(Func<HttpRequestMessage, int, HttpResponseMessage>? respond) : HttpMessageHandler
    {
        private int _count;

        public List<string> Paths { get; } = [];

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            Paths.Add(path);

            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (respond is not null) return respond(request, _count++);

            _count++;

            if (path.EndsWith("/devices/codes", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.Created,
                    $$"""{"code":"K7MN9PQR","expiresAt":"{{DateTimeOffset.UtcNow.AddMinutes(10):O}}"}""");
            }

            if (path.EndsWith("/devices/me", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    $$"""{"ownerId":"{{Owner}}","deviceId":"{{Device}}","deviceName":"Workshop PC","pairedDeviceCount":2}""");
            }

            if (path.EndsWith("/devices/token", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    $$"""{"accessToken":"a-token","expiresAt":"{{DateTimeOffset.UtcNow.AddMinutes(30):O}}","tokenType":"Bearer"}""");
            }

            // One route, two directions: the method is what tells them apart, the
            // same way the service's own mapping does.
            if (path.EndsWith("/tasks", StringComparison.Ordinal))
            {
                return request.Method == HttpMethod.Post
                    ? Json(HttpStatusCode.OK, """{"accepted":1}""")
                    : Json(HttpStatusCode.OK, """{"tasks":[],"since":"cursor-1","hasMore":false}""");
            }

            return Json(
                HttpStatusCode.Created,
                $$"""{"ownerId":"{{Owner}}","deviceId":"{{Device}}","credential":"a-registration-credential"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    /// <summary>One task, stamped now, so a push has exactly one thing to
    /// send and the reported count is a number a test can name.</summary>
    private sealed class OneTaskRepository : ITaskRepository
    {
        private readonly Dictionary<Guid, TaskItem> _tasks;

        public OneTaskRepository()
        {
            var task = new TaskItem("Something to send", string.Empty, EntryType.Task);
            _tasks = new Dictionary<Guid, TaskItem> { [task.Id] = task };
        }

        public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            _tasks[task.Id] = task;
            return Task.CompletedTask;
        }

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tasks.TryGetValue(id, out var task) && task.DeletedAt is null ? task : null);

        public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tasks.TryGetValue(id, out var task) ? task : null);

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. _tasks.Values.Where(task => task.DeletedAt is null)]);

        // The sync read, tombstones included — which is what the push actually
        // selects through.
        public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(
                [.. _tasks.Values.Where(task => task.UpdatedAt > since).OrderBy(task => task.UpdatedAt)]);
    }

    /// <summary>Progress that lives for the render and no longer. Nothing here
    /// asserts across a restart; what the panel needs is somewhere for the
    /// session to put a watermark, and somewhere the two starting-over actions
    /// can be seen to have cleared one.</summary>
    internal sealed class ForgetfulTaskSyncStateStore(TaskSyncState? initial = null) : ITaskSyncStateStore
    {
        private readonly List<TaskSyncState> _saved = [];

        public event Action? Changed;

        public TaskSyncState Current { get; private set; } = initial ?? new(DateTimeOffset.MinValue, null);

        public string StorePath => "in memory";

        /// <summary>
        /// Every state this store was ever given, in order.
        /// <para>
        /// The two starting-over actions have to be asserted against this rather
        /// than against <see cref="Current"/>, because a reset is an event and
        /// not a resting state: the cycle that carries one out goes straight on
        /// to push and pull, so the watermark it cleared is advanced again and
        /// the cursor it forgot is replaced - by the very exchange the reset
        /// asked for. Asserting the resting state would be asserting that the
        /// reset did not work.
        /// </para>
        /// <para>
        /// Copied under the lock because the cycle that writes these runs on the
        /// thread pool while the test reads them.
        /// </para>
        /// </summary>
        public IReadOnlyList<TaskSyncState> Saved
        {
            get { lock (_saved) return [.. _saved]; }
        }

        public void Save(TaskSyncState state)
        {
            lock (_saved) _saved.Add(state);

            Current = state;
            Changed?.Invoke();
        }
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
