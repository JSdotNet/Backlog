using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// The startup probe for the <c>spec-manager</c> subjects (<see cref="INotificationSubjectProbe"/>):
/// a question that is no linked task's lives while spec-manager still lists it open in one of
/// the connected products. One answered or deleted while the app was closed is caught this way;
/// one answered ends with who answered and what, the reason the pulls close it with
/// (<see cref="SpecManagerQuestionMapper.AnsweredReason"/>), and one deleted names no reason.
/// <para>
/// Nobody signed in, no product connected, or a product that cannot be read is no answer: the
/// probe throws, and the owner leaves the notification open for the pulls to settle. Each
/// product's open questions are read once per pass — the startup asks about every subject in
/// turn — and its answered questions once more in that pass, only when a subject has ended.
/// </para>
/// </summary>
internal sealed class SpecManagerQuestionProbe(SpecManagerClient client, SpecManagerSignIn signIn, IConnectedTargets? targets, TimeProvider time)
    : INotificationSubjectProbe
{
    /// <summary>How long one read of a product's open questions answers for.</summary>
    internal static readonly TimeSpan PassLength = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Pass? _pass;

    public string Context => SpecManagerQuestionMapper.SourceContext;

    public async Task<bool> IsLiveAsync(NotificationSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // A subject this probe does not name is nothing it can say ended.
        if (!IsQuestion(subject)) return true;

        var pass = await PassAsync(withAnswers: false, cancellationToken).ConfigureAwait(false);
        return pass.Open.Contains(subject.Id);
    }

    public async Task<string?> EndReasonAsync(NotificationSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (!IsQuestion(subject)) return null;

        var pass = await PassAsync(withAnswers: true, cancellationToken).ConfigureAwait(false);
        return pass.Answered!.GetValueOrDefault(subject.Id);
    }

    private static bool IsQuestion(NotificationSubject subject) =>
        string.Equals(subject.Type, SpecManagerQuestionMapper.QuestionSubjectType, StringComparison.Ordinal);

    /// <summary>The current pass, read afresh when it is older than <see cref="PassLength"/>;
    /// with its answered questions read too when <paramref name="withAnswers"/>.</summary>
    private async Task<Pass> PassAsync(bool withAnswers, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pass is not { } pass || time.GetUtcNow() - pass.At >= PassLength)
            {
                pass = await ReadOpenAsync(cancellationToken).ConfigureAwait(false);
                _pass = pass;
            }

            if (withAnswers && pass.Answered is null)
            {
                pass.Answered = await ReadAnsweredAsync(pass.Products, cancellationToken).ConfigureAwait(false);
            }

            return pass;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Pass> ReadOpenAsync(CancellationToken cancellationToken)
    {
        if (signIn.Account is null) throw new SpecManagerSignInRequiredException();

        var products = (targets?.List() ?? [])
            .Where(target => target.Enabled && string.Equals(target.ConnectorId, SpecManagerConnector.ConnectorId, StringComparison.Ordinal))
            .Select(target => target.Target.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (products.Count == 0) throw new InvalidOperationException("No spec-manager product is connected to ask about its questions.");

        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in products)
        {
            foreach (var question in await client.GetQuestionsAsync(product, BacklogQuestionStates.Open, changedSince: null, cancellationToken).ConfigureAwait(false))
            {
                open.Add(question.Id);
            }
        }

        return new Pass(time.GetUtcNow(), products, open);
    }

    private async Task<Dictionary<string, string>> ReadAnsweredAsync(IReadOnlyList<string> products, CancellationToken cancellationToken)
    {
        var answered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in products)
        {
            foreach (var question in await client.GetQuestionsAsync(product, BacklogQuestionStates.Answered, changedSince: null, cancellationToken).ConfigureAwait(false))
            {
                if (question.Answer is { } answer) answered[question.Id] = SpecManagerQuestionMapper.AnsweredReason(answer);
            }
        }

        return answered;
    }

    /// <summary>One read of the connected products: their open questions, and — once a subject
    /// has ended — the reason each answered one closes with.</summary>
    private sealed class Pass(DateTimeOffset at, IReadOnlyList<string> products, HashSet<string> open)
    {
        public DateTimeOffset At { get; } = at;

        public IReadOnlyList<string> Products { get; } = products;

        public HashSet<string> Open { get; } = open;

        public Dictionary<string, string>? Answered { get; set; }
    }
}
