using System.Globalization;
using System.Text;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// One spec-manager question as the notification that asks it in Backlog
/// (<c>.devbook/domain/notifications/domain.md#notification</c>): pure, so every rule of
/// the mapping is asserted without a server.
/// </summary>
internal static class SpecManagerQuestionMapper
{
    /// <summary>The notification source every spec-manager question and alert is raised
    /// under, and the context its subject probe answers for.</summary>
    public const string SourceContext = "spec-manager";

    /// <summary>The subject type of a question that is not about a linked task.</summary>
    public const string QuestionSubjectType = "question";

    /// <summary>The "Via" of a question a person typed in spec-manager.</summary>
    public const string TypedInSpecManager = "typed in spec-manager";

    /// <summary>How long the "Other" answer and the note may run: spec-manager's own
    /// bound on both.</summary>
    public const int AnswerTextLimit = 4_000;

    /// <summary>The reason a question no longer asked of the person closes with: deleted,
    /// or reassigned away from them.</summary>
    public const string WithdrawnReason = "Withdrawn in spec-manager";

    /// <summary>The longest close reason written; an answer quoted in it is cut.</summary>
    internal const int MaxReasonLength = 300;

    /// <summary>The source key of a question: <c>{product}/{questionId}</c>.</summary>
    public static string SourceKey(string product, string questionId) => $"{product}/{questionId}";

