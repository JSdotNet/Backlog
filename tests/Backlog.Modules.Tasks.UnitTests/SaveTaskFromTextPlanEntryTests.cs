using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// What happens when a <c>plan</c> entry reaches the path that saves a task.
/// <para>
/// ADR 0013 ruling 2 gives Import the only door a roadmap item may come through,
/// and this handler is the one every other way in goes past — the entry editor, a
/// paste, quick-add, a sub-item. Refusing here is what makes "a <c>plan</c> entry
/// never becomes a Task" true of all of them at once, which is why these tests
/// assert on the handler's outcome rather than on any one screen.
/// </para>
/// <para>
/// Both dependencies are passed as null on purpose. The refusal has to come
/// before the entry store and the repository registry are asked anything, and a
/// null that never gets dereferenced proves it in a way an assertion on a fake
/// could not: a guard that moved below either call would fail these tests with a
/// <see cref="NullReferenceException"/> instead of passing quietly.
/// </para>
/// <para>
/// That the guard does not over-refuse — a sigilled <c>`*plan`</c>, a misspelt
/// <c>`plann`</c> — is settled in <c>EntryTextParserTests</c> where those inputs
/// are read, because all this guard does is ask the parser which kind it found.
/// Re-asserting it here would need a full store fake to get past the refusal, and
/// would still be testing the parser.
/// </para>
/// </summary>
public class SaveTaskFromTextPlanEntryTests
{
    private const string PlanEntry = "# Imported plans on the roadmap\n`plan` `+roadmap-imported-plans`\n";

    [Fact]
    public async Task A_plan_entry_is_refused_rather_than_saved_as_a_task()
    {
        var result = await Save(null, PlanEntry);

        Assert.True(result.IsFailure);
        Assert.Equal(SaveTaskFromTextCommandHandler.PlanBelongsToImport, result.Error);
    }

    /// <summary>The message has one job: say where the entry does belong, so the
    /// person is not left guessing why a word the grammar accepts was turned
    /// down.</summary>
    [Fact]
    public async Task The_refusal_names_import_as_the_way_in()
    {
        var result = await Save(null, PlanEntry);

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Contains("Import", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("roadmap", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An update is refused on the same terms as a create: retyping an
    /// entry somebody already has into a plan entry would otherwise walk a stored
    /// task through the same gap.</summary>
    [Fact]
    public async Task An_update_is_refused_on_the_same_terms()
    {
        var result = await Save(Guid.NewGuid(), PlanEntry);

        Assert.True(result.IsFailure);
        Assert.Equal(SaveTaskFromTextCommandHandler.PlanBelongsToImport, result.Error);
    }

    /// <summary>The whole line is read before the kind is judged, so a plan entry
    /// carrying every token ruling 2 gives it is refused exactly like the bare
    /// one — a roadmap item does not become saveable by being fully written
    /// out.</summary>
    [Fact]
    public async Task A_fully_written_plan_entry_is_refused_too()
    {
        var result = await Save(
            null,
            "# Imported plans on the roadmap\n"
            + "`plan` `*high` `+roadmap-imported-plans` `id:roadmap-imported-plans` "
            + "`after:capture-adapters` `repo:backlog` `due:2026-10-31`\n\n"
            + "What the plan is about.\n");

        Assert.True(result.IsFailure);
        Assert.Equal(SaveTaskFromTextCommandHandler.PlanBelongsToImport, result.Error);
    }

    private static Task<Result<SavedTaskDto>> Save(Guid? id, string rawText) =>
        new SaveTaskFromTextCommandHandler(null!, null!)
            .Handle(new SaveTaskFromTextCommand(id, rawText, 0));
}
