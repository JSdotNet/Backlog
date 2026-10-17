using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>raise_impediment</c> and <c>set_impediment_state</c>, the two impediment tools beside
/// <c>set_blocked</c>: each hands its arguments to Progress's <see cref="IImpediments"/>
/// port, answers the impediment with its state as a wire token, and turns a refusal into
/// the failure a client sees.
/// </summary>
public sealed class ImpedimentToolsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Raise_hands_title_tasks_and_note_to_the_port_and_answers_the_impediment()
    {
        var port = new FakeImpediments();
        var task = Guid.NewGuid();

        var payload = await new ImpedimentTools(port).RaiseAsync("Bank sample files missing", [task], "Asked twice", Cancellation);

        Assert.Equal(new RaiseImpedimentRequest("Bank sample files missing", [task], "Asked twice").Title, port.Raised!.Title);
        Assert.Equal([task], port.Raised.HoldsBack);
        Assert.Equal("new", payload.State);
        Assert.Equal("Bank sample files missing", payload.Title);
        Assert.Equal([task], payload.HoldsBack);
        Assert.Equal("new", Assert.Single(payload.History).State);
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

        var payload = await new ImpedimentTools(port).SetStateAsync(id, token, "bank support", Cancellation);

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

        var failure = await Assert.ThrowsAsync<McpException>(() => new ImpedimentTools(port).SetStateAsync(Guid.NewGuid(), token, null, Cancellation));

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

        var failure = await Assert.ThrowsAsync<McpException>(() => new ImpedimentTools(port).SetStateAsync(Guid.NewGuid(), "reported", null, Cancellation));

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

    private static ImpedimentDto Dto(Guid id, string title, ImpedimentState state, IReadOnlyList<Guid> holds) =>
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

    public Task<Result<ImpedimentDto>> HoldBackAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<ImpedimentDto>> ReleaseAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
