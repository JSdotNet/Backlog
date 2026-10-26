using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The <see cref="INotificationAnswerHandler"/> for the <c>mcp</c> source: it takes every answer
/// and does nothing with it, because the session that asked collects its answer by polling
/// <c>get_answer</c>, which reads the stored answer whether or not a handler took it.
/// <para>
/// Registered all the same, so an MCP answer is marked delivered once accepted rather than left
/// for <c>NotificationsStartup</c> to replay on every start, forever, with nobody to take it.
/// </para>
/// </summary>
public sealed class McpNotificationAnswers : INotificationAnswerHandler
{
    public string Source => NotificationTools.SourceContext;

    public Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
