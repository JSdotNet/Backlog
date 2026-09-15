using System.Text;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// What the model is told when it is asked for an import plan, and how the
/// item it is asked about is written out for it.
/// <para>
/// The system prompt restates the Backlog entry text grammar rather than
/// pointing at it: the model sees nothing but these two messages, so every
/// token the import parses has to be named here, and every token it does not
/// parse has to be kept out. Tasks' <c>EntryTextParser</c> is the reader this
/// text is written for — <c># Title</c>, one backtick-quoted metadata line,
/// <c>#tag</c> on every entry so the plan can be recognised on a re-import
/// (local ADR 0007), <c>id:</c>/<c>after:</c> for order, <c>!draft</c> on
/// every entry because a plan a model wrote about captured content gets no
/// Ready without a person's look (the import enforces that whatever the
/// model answers; asking for it keeps the two in step), and <c>repo:</c>
/// copied verbatim from the repositories the item names. The token list is
/// held to <c>plugins/backlog-tools/skills/backlog-import-plan/assets/backlog-import-grammar.md</c>
/// by <c>FoundryPlanPromptTests</c>, so a token added to the grammar is also
/// added, or deliberately refused, here.
/// </para>
/// <para>
/// <see cref="Marker"/> is the prompt's first line and nothing else's. The
/// local test service keys its canned plan on it — see
/// <c>LocalAzureFoundryCompletion</c> in <c>Backlog.AzureFoundry.TestService</c>,
/// which has no project references by design and therefore carries the same
/// literal — so changing the words here means changing them there too.
/// </para>
/// </summary>
public static class AzureFoundryPlanPrompt
{
    /// <summary>The prompt's opening line, the sentence a stand-in service
    /// recognises a plan request by. Duplicated, on purpose, in the harness.</summary>
    public const string Marker = "You write Backlog import plans.";

    /// <summary>The system message. Starts with <see cref="Marker"/>; the raw
    /// literal opens with two empty lines so the marker sits alone on the
    /// first line with a blank one under it.</summary>
    public const string Text = Marker + """


        Answer with Markdown in the Backlog entry text grammar and nothing else: no wrapper heading, no front matter, no code fence around the answer, and no prose outside the entries. A second top-level `# ` heading starts the next entry.

        Each entry, in this order:
        1. `# Title` — one line. Do not put a `#tag` or an `@name` in the title.
        2. One line of backtick-quoted metadata tokens.
        3. Body prose — the instruction to carry out, written for whoever picks the entry up.
        4. Optional `##` sub-item headings for setup steps and manual steps.

        The metadata line holds these tokens, in this order:
        - The type: `prompt` for an instruction an agent runs, `task` for a step a person does.
        - The status `!draft` on every entry. Never `!ready`, `!in-progress`, `!done` or `!archived`: a person promotes each entry after reading it, and order is carried by `after:`, not by status.
        - Optionally a priority: `*low`, `*medium`, `*high` or `*critical`.
        - The plan tag, `#<plan tag>` with the value given under "Plan tag:", on every entry. It is the plan's whole identity; an entry without it cannot be recognised on a re-import.
        - `id:<slug>` — a short lower-case slug naming this entry, unique within the plan, on every entry.
        - `after:<id>` — one token for each entry this one waits on, naming that entry's `id:`. Omit it on an entry that waits on nothing.
        - `repo:<owner/name>` — only a value copied verbatim from the list under "Repositories:". Never write a repository that is not on that list, and write no `repo:` token at all when the list is empty.
        - `effort:<points>` — the size in story points, one of 1, 2, 3, 5, 8, 13 or 21, on every entry.
        Do not write an `@area` token or a `due:` date; the person sets those in Backlog.

        When several repositories are given, cover every repository the work touches: one entry per repository per step, each with its own `id:` and its own `repo:`.

        The first line of every body is `Backlog plan item <id> of plan <plan tag>`, with the same id and the same tag as the metadata line, the tag written bare without its `#`. Then the instruction. When a source URL is given, end the body with a line `Source: <url>`.

        Write between two and eight entries. Never invent a token the grammar above does not list.
        """;

    /// <summary>
    /// The user message: the item as labelled sections, one fact per label, the
    /// content last because it is the one section free to hold blank lines.
    /// The labels are the ones the stand-in service reads back (<c>Item:</c>,
    /// <c>Repositories:</c>, <c>Plan tag:</c>), so they are part of the contract
    /// with the harness as much as with the model.
    /// </summary>
    public static string User(AzureFoundryPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = new StringBuilder();
        message.Append("Item: ").Append(request.ItemTitle.Trim()).Append("\n\n");
        message.Append("Kind: ").Append(request.KindSlug.Trim()).Append("\n\n");

        if (!string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            message.Append("Source: ").Append(request.SourceUrl.Trim()).Append("\n\n");
        }

        message.Append("Tags: ").Append(request.Tags.Count == 0 ? "(none)" : string.Join(", ", request.Tags)).Append("\n\n");

        message.Append("Repositories:");
        if (request.Repositories.Count == 0)
        {
            message.Append(" (none)");
        }
        else
        {
            foreach (var repository in request.Repositories)
            {
                message.Append('\n').Append(repository);
            }
        }

        message.Append("\n\n");
        message.Append("Plan tag: ").Append(request.PlanTag.Trim()).Append("\n\n");
        message.Append("Content:\n").Append(request.ItemContent.Trim());

        return message.ToString();
    }
}
