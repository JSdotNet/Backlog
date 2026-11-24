namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Writes what an alias move did to a store kept as one file per repository (local
/// ADR 0060 §3), in the order that never loses data to a failed write: every target
/// first, then each source — rewritten while it still holds something, deleted once
/// it holds nothing — and a source only once every target its data went to was
/// written. A source whose target could not be written is left on disk as it was, so
/// the next start reads the data under its old alias and the backstop moves it again.
/// <para>
/// A file that is both a source and a target — each side of a swap — is written as a
/// source: its own data and what moved into it are one file, and one write holds or
/// fails for both.
/// </para>
/// </summary>
internal static class RepositoryFileMove
{
    /// <param name="moves">Each source with each file its data went to; a source may
    /// appear once per target.</param>
    /// <param name="save">Writes a file, answering whether the write held.</param>
    /// <param name="holdsAnything">Whether a file still keeps data.</param>
    /// <param name="delete">Removes a file that keeps nothing.</param>
    public static void Commit<TFile>(
        IReadOnlyCollection<(TFile Source, TFile Target)> moves,
        Func<TFile, bool> save,
        Func<TFile, bool> holdsAnything,
        Action<TFile> delete)
        where TFile : class
    {
        var sources = moves.Select(move => move.Source).ToHashSet();
        var written = new Dictionary<TFile, bool>();

        foreach (var target in moves.Select(move => move.Target).Distinct())
        {
            if (!sources.Contains(target)) written[target] = save(target);
        }

        foreach (var source in sources)
        {
            var targetsHeld = moves
                .Where(move => ReferenceEquals(move.Source, source))
                .All(move => !written.TryGetValue(move.Target, out var held) || held);

            if (!targetsHeld) continue;

            if (holdsAnything(source))
            {
                save(source);
            }
            else
            {
                delete(source);
            }
        }
    }
}
