using Backlog.Desktop.UI.Changes;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Changes;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The answer to an agent's restore proposal (local ADR 0061 §5): Restore shows the main
/// window's Changes view on that change set's confirmation panel, and Keep — or any other
/// answer of the <c>mcp</c> source — shows nothing, so nothing is restored and nothing moves.
/// </summary>
public sealed class RestoreProposalAnswersTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Guid ChangeSetId = Guid.NewGuid();

    [Fact]
    public async Task Restore_shows_the_changes_view_on_the_change_sets_confirmation()
    {
        var (surfaces, shown) = AttachedMainWindow();

        await new RestoreProposalAnswers(surfaces).HandleAnswerAsync(Answer(RestoreProposals.Key(ChangeSetId), RestoreProposals.RestoreAnswer), Cancellation);

        var request = Assert.Single(shown);
        Assert.Equal(ChangesSurfaceQuery.SurfaceId, request.SurfaceId);
        Assert.Equal(ChangeSetId.ToString("D"), request.Value(ChangesSurfaceQuery.RestoreKey));
    }

    [Fact]
    public async Task Keep_shows_nothing()
    {
        var (surfaces, shown) = AttachedMainWindow();

        await new RestoreProposalAnswers(surfaces).HandleAnswerAsync(Answer(RestoreProposals.Key(ChangeSetId), RestoreProposals.KeepAnswer), Cancellation);

        Assert.Empty(shown);
    }

    [Fact]
    public async Task Another_asks_answer_shows_nothing_even_when_it_reads_restore()
    {
        var (surfaces, shown) = AttachedMainWindow();

        await new RestoreProposalAnswers(surfaces).HandleAnswerAsync(Answer(Guid.NewGuid().ToString("D"), RestoreProposals.RestoreAnswer), Cancellation);

        Assert.Empty(shown);
    }

    /// <summary>Delivery is never failed for want of a window: the answer is taken, so a later
    /// start does not open the view unasked.</summary>
    [Fact]
    public async Task Restore_with_no_main_window_is_still_taken()
    {
        await new RestoreProposalAnswers(new MainWindowSurfaces()).HandleAnswerAsync(
            Answer(RestoreProposals.Key(ChangeSetId), RestoreProposals.RestoreAnswer),
            Cancellation);
    }

    [Fact]
    public void It_answers_the_mcp_source()
    {
        Assert.Equal("mcp", new RestoreProposalAnswers(new MainWindowSurfaces()).Source);
    }

    private static NotificationAnswer Answer(string key, string choice) =>
        new(Guid.NewGuid(), new NotificationSource("mcp", key), Guid.NewGuid(), Guid.NewGuid(), [choice], null, DateTimeOffset.UnixEpoch);

    private static (MainWindowSurfaces Surfaces, List<SurfaceRequest> Shown) AttachedMainWindow()
    {
        var surfaces = new MainWindowSurfaces();
        var shown = new List<SurfaceRequest>();
        surfaces.Attach(request =>
        {
            shown.Add(request);
            return Task.FromResult(true);
        });

        return (surfaces, shown);
    }
}