    /// <summary>The product and question a source key names, or null when it names
    /// neither.</summary>
    public static (string Product, string QuestionId)? ParseSourceKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var slash = key.LastIndexOf('/');
        return slash <= 0 || slash == key.Length - 1 ? null : (key[..slash], key[(slash + 1)..]);
    }

    /// <summary>Whether a question reaches the person: assigned to them, or — unless
    /// <paramref name="onlyAssignedToMe"/> — assigned to nobody and theirs to answer.
    /// <paramref name="me"/> is the person's member id in the product, or null when the
    /// product would not say, and then nothing assigned counts as theirs.</summary>
    public static bool Concerns(BacklogQuestionDto question, string? me, bool onlyAssignedToMe) =>
        IsAssignedTo(question, me)
        || (!onlyAssignedToMe && question.Assignee is null && question.CanAnswer);

    /// <summary>Whether the question is assigned to <paramref name="me"/>.</summary>
    public static bool IsAssignedTo(BacklogQuestionDto question, string? me) =>
        me is not null && question.Assignee is { } assignee && string.Equals(assignee.Id, me, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The notification <paramref name="question"/> becomes. <paramref name="root"/> is the
    /// installation's, and <paramref name="defaultInstallation"/> whether it is the one
    /// that ships — the origin names the host of any other. <paramref name="linked"/> is the
    /// linked task the question's item became, when it became one.
    /// </summary>
    public static NotificationDraft ToDraft(
        BacklogQuestionDto question,
        string product,
        string root,
        bool defaultInstallation,
        string? me,
        SpecManagerLinkedWork? linked)
    {
        ArgumentNullException.ThrowIfNull(question);

        var assignedToMe = IsAssignedTo(question, me);
        var link = LinkOf(question, product, root);

        return new NotificationDraft(
            new NotificationSource(SourceContext, SourceKey(product, question.Id)),
            linked is { } task
                ? new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(task.TaskId))
                : new NotificationSubject(SourceContext, QuestionSubjectType, question.Id),
            NotificationKind.Question,
            assignedToMe ? NotificationSeverity.Warning : NotificationSeverity.Info,
            Cut(OneLine(question.Text), 80),
            Cut(OneLine(WhatItIsOn(question)), 180),
            Body(question),
            AnswerSpecOf(question),
            DeepLink: link,
            Origin: OriginOf(question, product, root, defaultInstallation, linked, link),
            Id: SpecManagerQuestionIds.For(root, question.Id));
    }

    /// <summary>The answers spec-manager takes: each option a choice, by its position;
    /// "Other" the open answer; and the note.</summary>
    public static AnswerSpec AnswerSpecOf(BacklogQuestionDto question) => new(
        [.. (question.Options ?? [])
            .OrderBy(option => option.Position)
            .Select(option => new AnswerChoice(
                option.Position.ToString(CultureInfo.InvariantCulture),
                Cut(OneLine(option.Label), 120)))],
        Open: new OpenAnswer("Other: answer in your own words", AnswerTextLimit),
        Note: new OpenAnswer("Add a note (optional)", AnswerTextLimit));

    /// <summary>What the question is on: <c>#7 Rounding in invoices</c>, else the OpenSpec
    /// change, else nothing.</summary>
    public static string WhatItIsOn(BacklogQuestionDto question) =>
        question.ItemNumber is { } number
            ? $"#{number.ToString(CultureInfo.InvariantCulture)} {question.ItemTitle}".Trim()
            : question.OpenSpecChange?.Trim() ?? string.Empty;

    /// <summary>The reason a question answered in spec-manager closes with here.</summary>
    public static string AnsweredReason(BacklogAnswerDto answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var by = string.IsNullOrWhiteSpace(answer.AnsweredBy?.Name) ? "someone" : answer.AnsweredBy.Name.Trim();
        return Cut(OneLine($"Answered in spec-manager by {by}: {answer.Said}"), MaxReasonLength);
    }

    /// <summary>
    /// Where the question opens in spec-manager: the question itself on the questions
    /// overview when it hangs on an item, and the comparison of its change when it hangs
    /// on a change alone — the overview may be off then, and the comparison shows it.
    /// </summary>
    public static string? LinkOf(BacklogQuestionDto question, string product, string root)
    {
        var slug = Uri.EscapeDataString(product);

        if (question.ItemId is not null)
            return $"{root}/producten/{slug}/questions?question={Uri.EscapeDataString(question.Id)}";

        return string.IsNullOrWhiteSpace(question.OpenSpecChange)
            ? $"{root}/producten/{slug}/questions?question={Uri.EscapeDataString(question.Id)}"
            : $"{root}/producten/{slug}?openspec={Uri.EscapeDataString(question.OpenSpecChange.Trim())}";
    }

    /// <summary>Where the product's settings are switched, its features section: the link
    /// an alert about "Answer questions from a connected app" opens.</summary>
    public static string ProductSettingsLink(string product, string root) =>
        $"{root}/producten/{Uri.EscapeDataString(product)}/instellingen/features";

    /// <summary>The origin's system: spec-manager, and the host when it is not the
    /// installation that ships.</summary>
    public static string SystemOf(string root, bool defaultInstallation) =>
        defaultInstallation || !Uri.TryCreate(root, UriKind.Absolute, out var uri)
            ? NotificationOrigins.SpecManager
            : $"{NotificationOrigins.SpecManager} ({uri.Authority})";

    private static NotificationOrigin? OriginOf(
        BacklogQuestionDto question,
        string product,
        string root,
        bool defaultInstallation,
        SpecManagerLinkedWork? linked,
        string? link)
    {
        var on = WhatItIsOn(question);
        var via = !question.FromAgentSession
            ? TypedInSpecManager
            : linked?.SessionTitle is { Length: > 0 } session
                ? NotificationOrigins.AgentSession + NotificationOrigin.Separator + session
                : NotificationOrigins.AgentSession;

        return NotificationOrigin.Fit(
            SystemOf(root, defaultInstallation),
            on.Length == 0 ? product : product + NotificationOrigin.Separator + on,
            question.AskedBy?.Name,
            via,
            link);
    }

    /// <summary>The question in full, its context, and what each option means, as
    /// Markdown within the body's bound.</summary>
    internal static string Body(BacklogQuestionDto question)
    {
        var body = new StringBuilder();

        body.Append("**").Append(question.Text.Trim()).AppendLine("**");

        if (!string.IsNullOrWhiteSpace(question.Context))
        {
            body.AppendLine().AppendLine(question.Context.Trim());
        }

        var options = (question.Options ?? []).OrderBy(option => option.Position).ToList();
        if (options.Count > 0)
        {
            body.AppendLine().AppendLine("**Options**").AppendLine();
            foreach (var option in options)
            {
                body.Append(option.Position.ToString(CultureInfo.InvariantCulture)).Append(". **").Append(OneLine(option.Label)).Append("**");
                if (!string.IsNullOrWhiteSpace(option.Description)) body.Append(" — ").Append(OneLine(option.Description));
                body.AppendLine();
            }

            body.AppendLine().AppendLine("Or answer in your own words, and add a note if it helps.");
        }

        return CutBytes(body.ToString(), 32 * 1024);
    }

    internal static string OneLine(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static string Cut(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";

    /// <summary>Text cut to at most <paramref name="bytes"/> bytes of UTF-8, on a character
    /// boundary, an ellipsis marking the cut.</summary>
    internal static string CutBytes(string text, int bytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= bytes) return text;

        var length = text.Length;
        while (length > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > bytes - 8) length--;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;

        return text[..length] + "…";
    }
}
