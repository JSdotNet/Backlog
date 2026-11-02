using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The impediment tools (<c>.devbook/domain/progress/features.md#impediments-for-agents</c>):
/// <c>raise_impediment</c>, <c>set_impediment_state</c>, <c>link_to_impediment</c> and
/// <c>unlink_from_impediment</c>. Each hands its arguments to Progress's
/// <see cref="IImpediments"/> port, answers the impediment with its state as a wire token,
/// and turns a refusal into the failure a client sees.
/// </summary>
public sealed class ImpedimentToolsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The tools over <paramref name="port"/>, with <c>acme/payments</c> the one
    /// registered repository.</summary>
    private static ImpedimentTools Tools(FakeImpediments port) =>
        new(port, new FakeRepositoryDirectory([new TasksRepositoryRef("payments", "Acme", "Payments")]));

    [Fact]
    public async Task Raise_hands_title_tasks_and_note_to_the_port_and_answers_the_impediment()
    {
        var port = new FakeImpediments();
        var task = Guid.NewGuid();

        var payload = await Tools(port).RaiseAsync("Bank sample files missing", [task], note: "Asked twice", cancellationToken: Cancellation);

        Assert.Equal("Bank sample files missing", port.Raised!.Title);
        Assert.Equal("Asked twice", port.Raised.Note);
        Assert.Equal([HeldBackItemDto.ForTask(task)], port.Raised.HoldsBack);
        Assert.Equal("new", payload.State);
        Assert.Equal("Bank sample files missing", payload.Title);
        Assert.Equal([task], payload.HoldsBack);
        Assert.Empty(payload.PullRequests);
        Assert.Empty(payload.Plans);
        Assert.Equal("new", Assert.Single(payload.History).State);
    }

    /// <summary>Scenario "An agent raises with all three kinds": the pull request's
    /// repository is resolved through the registry, so the registered <c>owner/name</c> is
    /// what is held back; the answer keeps <c>holdsBack</c> as task ids and adds the pull
    /// requests and plans beside them.</summary>
    [Fact]
    public async Task Raise_takes_tasks_pull_requests_and_plan_tags()
    {
        var port = new FakeImpediments();
        var task = Guid.NewGuid();

        var payload = await Tools(port).RaiseAsync(
            "Bank sandbox down",
            [task],
            [new ImpedimentPullRequestArgument("acme/payments", 412)],
            ["+bank-integration"],
            cancellationToken: Cancellation);

        Assert.Equal(
            [HeldBackItemDto.ForTask(task), HeldBackItemDto.ForPullRequest("Acme/Payments", 412), HeldBackItemDto.ForPlan("+bank-integration")],
            port.Raised!.HoldsBack);

        // The answer is the port's: its tasks, pull requests and plans, each in its own list.
        Assert.Equal([task], payload.HoldsBack);
        Assert.Equal([new ImpedimentPullRequestPayload("Acme/Payments", 412)], payload.PullRequests);
        Assert.Equal(["+bank-integration"], payload.Plans);
    }

    [Fact]
    public async Task Raise_refuses_a_pull_request_in_a_repository_nobody_registered_before_the_port_is_asked()
    {
        var port = new FakeImpediments();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).RaiseAsync(
            "Bank sandbox down",
            pullRequests: [new ImpedimentPullRequestArgument("acme/unknown", 7)],
            cancellationToken: Cancellation));

        Assert.StartsWith("repository.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Null(port.Raised);
    }

    /// <summary>Scenarios "A pull request no task covers" and "A plan", through MCP: exactly
    /// one selector names the item, and the port links it.</summary>
    [Fact]
    public async Task Link_hands_the_one_item_its_selector_names_to_the_port()
    {
        var port = new FakeImpediments();
        var id = Guid.NewGuid();
        var task = Guid.NewGuid();
        var tools = Tools(port);

        await tools.LinkAsync(id, taskId: task, cancellationToken: Cancellation);
        await tools.LinkAsync(id, planTag: " +goals-standup ", cancellationToken: Cancellation);
        var payload = await tools.LinkAsync(id, repository: "ACME/payments", number: 412, cancellationToken: Cancellation);

        Assert.Equal(
            [
                (id, HeldBackItemDto.ForTask(task)),
                (id, HeldBackItemDto.ForPlan("+goals-standup")),
                (id, HeldBackItemDto.ForPullRequest("Acme/Payments", 412)),
            ],
            port.HeldBack);
        Assert.Equal([new ImpedimentPullRequestPayload("Acme/Payments", 412)], payload.PullRequests);
    }

    /// <summary>Scenario "A pull request without its repository is refused": nothing reaches
    /// the port, so the impediment is unchanged.</summary>
    [Theory]
    [InlineData(null, null, null, null, "selector.required")]
    [InlineData("goals", null, null, 412, "selector.required")]
    [InlineData(null, "00000000-0000-0000-0000-000000000001", "acme/payments", 412, "selector.required")]
    [InlineData(null, null, null, 412, "selector.incomplete")]
    [InlineData(null, null, "acme/payments", null, "selector.incomplete")]
    public async Task Link_and_unlink_take_exactly_one_whole_selector(string? planTag, string? taskId, string? repository, int? number, string code)
    {
        var port = new FakeImpediments();
        var tools = Tools(port);
        Guid? task = taskId is null ? null : Guid.Parse(taskId);

        var linked = await Assert.ThrowsAsync<McpException>(() => tools.LinkAsync(Guid.NewGuid(), planTag, task, repository, number, Cancellation));
        var unlinked = await Assert.ThrowsAsync<McpException>(() => tools.UnlinkAsync(Guid.NewGuid(), planTag, task, repository, number, Cancellation));

        Assert.StartsWith(code, linked.Message, StringComparison.Ordinal);
        Assert.StartsWith(code, unlinked.Message, StringComparison.Ordinal);
        Assert.Empty(port.HeldBack);
        Assert.Empty(port.Released);
    }

    /// <summary>Scenario "Unlinking": an unlink passes a repository the registry no longer
    /// knows on as given, so a pull request held back in it can still be let go.</summary>
    [Fact]
    public async Task Unlink_hands_the_item_to_the_port_even_in_a_repository_since_removed()
    {
        var port = new FakeImpediments();
        var id = Guid.NewGuid();
        var tools = Tools(port);

        await tools.UnlinkAsync(id, repository: "acme/payments", number: 412, cancellationToken: Cancellation);
        await tools.UnlinkAsync(id, repository: " acme/legacy-billing ", number: 77, cancellationToken: Cancellation);

        Assert.Equal(
            [(id, HeldBackItemDto.ForPullRequest("Acme/Payments", 412)), (id, HeldBackItemDto.ForPullRequest("acme/legacy-billing", 77))],
            port.Released);
    }

    [Fact]
    public async Task A_refused_link_reaches_the_client_with_its_code()
    {
        var port = new FakeImpediments { ItemAnswer = ProgressErrors.ImpedimentNotFound(Guid.Empty) };

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).LinkAsync(Guid.Empty, planTag: "goals", cancellationToken: Cancellation));

        Assert.StartsWith("progress.impediment_not_found", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reported", ImpedimentState.Reported, "reported")]
    [InlineData("Escalated", ImpedimentState.Escalated, "escalated")]
    [InlineData("being-handled", ImpedimentState.BeingHandled, "being-handled")]
    [InlineData(" resolved ", ImpedimentState.Resolved, "resolved")]
    public async Task Set_state_reads_the_wire_token_and_records_who(string token, ImpedimentState expected, string answered)
    {
        var port = new FakeImpediments();
        var id = Guid.NewGuid();

        var payload = await Tools(port).SetStateAsync(id, token, "bank support", Cancellation);

        Assert.Equal((id, expected, "bank support"), port.Moved);
        Assert.Equal(answered, payload.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("blocked")]
    [InlineData("being handled")]
    public async Task An_unknown_state_is_refused_before_the_port_is_asked(string token)
    {
        var port = new FakeImpediments();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).SetStateAsync(Guid.NewGuid(), token, null, Cancellation));

        Assert.StartsWith("progress.impediment_state_unknown", failure.Message, StringComparison.Ordinal);
        Assert.Null(port.Moved);
    }

    [Fact]
    public async Task A_refused_move_reaches_the_client_with_its_code()
    {
        var port = new FakeImpediments
        {
            MoveAnswer = ProgressErrors.ImpedimentMoveNotAllowed(ImpedimentState.Escalated, ImpedimentState.Reported)
        };

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).SetStateAsync(Guid.NewGuid(), "reported", null, Cancellation));

        Assert.StartsWith("progress.impediment_move_not_allowed", failure.Message, StringComparison.Ordinal);
    }
}

