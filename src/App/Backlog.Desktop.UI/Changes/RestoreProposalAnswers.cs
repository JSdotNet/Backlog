using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Changes;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Desktop.UI.Changes;

/// <summary>
/// Answers an agent's restore proposal (local ADR 0061 §5): the <c>propose_restore</c>
/// question, which the <c>mcp</c> source asks with a key naming the change set
/// (<see cref="RestoreProposals"/>).
/// <list type="bullet">
/// <item><b>Restore</b> shows the main window's Changes view on that change set's
/// confirmation panel. It restores nothing: the person still confirms or
/// cancels there.</item>
/// <item><b>Keep</b>, and every other <c>mcp</c> answer, does nothing. The asking session
/// reads the answer with <c>get_answer</c>, as it reads any <c>ask</c>.</item>
/// </list>
/// <para>
/// A second handler for the <c>mcp</c> source beside <see cref="McpNotificationAnswers"/>,
/// which takes every answer; delivery hands an answer to every handler of its source.
/// Here rather than in the MCP project because what shows a view is the shell's.
/// </para>
/// <para>
/// <b>Never fails delivery.</b> With no main window to show the view in — the app is
/// starting, or the answer is replayed before the shell attached — the answer is still
/// taken and the jump is logged as not made, so a later start does not open the view
/// unasked. The person finds the change set in the Changes view themselves.
/// </para>
/// </summary>
/// <para>
/// <b>It does not bring the main window forward.</b> The shell's windows are scoped per
/// window in the web harness, and this handler is a singleton; a person who answered in the
/// app's own notification centre is looking at the main window already.
/// </para>
/// <param name="surfaces">The main window's shell, which shows the view.</param>
public sealed class RestoreProposalAnswers(
    MainWindowSurfaces surfaces,
    ILogger<RestoreProposalAnswers>? logger = null) : INotificationAnswerHandler
{
    private readonly ILogger _logger = logger ?? NullLogger<RestoreProposalAnswers>.Instance;

    public string Source => RestoreProposals.Source;

    public async Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (RestoreProposals.ChangeSetOf(answer.Source.Key) is not { } changeSetId) return;
        if (!answer.ChoiceIds.Contains(RestoreProposals.RestoreAnswer, StringComparer.Ordinal)) return;

        var shown = await surfaces.ShowAsync(new SurfaceRequest(
            ChangesSurfaceQuery.SurfaceId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ChangesSurfaceQuery.RestoreKey] = changeSetId.ToString("D"),
            })).ConfigureAwait(false);

        if (!shown)
        {
            _logger.LogInformation(
                "Restore proposal {NotificationId} was answered Restore, but no main window showed the Changes view for change set {ChangeSetId}.",
                answer.NotificationId,
                changeSetId);
        }
    }
}
