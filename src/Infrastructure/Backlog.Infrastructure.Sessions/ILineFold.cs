namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// A file's lines folded one at a time into state a later read can go on from: what
/// <see cref="ResumableFileMemory{TFold, TValue}"/> needs of a reader's parser.
/// <para>
/// <see cref="Clone"/> is what makes resuming safe. The memory keeps the state the last
/// read left untouched, folds the new lines into a copy, and keeps the copy only when the
/// read completes — so a read cut short spoils nothing, and two reads resuming the same
/// state at once never share one.
/// </para>
/// </summary>
/// <typeparam name="TSelf">The fold itself, so a copy comes back as what it was.</typeparam>
internal interface ILineFold<TSelf>
    where TSelf : class, ILineFold<TSelf>
{
    /// <summary>One line of the file, in the order the file holds it.</summary>
    void Observe(string line);

    /// <summary>A copy to fold further lines into, leaving this one as it is.</summary>
    TSelf Clone();
}
