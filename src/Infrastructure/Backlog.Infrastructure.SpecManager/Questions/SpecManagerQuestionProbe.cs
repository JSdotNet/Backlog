using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// The startup probe for the <c>spec-manager</c> subjects (<see cref="INotificationSubjectProbe"/>):
/// a question that is no linked task's lives while spec-manager still lists it open in one of
/// the connected products. One answered or deleted while the app was closed is caught this way.
/// <para>
/// Nobody signed in, no product connected, or a product that cannot be read is no answer: the
/// probe throws, and the owner leaves the notification open for the pulls to settle. Each
/// product's open questions are read once per pass — the startup asks about every subject in
/// turn.
/// </para>
/// </summary>
internal sealed class SpecManagerQuestionProbe(SpecManagerClient client, SpecManagerSignIn signIn, IConnectedTargets? targets, TimeProvider time)
    : INotificationSubjectProbe
{
    /// <summary>How long one read of a product's open questions answers for.</summary>
    internal static readonly TimeSpan PassLength = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, HashSet<string> Open)? _pass;

    public string Context => SpecManagerQuestionMapper.SourceContext;

    public async Task<bool> IsLiveAsync(NotificationSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // A subject this probe does not name is nothing it can say ended.
        if (!string.Equals(subject.Type, SpecManagerQuestionMapper.QuestionSubjectType, StringComparison.Ordinal)) return true;

        var open = await OpenQuestionsAsync(cancellationToken).ConfigureAwait(false);
        return open.Contains(subject.Id);
    }

    private async Task<HashSet<string>> OpenQuestionsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pass is { } pass && time.GetUtcNow() - pass.At < PassLength) return pass.Open;

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

            _pass = (time.GetUtcNow(), open);
            return open;
        }
        finally
        {
            _gate.Release();
        }
    }
}
