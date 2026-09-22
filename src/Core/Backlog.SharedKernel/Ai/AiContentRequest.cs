namespace Backlog.SharedKernel.Ai;

/// <summary>
/// The question, and the room its answer's content may take.
/// </summary>
/// <remarks>
/// <para>
/// The budget travels with the question rather than being a constant a source
/// reads, because it is the shell's to set: the shell knows what the assistant
/// behind the panel can take in one turn, and a source knows only what its own
/// records look like. Characters rather than tokens, because no source has a
/// tokenizer and every one of them has a string length.
/// </para>
/// </remarks>
/// <param name="Question">What the reader typed. A source may use it to choose
/// which of its records matter most; it never answers it.</param>
/// <param name="BudgetCharacters">How long the body may be. See
/// <see cref="AiContentBudget.DefaultCharacters"/> for the figure the shell
/// sends and why.</param>
public sealed record AiContentRequest(string Question, int BudgetCharacters);
