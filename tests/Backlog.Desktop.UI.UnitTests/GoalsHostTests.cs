using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.UI.Goals;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Progress;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Goals view's host against the Progress ports
/// (<c>.devbook/domain/progress/features.md#the-goals-view</c>): what it reads and when,
/// and each write it makes — the iteration, New goal, attach with the other-goal question,
/// and an impediment's move — with the module's refusals shown where the person acted.
/// The ports are doubles at the module's boundary; the shell's candidates are handed in.
/// </summary>
public sealed class GoalsHostTests : IDisposable
{
    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static readonly Guid PaidId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SearchId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Vat = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid BankId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    private readonly BunitContext _context = new();
    private readonly FakeStandUp _standUp = new();
    private readonly FakeGoals _goals = new();
    private readonly FakeImpediments _impediments = new();
    private readonly FakeIterationSettings _settings = new();
    private readonly FakePullRequests _pulls = new();

    private GoalsCandidates _candidates = new(
        [new GoalsTaskCandidate(Vat, "VAT rate per line item", GoalTaskStatus.Doing, [], [], true)],
        ["bank-integration"],
        [new GoalsPullRequestCandidate("JSdotNet/Backlog", 1357, "Payment status webhook", true)]);

    public GoalsHostTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton(TimeProvider.System);
        _context.Services.AddSingleton<IStandUp>(_standUp);
        _context.Services.AddSingleton<IGoals>(_goals);
        _context.Services.AddSingleton<IImpediments>(_impediments);
        _context.Services.AddSingleton<IIterationSettings>(_settings);
        _context.Services.AddSingleton<IGoalPullRequestReader>(_pulls);
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<GoalsHost> Render(List<bool>? reads = null) =>
        _context.Render<GoalsHost>(p => p
            .Add(c => c.Candidates, () => _candidates)
            .Add(c => c.OnRead, (bool refresh) => reads?.Add(refresh)));

    [Fact]
    public void The_view_reads_the_stand_up_and_the_goals_over_the_last_window_and_draws_the_board()
    {
        var reads = new List<bool>();
        var host = Render(reads);

        host.WaitForAssertion(() =>
        {
            Assert.Equal("12–16 Oct → 19–23 Oct", host.Find("[data-testid='goals-board-window']").TextContent);
            Assert.Equal("Customers get paid faster", host.Find($"[data-testid='goals-board-goal-{PaidId}'] h3").TextContent.Trim());
            Assert.NotNull(host.Find("aside[aria-label='Stand-up']"));
        });

        Assert.Equal((Oct(12), Oct(16)), _goals.ListedOver);
        Assert.Equal([false], reads);
    }

    [Fact]
    public void Work_in_progress_no_goal_holds_is_listed_under_no_goal()
    {
        var host = Render();

        host.WaitForAssertion(() =>
        {
            var noGoal = host.Find("[data-testid='goals-board-no-goal']");
            Assert.Contains("VAT rate per line item", noGoal.TextContent);
            Assert.Contains("Payment status webhook", noGoal.TextContent);
        });
    }

    [Fact]
    public void With_no_goals_and_nothing_outside_one_the_view_is_a_real_empty_state()
    {
        _standUp.Reading = _standUp.Reading with { Goals = [] };
        _goals.Summaries = [];
        _candidates = GoalsCandidates.Empty;

        var host = Render();

        host.WaitForAssertion(() =>
        {
            var empty = host.Find("[data-testid='goals-view-empty']");
            Assert.Equal("No goals yet", empty.QuerySelector(".empty-state__title")!.TextContent);
            Assert.Empty(host.FindAll("[data-testid='goals-view-unavailable']"));
        });
    }

    [Fact]
    public void A_stand_up_the_module_refuses_to_read_says_why_and_offers_another_try()
    {
        _standUp.Failure = Error.Validation("progress.plan_tag_required", "The stand-up could not be read.");

        var host = Render();

        host.WaitForAssertion(() => Assert.Contains("The stand-up could not be read.", host.Find("[data-testid='goals-host-error']").TextContent));

        _standUp.Failure = null;
        host.Find("[data-testid='goals-host-error'] button").Click();

        host.WaitForAssertion(() => Assert.NotNull(host.Find("[data-testid='goals-board']")));
    }