/// <summary>Progress's impediment port, recording what a tool asked of it.</summary>
internal sealed class FakeImpediments : IImpediments
{
    public event Action? Changed { add { } remove { } }

    public RaiseImpedimentRequest? Raised { get; private set; }

    public (Guid Id, ImpedimentState State, string? With)? Moved { get; private set; }

    public Error? MoveAnswer { get; init; }

    /// <summary>The refusal a link or an unlink answers, or null to take it.</summary>
    public Error? ItemAnswer { get; init; }

    public List<(Guid Id, HeldBackItemDto Item)> HeldBack { get; } = [];

    public List<(Guid Id, HeldBackItemDto Item)> Released { get; } = [];

    private static ImpedimentDto Dto(Guid id, string title, ImpedimentState state, IReadOnlyList<HeldBackItemDto> holds) =>
        new(id, title, state, null, null, null, null, [new ImpedimentStateChangeDto(state, new DateOnly(2026, 10, 17), null)], holds);

    public Task<Result<ImpedimentDto>> RaiseAsync(RaiseImpedimentRequest request, CancellationToken cancellationToken = default)
    {
        Raised = request;
        return Task.FromResult(Result.Success(Dto(Guid.NewGuid(), request.Title!, ImpedimentState.New, request.HoldsBack ?? [])));
    }

