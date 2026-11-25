namespace Backlog.SharedKernel;

/// <summary>
/// <see cref="IUserConsent"/> that lets one confirmation hold for a while (local ADR 0058 §6):
/// after a <see cref="UserConsentOutcome.Verified"/> answer, a <see cref="VerifyAsync"/> within
/// <see cref="DefaultWindow"/> answers Verified without asking again, so the person checking
/// several scopes, a grant and a revocation in a row confirms once.
/// <para>
/// <b>The person's own acts only.</b> <see cref="VerifyEveryTimeAsync"/> always asks the inner
/// consent and never opens the window: an agent's <c>tasks:admin</c> call and a Refuse answered on
/// the phone are not started by the person at the desk, so a confirmation from a moment ago does
/// not stand in for theirs. A Declined or unavailable answer is never remembered.
/// </para>
/// </summary>
/// <param name="inner">What asks: Windows Hello on the desktop head.</param>
/// <param name="time">The clock the window runs on; the system clock when none is given.</param>
/// <param name="window">How long a confirmation holds; <see cref="DefaultWindow"/> when none is given.</param>
public sealed class RememberedUserConsent(IUserConsent inner, TimeProvider? time = null, TimeSpan? window = null) : IUserConsent
{
    /// <summary>How long a confirmation holds when no window is given: five minutes.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);

    private readonly IUserConsent _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TimeSpan _window = window is { } given
        ? given > TimeSpan.Zero ? given : throw new ArgumentOutOfRangeException(nameof(window), given, "The window must be longer than zero.")
        : DefaultWindow;

    private readonly Lock _gate = new();
    private DateTimeOffset? _verifiedAt;

    /// <inheritdoc />
    /// <remarks>Answered Verified without asking within the window of the last confirmation; asked
    /// of the inner consent otherwise, and a Verified answer opens a new window from when it
    /// landed.</remarks>
    public Task<UserConsentOutcome> VerifyAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return VerifyCoreAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public Task<UserConsentOutcome> VerifyEveryTimeAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return _inner.VerifyEveryTimeAsync(message, cancellationToken);
    }

    private async Task<UserConsentOutcome> VerifyCoreAsync(string message, CancellationToken cancellationToken)
    {
        if (IsRemembered())
        {
            // A withdrawn check is not confirmed by the window either: the caller is going away.
            cancellationToken.ThrowIfCancellationRequested();
            return UserConsentOutcome.Verified;
        }

        var outcome = await _inner.VerifyAsync(message, cancellationToken).ConfigureAwait(false);

        if (outcome == UserConsentOutcome.Verified)
        {
            // From when the answer landed, not when the prompt opened: the person may take a
            // while at it.
            var now = _time.GetUtcNow();
            lock (_gate) _verifiedAt = now;
        }

        return outcome;
    }

    private bool IsRemembered()
    {
        DateTimeOffset? verifiedAt;
        lock (_gate) verifiedAt = _verifiedAt;

        return verifiedAt is { } at && _time.GetUtcNow() - at < _window;
    }
}