    [Fact]
    public void Picking_an_iteration_sets_the_persons_length_from_today_and_reads_again()
    {
        var host = Render();
        host.WaitForAssertion(() => host.Find("[data-testid='goals-board-iteration-month']"));
        var before = _standUp.Reads;

        host.Find("[data-testid='goals-board-iteration-month']").Click();

        host.WaitForAssertion(() =>
        {
            Assert.Equal(IterationLength.Month, _settings.Length);
            Assert.Equal(DateOnly.FromDateTime(DateTime.Now), _settings.SetOn);
            Assert.True(_standUp.Reads > before);
        });
    }

    [Fact]
    public void Refresh_reads_the_shell_and_the_view_again()
    {
        var reads = new List<bool>();
        var host = Render(reads);
        host.WaitForAssertion(() => host.Find("[data-testid='goals-board-refresh']"));
        var before = _standUp.Reads;

        host.Find("[data-testid='goals-board-refresh']").Click();

        host.WaitForAssertion(() =>
        {
            Assert.Equal([false, true], reads);
            Assert.True(_standUp.Reads > before);
        });
    }

    [Fact]
    public void New_goal_creates_it_through_the_module_and_keeps_a_refusal_in_the_dialog()
    {
        var host = Render();
        host.WaitForAssertion(() => host.Find("[data-testid='goals-board-new-goal']"));

        host.Find("[data-testid='goals-board-new-goal']").Click();
        host.WaitForAssertion(() => host.Find("[data-testid='goal-create-dialog-title'] input"));
        host.Find("[data-testid='goal-create-dialog-title'] input").Input("Phone capture in daily use");
        _goals.CreateFailure = Error.Validation("progress.goal_without_due_date", "A goal needs a due date.");
        host.Find("[data-testid='goal-create-dialog-save']").Click();

        host.WaitForAssertion(() =>
            Assert.Equal("A goal needs a due date.", host.Find("[data-testid='goal-create-dialog-error']").TextContent.Trim()));

        _goals.CreateFailure = null;
        host.Find("[data-testid='goal-create-dialog-due'] input").Input("2026-11-30");
        host.Find("[data-testid='goal-create-dialog-save']").Click();

        host.WaitForAssertion(() =>
        {
            Assert.Equal(new SetGoalRequest("Phone capture in daily use", null, new DateOnly(2026, 11, 30)), _goals.Created.Last());
            Assert.Empty(host.FindAll("[data-testid='goal-create-dialog-title']"));
        });
    }

    [Fact]
    public void Attaching_from_no_goal_attaches_to_the_open_goal()
    {
        var host = Render();
        host.WaitForAssertion(() => host.Find($"[data-testid='goals-board-attach-task:{Vat}']"));

        host.Find($"[data-testid='goals-board-attach-task:{Vat}']").Click();

        host.WaitForAssertion(() =>
            Assert.Equal((PaidId, GoalItemDto.ForTask(Vat), false), Assert.Single(_goals.Attached)));
    }

    [Fact]
    public void An_item_another_goal_holds_asks_first_and_moves_on_confirm()
    {
        _goals.HeldBy = "Devbook search ships";
        var host = Render();
        host.WaitForAssertion(() => host.Find($"[data-testid='goals-board-attach-task:{Vat}']"));

        host.Find($"[data-testid='goals-board-attach-task:{Vat}']").Click();

        host.WaitForAssertion(() =>
            Assert.Contains("Already in Devbook search ships. Move it to Customers get paid faster?", host.Find("[data-testid='goals-move-confirm']").TextContent));

        host.Find("[data-testid='goals-move-confirm-yes']").Click();

        host.WaitForAssertion(() =>
        {
            Assert.Equal(
                [(PaidId, GoalItemDto.ForTask(Vat), false), (PaidId, GoalItemDto.ForTask(Vat), true)],
                _goals.Attached);
            Assert.Empty(host.FindAll("[data-testid='goals-move-confirm']"));
        });
    }

    [Fact]
    public void Picking_a_state_moves_the_impediment_with_no_person()
    {
        var host = Render();
        var picker = $"[data-testid='goals-board-stand-up-impediment-{BankId}-state'] select";
        host.WaitForAssertion(() => host.Find(picker));

        host.Find(picker).Change(nameof(StandUpImpedimentState.BeingHandled));

        host.WaitForAssertion(() =>
            Assert.Equal((BankId, ImpedimentState.BeingHandled, (string?)null), Assert.Single(_impediments.Moves)));
    }

