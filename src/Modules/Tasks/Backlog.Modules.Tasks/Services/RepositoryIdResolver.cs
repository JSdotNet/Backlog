using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Services;

/// <summary>
/// Turns the repository names an entry's text wrote into the <c>owner/name</c>
/// ids its <c>repo_ids</c> holds.
/// <para>
/// One rule in one place, because two use cases need it and they must not
/// disagree: the ordinary text save, and Import. The only difference between
/// them is what happens to a name nothing recognises, which is exactly the
/// leniency ADR 0007 grants Import and nothing else — so it is the difference
/// between the two methods here rather than two implementations of the whole
/// rule.
/// </para>
/// <para>
/// This is where the alias stops being the stored identity.
/// <c>.devbook/design/content-editing.md</c> describes the token as a label somebody
/// types; the registry is the authority on what that label means, and resolution
/// is the only way a value reaches <c>repo_ids</c>. <c>EntryTextParser</c> is
/// deliberately not involved: per ADR 0002 it lives in <c>.Abstractions</c>, may
/// not see a registry, and must keep having no opinion about a <c>repo:</c>
/// value.
/// </para>
/// <para>
/// Construct one per use-case run. The memo it holds is a within-run
/// answer — a plan naming the same repository in ten entries is one question
/// about one repository, and asking ten times is how an unrecognized name gets
/// offered for registration ten times. Held across runs it would be a cache of a
/// registry somebody may have edited in between.
/// </para>
/// </summary>
internal sealed class RepositoryIdResolver(IRepositoryDirectory repositories)
{
    /// <summary>Keyed on the name exactly as the text wrote it, because that is
    /// the question being asked. Ordinal: two spellings are two questions, and
    /// they may legitimately have the same answer.</summary>
    private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);

    /// <summary>
    /// The ids a set of names refers to. A name the registry does not know is
    /// kept verbatim.
    /// <para>
    /// Kept rather than dropped, deliberately: dropping it would delete a token
    /// somebody typed with no error to notice it by. <c>RepositoryFor(row)</c>
    /// answers null and the row reads "No repo", which is the token's general
    /// rule for a name nothing recognises.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Resolve(IEnumerable<string>? names) => Canonicalise(names, matches: null, register: false).Value;

    /// <summary>
    /// The same rule, plus the one thing Import may do that nothing else may: a
    /// name nothing recognises is registered on the spot, per ADR 0007, so a plan
    /// can introduce a repository to the workspace just by mentioning it.
    /// <para>
    /// A failure when a registration failed, carrying the registry's error. The
    /// only way this can fail, and the reason it can: an id handed back for a
    /// repository the registry could not keep would be filed on an entry and
    /// point at nothing after a restart.
    /// </para>
    /// </summary>
    /// <param name="matches">What the reader said in the Import dialog: the name
    /// as the plan wrote it, mapped to the repository they meant. A person having
    /// looked at a name is the strongest signal there is about what it means,
    /// which is why it is consulted before the registry rather than after
    /// it.</param>
    public Result<IReadOnlyList<string>> ResolveOrRegister(IEnumerable<string>? names, IReadOnlyDictionary<string, string>? matches) =>
        Canonicalise(names, matches, register: true);

    /// <summary>
    /// Canonicalises, then de-duplicates — in that order.
    /// <para>
    /// The order is the point. Two casings of one repository are genuinely two
    /// strings before resolution and one target after it, which is why
    /// <c>EntryTextParser</c> can keep its <c>StringComparer.Ordinal</c> and stay
    /// ignorant of the registry. Ordinal here too: after canonicalisation every
    /// resolved value is spelled the way the registry spells it, so two equal
    /// targets are byte-equal, and two unresolved values in different casings stay
    /// two targets because there is no authority to collapse them against.
    /// </para>
    /// </summary>
    private Result<IReadOnlyList<string>> Canonicalise(
        IEnumerable<string>? names,
        IReadOnlyDictionary<string, string>? matches,
        bool register)
    {
        if (names is null) return Result.Success<IReadOnlyList<string>>([]);

        var ids = new List<string>();
        foreach (var name in names)
        {
            var id = One(name, matches, register);
            if (id.IsFailure) return id.Error;

            ids.Add(id.Value);
        }

        return Result.Success<IReadOnlyList<string>>([.. ids.Distinct(StringComparer.Ordinal)]);
    }

    private Result<string> One(string name, IReadOnlyDictionary<string, string>? matches, bool register)
    {
        if (_resolved.TryGetValue(name, out var already)) return already;

        // A failure is not remembered: it is not an answer about the name, and
        // the run it belongs to stops here anyway.
        var id = Answer(name, matches, register);
        if (id.IsSuccess) _resolved[name] = id.Value;
        return id;
    }

    private Result<string> Answer(string name, IReadOnlyDictionary<string, string>? matches, bool register)
    {
        // The reader's own answer, resolved through the directory like any other
        // name so that both branches end at an id rather than one ending at
        // whatever the dialog happened to put in the map. A matched name is never
        // registered: the person said which repository they meant, and if that one
        // has since gone the honest outcome is the name they gave, not a new
        // repository nobody asked for.
        if (matches is not null && matches.TryGetValue(name, out var matched) && !string.IsNullOrWhiteSpace(matched))
        {
            return repositories.Resolve(matched)?.Id ?? matched;
        }

        if (repositories.Resolve(name) is { } known) return known.Id;

        if (!register) return name;

        var registered = repositories.Register(name);
        return registered.IsSuccess ? registered.Value.Id : registered.Error;
    }
}
