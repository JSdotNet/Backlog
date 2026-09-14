using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// Answers the Inbox's <see cref="IInboxPlanDrafter"/> port over the Azure
/// Foundry chat client: the one place an inbox item is turned into a request
/// the model understands, and a thrown transport failure into the
/// <c>inbox.plan.failed</c> the pane shows.
/// <para>
/// Here rather than in the Inbox module because the module may not know what
/// drafts its plans — a host with no Foundry registers nothing and the handler
/// reads "not configured". Availability is read straight off the settings
/// store on every ask, so a key entered in Settings enables "Create plan"
/// without a restart; the sentence it gives back when unavailable is the one
/// the client throws for the same case, so the button's reason and the
/// failure a stale click would hit say the same thing.
/// </para>
/// <para>
/// The lifetime is the host's decision, not this class's, because what it
/// captures — the typed chat client, and with it one <see cref="HttpClient"/>
/// — lives as long as whatever resolves it. The web harness registers it
/// Scoped: one circuit per visitor, each with the scoped handler that asks,
/// and a singleton there would hand every visitor one client for the life of
/// the process. The MAUI host registers it Singleton, because its
/// <c>InboxDesktopState</c> is one and resolves the whole chain from the root
/// once; a Scoped registration would not change that, only disguise it.
/// </para>
/// </summary>
public sealed class AzureFoundryInboxPlanDrafter(IAzureFoundryChatClient chat, AzureFoundrySettingsStore settingsStore) : IInboxPlanDrafter
{
    public bool IsAvailable => settingsStore.Current.IsConfigured;

    public string? UnavailableReason => IsAvailable ? null : AzureFoundryChatClient.PlanNotConfiguredMessage;

    public async Task<Result<InboxPlanDraftDto>> DraftAsync(InboxPlanDraftRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var response = await chat.DraftPlanAsync(
                new AzureFoundryPlanRequest(
                    request.Title,
                    request.BodyMd,
                    request.SourceUrl,
                    request.KindSlug,
                    request.Tags,
                    request.RepoIds,
                    request.PlanTag),
                cancellationToken).ConfigureAwait(false);

            return new InboxPlanDraftDto(response.PlanMarkdown);
        }
        catch (AzureFoundryException ex)
        {
            // The client's message already names the cause in a person's words
            // — which setting is missing, which status came back — so it is the
            // failure's detail rather than a rewording of it.
            return InboxErrors.PlanFailed(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            // The endpoint could not be reached at all: refused, unresolved,
            // reset. HttpClient throws rather than answering, and the pane must
            // show it the way it shows a bad status, not tear down the circuit.
            return InboxErrors.PlanFailed($"Could not reach Azure Foundry: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its timeout as a cancellation on a token that
            // is not the caller's. A genuine caller cancellation is not a bad
            // answer and propagates; anything else thrown is a bug, not a bad
            // answer, and is left to surface.
            return InboxErrors.PlanFailed("Azure Foundry did not answer before the request timed out.");
        }
    }
}