    [Fact]
    public void A_refused_move_shows_why_and_the_picker_goes_back_to_the_stored_state()
    {
        _impediments.Failure = Error.Validation("progress.impediment_move_not_allowed", "Escalated cannot move to New.");
        var host = Render();
        var picker = $"[data-testid='goals-board-stand-up-impediment-{BankId}-state'] select";
        host.WaitForAssertion(() => host.Find(picker));

        host.Find(picker).Change(nameof(StandUpImpedimentState.Resolved));

        host.WaitForAssertion(() =>
        {
            Assert.Equal("Escalated cannot move to New.", host.Find("[data-testid='goals-board-error']").TextContent.Trim());
            Assert.Equal(nameof(StandUpImpedimentState.Escalated), host.Find(picker).GetAttribute("value"));
        });
    }

    [Fact]
    public void A_change_the_module_reports_reads_the_view_again()
    {
        var host = Render();
        host.WaitForAssertion(() => host.Find("[data-testid='goals-board']"));
        var before = _standUp.Reads;

        _goals.RaiseChanged();
        _impediments.RaiseChanged();
        _settings.RaiseChanged();

        host.WaitForAssertion(() => Assert.True(_standUp.Reads >= before + 3));
    }

    [Fact]
    public async Task Disposing_the_view_stops_listening()
    {
        var host = Render();
        host.WaitForAssertion(() => host.Find("[data-testid='goals-board']"));

        await _context.DisposeComponentsAsync();

        Assert.Equal(0, _goals.Listeners);
        Assert.Equal(0, _impediments.Listeners);
        Assert.Equal(0, _settings.Listeners);
    }

    [Fact]
    public void Planned_and_goal_pull_requests_are_read_once_and_drawn_as_status_lines()
    {
        var host = Render();

        host.WaitForAssertion(() =>
            Assert.NotNull(host.Find("[data-testid='goals-board-stand-up-planned'] .pull-request-status [data-mark='checks']")));
        Assert.Equal(["1357"], _pulls.Asked.SelectMany(batch => batch).Select(pull => pull.Number).Distinct());
    }

    [Fact]
    public void Without_the_goals_module_the_view_says_so()
    {
        using var bare = new BunitContext();

        var host = bare.Render<GoalsHost>();

        Assert.Equal("Goals are not set up here", host.Find("[data-testid='goals-view-unavailable'] .empty-state__title").TextContent);
        Assert.Empty(host.FindAll("[data-testid='goals-view-empty']"));
    }

    /// <summary>
    /// The save of a move takes a while — the real module writes a file — and the picker
    /// the person is on stays enabled throughout, so a keyboard user's focus never drops to
    /// the page. The rest of the view waits.
    /// </summary>
    [Fact]
    public void While_a_move_is_saved_the_picker_being_moved_stays_enabled()
    {
        var gate = new TaskCompletionSource();
        _impediments.Gate = gate.Task;
        _standUp.Reading = _standUp.Reading with
        {
            Impediments =
            [
                .. _standUp.Reading.Impediments,
                new StandUpImpedimentDto(OtherId, "Android build runner flaky", ImpedimentState.Reported, "Platform team", 4, null, null, null)
            ]
        };
        var host = Render();
        var picker = $"[data-testid='goals-board-stand-up-impediment-{BankId}-state'] select";
        var other = $"[data-testid='goals-board-stand-up-impediment-{OtherId}-state'] select";
        host.WaitForAssertion(() => host.Find(picker));

        host.Find(picker).Change(nameof(StandUpImpedimentState.BeingHandled));

        host.WaitForAssertion(() =>
        {
            Assert.Single(_impediments.Moves);
            Assert.True(host.Find("[data-testid='goals-board-refresh']").HasAttribute("disabled"));
            Assert.False(host.Find(picker).HasAttribute("disabled"));
            Assert.True(host.Find(other).HasAttribute("disabled"));
        });

        gate.SetResult();

        host.WaitForAssertion(() =>
        {
            Assert.False(host.Find("[data-testid='goals-board-refresh']").HasAttribute("disabled"));
            Assert.False(host.Find(other).HasAttribute("disabled"));
        });
    }

