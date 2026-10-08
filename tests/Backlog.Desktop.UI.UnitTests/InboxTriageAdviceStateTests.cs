using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox state's AI triage (local ADR 0023): it asks only while the
/// advisor can run, only for an item opened in triage, once per item per app
/// session — and whenever the advisor is off, fails or throws, it exposes no
/// advice, so the pane has nothing to draw.
/// </summary>
public sealed class InboxTriageAdviceStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-triage-state-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Without_an_available_advisor_nothing_is_asked_and_nothing_is_exposed()
    {
        var (inbox, state, item) = await Triaging();
        inbox.TriageAdvisorAvailable = false;

        await state.LoadTriageAdviceAsync();
        var proposed = await state.ProposeTriagePassAsync();

        Assert.False(state.TriageAdvisorAvailable);
        Assert.Empty(inbox.AdviceRequests);
        Assert.Empty(inbox.PassRequests);
        Assert.False(proposed);
        Assert.Null(state.TriageAdvice);
        Assert.Null(state.TriagePass);
        Assert.Null(state.TriagePassError);
        Assert.Null(state.TriageAdviceFor(item.Id));
    }

    [Fact]
    public async Task Opening_an_item_in_triage_asks_once_with_the_configured_repositories()
    {
        var (inbox, state, item) = await Triaging();
        inbox.TriageAdvice[item.Id] = Result.Success(Advice(item.Id));

        await state.LoadTriageAdviceAsync();
        await state.LoadTriageAdviceAsync();

        var request = Assert.Single(inbox.AdviceRequests);
        Assert.Equal(item.Id, request.Id);
        Assert.Equal(["acme/web"], request.Repositories);
        Assert.Equal("Fix the login page", state.TriageAdvice?.Duplicate?.TargetTitle);
    }

    [Fact]
    public async Task Outside_triage_mode_nothing_is_asked()
    {
        var (inbox, state, _) = await Triaging();
        state.SetTriageMode(false);

        await state.LoadTriageAdviceAsync();

        Assert.Empty(inbox.AdviceRequests);
    }

    [Fact]
    public async Task A_failed_call_exposes_no_advice_and_is_not_asked_again()
    {
        var (inbox, state, item) = await Triaging();
        inbox.TriageAdvice[item.Id] = Result.Failure<InboxTriageAdviceDto>(InboxErrors.TriageFailed("Azure Foundry returned 503: busy"));

        await state.LoadTriageAdviceAsync();
        await state.LoadTriageAdviceAsync();

        Assert.Single(inbox.AdviceRequests);
        Assert.Null(state.TriageAdvice);
    }

    [Fact]
    public async Task A_call_that_throws_exposes_no_advice()
    {
        var (inbox, state, _) = await Triaging();
        inbox.TriageThrows = new InvalidOperationException("boom");

        await state.LoadTriageAdviceAsync();

        Assert.Null(state.TriageAdvice);
    }

    [Fact]
    public async Task Advice_already_held_is_hidden_once_the_advisor_goes_away()
    {
        var (inbox, state, item) = await Triaging();
        inbox.TriageAdvice[item.Id] = Result.Success(Advice(item.Id));
        await state.LoadTriageAdviceAsync();
        Assert.NotNull(state.TriageAdvice);

        inbox.TriageAdvisorAvailable = false;

        Assert.Null(state.TriageAdvice);
    }

    [Fact]
    public async Task An_answer_with_no_card_exposes_none()
    {
        var (_, state, _) = await Triaging();

        await state.LoadTriageAdviceAsync();

        Assert.Null(state.TriageAdvice);
    }

    [Fact]
    public async Task The_pass_asks_for_the_unprocessed_items_of_the_slice_and_holds_the_answer()
    {
        var (inbox, state, item) = await Triaging();
        var pass = InboxTriagePassDto.Nothing with { Unplaced = [item.Id] };
        inbox.TriagePassAnswer = Result.Success(pass);

        var proposed = await state.ProposeTriagePassAsync();

        Assert.True(proposed);
        var request = Assert.Single(inbox.PassRequests);
        Assert.Equal([item.Id], request.Ids);
        Assert.Equal(["acme/web"], request.Repositories);
        Assert.Same(pass, state.TriagePass);

        state.DismissTriagePass();
        Assert.Null(state.TriagePass);
    }

    [Fact]
    public async Task A_failed_pass_leaves_no_pass_and_one_sentence()
    {
        var (inbox, state, _) = await Triaging();
        inbox.TriagePassAnswer = Result.Failure<InboxTriagePassDto>(InboxErrors.TriageFailed("Azure Foundry returned 503: busy"));

        var proposed = await state.ProposeTriagePassAsync();

        Assert.False(proposed);
        Assert.Null(state.TriagePass);
        Assert.Equal("Azure Foundry returned 503: busy", state.TriagePassError);
        Assert.False(state.TriagePassRunning);
    }

    [Fact]
    public async Task A_pass_that_throws_leaves_no_pass()
    {
        var (inbox, state, _) = await Triaging();
        inbox.TriageThrows = new HttpRequestException("refused");

        var proposed = await state.ProposeTriagePassAsync();

        Assert.False(proposed);
        Assert.Null(state.TriagePass);
        Assert.NotNull(state.TriagePassError);
    }

    [Fact]
    public async Task A_duplicate_of_a_capture_decided_since_is_no_longer_shown()
    {
        var (inbox, state, item) = await Triaging();
        var twin = inbox.Seed("Login page server error again");
        await state.ReloadAsync();
        inbox.TriageAdvice[item.Id] = Result.Success(new InboxTriageAdviceDto(
            item.Id,
            new InboxTriageDuplicateDto(InboxTriageTargetKind.InboxItem, twin.Id, twin.Title, "Same capture."),
            null,
            []));
        await state.LoadTriageAdviceAsync();
        Assert.NotNull(state.TriageAdvice);

        await inbox.ArchiveAsync(twin.Id, TestContext.Current.CancellationToken);
        await state.ReloadAsync();

        Assert.Null(state.TriageAdvice);
    }

    [Fact]
    public async Task A_pass_put_away_while_it_was_out_does_not_come_back()
    {
        var (inbox, state, item) = await Triaging();
        inbox.TriagePassAnswer = Result.Success(InboxTriagePassDto.Nothing with { Unplaced = [item.Id] });
        var gate = new TaskCompletionSource();
        inbox.BeforeTriagePass = () => gate.Task;

        var asking = state.ProposeTriagePassAsync();
        state.DismissTriagePass();
        gate.SetResult();

        Assert.False(await asking);
        Assert.Null(state.TriagePass);
        Assert.False(state.TriagePassRunning);
    }

    [Fact]
    public async Task Leaving_triage_puts_the_pass_away()
    {
        var (inbox, state, item) = await Triaging();
        inbox.TriagePassAnswer = Result.Success(InboxTriagePassDto.Nothing with { Unplaced = [item.Id] });
        await state.ProposeTriagePassAsync();
        Assert.NotNull(state.TriagePass);

        state.SetTriageMode(false);

        Assert.Null(state.TriagePass);
    }

    [Fact]
    public async Task A_pass_error_is_hidden_with_the_advisor()
    {
        var (inbox, state, _) = await Triaging();
        inbox.TriagePassAnswer = Result.Failure<InboxTriagePassDto>(InboxErrors.TriageFailed("busy"));
        await state.ProposeTriagePassAsync();
        Assert.NotNull(state.TriagePassError);

        inbox.TriageAdvisorAvailable = false;

        Assert.Null(state.TriagePassError);
    }

    private async Task<(FakeInboxItems Inbox, InboxDesktopState State, InboxItemDto Item)> Triaging()
    {
        var inbox = new FakeInboxItems { TriageAdvisorAvailable = true };
        var item = inbox.Seed("Login page returns a server error");
        inbox.Seed("Already decided", status: InboxStatus.Deferred);

        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Assert.Null(settings.SetRepositories([new GitHubRepositoryRef("web", "acme", "web")]));

        var state = new InboxDesktopState(inbox, settings);
        await state.ReloadAsync();
        state.SelectItem(item.Id);
        state.SetTriageMode(true);

        return (inbox, state, item);
    }

    private static InboxTriageAdviceDto Advice(Guid itemId) => new(
        itemId,
        new InboxTriageDuplicateDto(InboxTriageTargetKind.Task, Guid.NewGuid(), "Fix the login page", "It asks for the same fix."),
        null,
        ["acme/web"]);
}
