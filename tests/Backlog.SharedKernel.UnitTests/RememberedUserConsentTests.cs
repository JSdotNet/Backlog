using Microsoft.Extensions.Time.Testing;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// One Windows Hello confirmation holds for five minutes (local ADR 0058 §6): an act the person
/// starts within the window of a Verified answer is not asked again; a Declined or unavailable
/// answer is never remembered; and <see cref="IUserConsent.VerifyEveryTimeAsync"/> always asks
/// and never opens the window.
/// </summary>
public sealed class RememberedUserConsentTests
{
    private const string Prompt = "Revoke a device";

    [Fact]
    public async Task The_first_check_asks()
    {
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);
        var consent = new RememberedUserConsent(inner, new FakeTimeProvider());

        var outcome = await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);

        Assert.Equal(UserConsentOutcome.Verified, outcome);
        Assert.Equal(1, inner.Asked);
    }

    [Fact]
    public async Task A_check_within_five_minutes_of_a_confirmation_is_not_asked()
    {
        var clock = new FakeTimeProvider();
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);
        var consent = new RememberedUserConsent(inner, clock);

        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);
        inner.Next = UserConsentOutcome.Declined;
        clock.Advance(RememberedUserConsent.DefaultWindow - TimeSpan.FromSeconds(1));

        var outcome = await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);

        Assert.Equal(UserConsentOutcome.Verified, outcome);
        Assert.Equal(1, inner.Asked);
    }

    [Fact]
    public async Task A_check_five_minutes_after_a_confirmation_asks_again()
    {
        var clock = new FakeTimeProvider();
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);
        var consent = new RememberedUserConsent(inner, clock);

        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);
        inner.Next = UserConsentOutcome.Declined;
        clock.Advance(RememberedUserConsent.DefaultWindow);

        var outcome = await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);

        Assert.Equal(UserConsentOutcome.Declined, outcome);
        Assert.Equal(2, inner.Asked);
    }

    [Fact]
    public async Task A_confirmation_after_the_window_opens_a_new_one()
    {
        var clock = new FakeTimeProvider();
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);
        var consent = new RememberedUserConsent(inner, clock);

        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);
        clock.Advance(RememberedUserConsent.DefaultWindow);
        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);
        clock.Advance(RememberedUserConsent.DefaultWindow - TimeSpan.FromSeconds(1));

        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);

        Assert.Equal(2, inner.Asked);
    }

    [Theory]
    [InlineData(UserConsentOutcome.Declined)]
    [InlineData(UserConsentOutcome.NotAvailable)]
    public async Task An_answer_other_than_verified_is_not_remembered(UserConsentOutcome answer)
    {
        var inner = new ScriptedConsent(answer);
        var consent = new RememberedUserConsent(inner, new FakeTimeProvider());

        var first = await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);
        inner.Next = UserConsentOutcome.Verified;
        var second = await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);

        Assert.Equal(answer, first);
        Assert.Equal(UserConsentOutcome.Verified, second);
        Assert.Equal(2, inner.Asked);
    }

    [Fact]
    public async Task Verify_every_time_asks_within_the_window()
    {
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);
        var consent = new RememberedUserConsent(inner, new FakeTimeProvider());

        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);
        inner.Next = UserConsentOutcome.Declined;

        var outcome = await consent.VerifyEveryTimeAsync(Prompt, TestContext.Current.CancellationToken);

        Assert.Equal(UserConsentOutcome.Declined, outcome);
        Assert.Equal(1, inner.Asked);
        Assert.Equal(1, inner.AskedEveryTime);
    }

    [Fact]
    public async Task Verify_every_time_does_not_open_the_window()
    {
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);
        var consent = new RememberedUserConsent(inner, new FakeTimeProvider());

        await consent.VerifyEveryTimeAsync(Prompt, TestContext.Current.CancellationToken);
        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);

        Assert.Equal(1, inner.AskedEveryTime);
        Assert.Equal(1, inner.Asked);
    }

    /// <summary>A check withdrawn while the window holds throws rather than answering Verified:
    /// the caller is going away, and nothing should run on its behalf.</summary>
    [Fact]
    public async Task A_cancelled_check_within_the_window_throws()
    {
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);
        var consent = new RememberedUserConsent(inner, new FakeTimeProvider());
        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consent.VerifyAsync(Prompt, cancelled.Token));
        Assert.Equal(1, inner.Asked);
    }

    /// <summary>A decorator wrapped around the remembering consent that forwards both methods
    /// keeps the every-time path every time: the window never answers it.</summary>
    [Fact]
    public async Task A_decorator_around_it_still_asks_every_time()
    {
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);
        IUserConsent consent = new PassThrough(new RememberedUserConsent(inner, new FakeTimeProvider()));

        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);
        await consent.VerifyEveryTimeAsync(Prompt, TestContext.Current.CancellationToken);
        await consent.VerifyEveryTimeAsync(Prompt, TestContext.Current.CancellationToken);

        Assert.Equal(1, inner.Asked);
        Assert.Equal(2, inner.AskedEveryTime);
    }

    [Fact]
    public async Task A_window_of_its_own_is_honoured()
    {
        var clock = new FakeTimeProvider();
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);
        var consent = new RememberedUserConsent(inner, clock, TimeSpan.FromSeconds(30));

        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(30));
        await consent.VerifyAsync(Prompt, TestContext.Current.CancellationToken);

        Assert.Equal(2, inner.Asked);
    }

    [Fact]
    public void Its_arguments_are_checked()
    {
        var inner = new ScriptedConsent(UserConsentOutcome.Verified);

        Assert.Throws<ArgumentNullException>(() => new RememberedUserConsent(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RememberedUserConsent(inner, window: TimeSpan.Zero));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_message_is_refused_before_any_task_starts(string message)
    {
        var consent = new RememberedUserConsent(new ScriptedConsent(UserConsentOutcome.Verified), new FakeTimeProvider());

        // Statement lambdas that drop the task: only a throw on the call itself passes.
        Assert.Throws<ArgumentException>(() => { _ = consent.VerifyAsync(message, TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentException>(() => { _ = consent.VerifyEveryTimeAsync(message, TestContext.Current.CancellationToken); });
    }

    /// <summary>A decorator that forwards each method to the same one of what it wraps.</summary>
    private sealed class PassThrough(IUserConsent inner) : IUserConsent
    {
        public Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default) =>
            inner.VerifyAsync(message, cancellationToken);

        public Task<UserConsentOutcome> VerifyEveryTimeAsync(string message, CancellationToken cancellationToken = default) =>
            inner.VerifyEveryTimeAsync(message, cancellationToken);
    }

    /// <summary>A consent that answers <see cref="Next"/> and counts each method's asks apart.</summary>
    private sealed class ScriptedConsent(UserConsentOutcome outcome) : IUserConsent
    {
        public UserConsentOutcome Next { get; set; } = outcome;

        public int Asked { get; private set; }

        public int AskedEveryTime { get; private set; }

        public Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(Next);
        }

        public Task<UserConsentOutcome> VerifyEveryTimeAsync(string message, CancellationToken cancellationToken = default)
        {
            AskedEveryTime++;
            return Task.FromResult(Next);
        }
    }
}