    /// <summary>A write the module answers with Changed is read once, not once for the
    /// write and again for the event.</summary>
    [Fact]
    public void A_write_is_read_once_even_when_the_module_reports_it_changed()
    {
        _impediments.RaiseOnMove = true;
        var host = Render();
        var picker = $"[data-testid='goals-board-stand-up-impediment-{BankId}-state'] select";
        host.WaitForAssertion(() => host.Find(picker));
        var before = _standUp.Reads;

        host.Find(picker).Change(nameof(StandUpImpedimentState.BeingHandled));

        host.WaitForAssertion(() => Assert.Single(_impediments.Moves));
        host.WaitForAssertion(() => Assert.Equal(before + 1, _standUp.Reads));
        Assert.Equal(1, _pulls.Asked.Count - 1);
    }

    // ---- Doubles at the Progress module's boundary ------------------------------------

    internal sealed class FakeStandUp : IStandUp
    {
        public StandUpDto Reading { get; set; } = new(
            new IterationWindowDto(IterationLength.Week, Oct(12), Oct(16)),
            new IterationWindowDto(IterationLength.Week, Oct(19), Oct(23)),
            [
                new StandUpGoalDto(PaidId, "Customers get paid faster", Oct(30), new GoalProgressDto(9, 15, 3, 1, 2, 3), GoalStanding.AtRisk, null),
                new StandUpGoalDto(SearchId, "Devbook search ships", Oct(23), new GoalProgressDto(5, 6, 0, 0, 1, 0), GoalStanding.OnTrack, null)
            ],
            [],
            [new StandUpGroupDto(PaidId, "Customers get paid faster",
                [new StandUpItemDto(GoalAttachmentKind.PullRequest, null, "Payment status webhook", "JSdotNet/Backlog", 1357, 3, null, null)])],
            3,
            18,
            [new StandUpImpedimentDto(BankId, "Bank sample files missing", ImpedimentState.Escalated, "Account manager", 3, null, PaidId, "Customers get paid faster")],
            0);

        public Error? Failure { get; set; }

        public int Reads { get; private set; }

        public Task<Result<StandUpDto>> ReadAsync(DateOnly today, IterationLength length, CancellationToken cancellationToken = default) =>
            ReadAsync(cancellationToken);