    public Task<Result<ImpedimentDto>> MoveAsync(Guid impedimentId, ImpedimentState state, string? with = null, CancellationToken cancellationToken = default)
    {
        if (MoveAnswer is { } error) return Task.FromResult(Result.Failure<ImpedimentDto>(error));

        Moved = (impedimentId, state, with);
        return Task.FromResult(Result.Success(Dto(impedimentId, "Bank sample files missing", state, [])));
    }

    public Task<Result<IReadOnlyList<ImpedimentDto>>> ListAsync(DateOnly? resolvedFrom = null, DateOnly? resolvedTo = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<ImpedimentDto>> GetAsync(Guid impedimentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<ImpedimentDto>> SetCheckAgainAsync(Guid impedimentId, DateOnly? checkAgain, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<ImpedimentDto>> EditNoteAsync(Guid impedimentId, string? note, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<ImpedimentDto>> HoldBackAsync(Guid impedimentId, HeldBackItemDto item, CancellationToken cancellationToken = default)
    {
        if (ItemAnswer is { } error) return Task.FromResult(Result.Failure<ImpedimentDto>(error));

        HeldBack.Add((impedimentId, item));
        return Task.FromResult(Result.Success(Dto(impedimentId, "Bank sample files missing", ImpedimentState.New, [item])));
    }

    public Task<Result<ImpedimentDto>> ReleaseAsync(Guid impedimentId, HeldBackItemDto item, CancellationToken cancellationToken = default)
    {
        if (ItemAnswer is { } error) return Task.FromResult(Result.Failure<ImpedimentDto>(error));

        Released.Add((impedimentId, item));
        return Task.FromResult(Result.Success(Dto(impedimentId, "Bank sample files missing", ImpedimentState.New, [])));
    }
}