        public Task<Result<StandUpDto>> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Failure is { } error ? Result.Failure<StandUpDto>(error) : Result.Success(Reading));
        }
    }

    internal sealed class FakeGoals : IGoals
    {
        private Action? _changed;

        public event Action? Changed
        {
            add { _changed += value; Listeners++; }
            remove { _changed -= value; Listeners--; }
        }

        public int Listeners { get; private set; }

        public IReadOnlyList<GoalSummaryDto> Summaries { get; set; } =
        [
            new(new GoalDto(PaidId, "Customers get paid faster", null, Oct(30), []), new GoalProgressDto(9, 15, 3, 1, 2, 3)),
            new(new GoalDto(SearchId, "Devbook search ships", null, Oct(23), []), new GoalProgressDto(5, 6, 0, 0, 1, 0))
        ];

        public (DateOnly?, DateOnly?) ListedOver { get; private set; }

        public List<SetGoalRequest> Created { get; } = [];

        public Error? CreateFailure { get; set; }

        public List<(Guid, GoalItemDto, bool)> Attached { get; } = [];

        /// <summary>The goal that holds whatever is attached without moving it.</summary>
        public string? HeldBy { get; set; }

        public void RaiseChanged() => _changed?.Invoke();

        public Task<Result<IReadOnlyList<GoalSummaryDto>>> ListAsync(DateOnly? windowStart = null, DateOnly? windowEnd = null, CancellationToken cancellationToken = default)
        {
            ListedOver = (windowStart, windowEnd);
            return Task.FromResult(Result.Success(Summaries));
        }

        public Task<Result<GoalDto>> CreateAsync(SetGoalRequest request, CancellationToken cancellationToken = default)
        {
            Created.Add(request);
            return Task.FromResult(CreateFailure is { } error
                ? Result.Failure<GoalDto>(error)
                : Result.Success(new GoalDto(Guid.NewGuid(), request.Title, request.Outcome, request.DueDate ?? Oct(30), [])));
        }

        public Task<Result<GoalDto>> AttachAsync(Guid goalId, GoalItemDto item, bool moveFromOtherGoal = false, CancellationToken cancellationToken = default)
        {
            Attached.Add((goalId, item, moveFromOtherGoal));
            return Task.FromResult(HeldBy is { } other && !moveFromOtherGoal
                ? Result.Failure<GoalDto>(ProgressErrors.ItemInOtherGoal(other))
                : Result.Success(new GoalDto(goalId, "Customers get paid faster", null, Oct(30), [new GoalAttachmentDto(item, Oct(19))])));
        }

        public Task<Result<GoalDto>> GetAsync(Guid goalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<GoalDto?>> FindGoalOfAsync(GoalItemDto item, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<GoalDto>> EditAsync(Guid goalId, SetGoalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid goalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<GoalDto>> DetachAsync(Guid goalId, GoalItemDto item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class FakeImpediments : IImpediments
    {
        private Action? _changed;

        public event Action? Changed
        {
            add { _changed += value; Listeners++; }
            remove { _changed -= value; Listeners--; }
        }

        public int Listeners { get; private set; }

        public List<(Guid, ImpedimentState, string?)> Moves { get; } = [];

        public Error? Failure { get; set; }

        /// <summary>A save that completes only when the test says so.</summary>
        public Task? Gate { get; set; }

        /// <summary>Raise Changed from inside the save, as the module does.</summary>
        public bool RaiseOnMove { get; set; }

        public void RaiseChanged() => _changed?.Invoke();

        public async Task<Result<ImpedimentDto>> MoveAsync(Guid impedimentId, ImpedimentState state, string? with = null, CancellationToken cancellationToken = default)
        {
            Moves.Add((impedimentId, state, with));
            if (Gate is { } gate) await gate;
            if (Failure is { } error) return Result.Failure<ImpedimentDto>(error);

            if (RaiseOnMove) _changed?.Invoke();
            return Result.Success(new ImpedimentDto(
                impedimentId, "Bank sample files missing", state, with, null, null, null,
                [new ImpedimentStateChangeDto(state, Oct(19), with)], []));
        }

        public Task<Result<IReadOnlyList<ImpedimentDto>>> ListAsync(DateOnly? resolvedFrom = null, DateOnly? resolvedTo = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> GetAsync(Guid impedimentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> RaiseAsync(RaiseImpedimentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> SetCheckAgainAsync(Guid impedimentId, DateOnly? checkAgain, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> EditNoteAsync(Guid impedimentId, string? note, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> HoldBackAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> ReleaseAsync(Guid impedimentId, Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class FakeIterationSettings : IIterationSettings
    {
        private Action? _changed;

        public event Action? Changed
        {
            add { _changed += value; Listeners++; }
            remove { _changed -= value; Listeners--; }
        }

        public int Listeners { get; private set; }

        public IterationLength Length { get; private set; } = IterationLength.Week;

        public DateOnly? TwoWeekStart { get; private set; }

        public DateOnly? SetOn { get; private set; }

        public void RaiseChanged() => _changed?.Invoke();

        public void SetLength(IterationLength length, DateOnly today)
        {
            Length = length;
            SetOn = today;
        }

        public void SetTwoWeekStart(DateOnly start) => TwoWeekStart = start;
    }

    internal sealed class FakePullRequests : IGoalPullRequestReader
    {
        public List<IReadOnlyList<PullRequestRefDto>> Asked { get; } = [];

        public Task<IReadOnlyList<GoalPullRequestDto>> ReadAsync(IReadOnlyList<PullRequestRefDto> pullRequests, CancellationToken cancellationToken = default)
        {
            Asked.Add(pullRequests);
            IReadOnlyList<GoalPullRequestDto> read =
            [
                .. pullRequests.Select(pull => new GoalPullRequestDto(
                    pull.Repository, pull.Number, LinkedPullRequestState.Open, null,
                    new GoalPullRequestChecksDto(9, 0, 5, []), new GoalPullRequestApprovalsDto(1, 2)))
            ];
            return Task.FromResult(read);
        }
    }
}
