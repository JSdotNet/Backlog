namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// What the Code level of the C4 zoom tab draws for one component: the types its code
/// folders declare, the uses between them, and the decision records linked to the
/// component.
/// <para>
/// The component library knows neither the C# reader nor the devbook, so this is the
/// picture already put into words — a stereotype, a name, member lines — and the host
/// fills it from both (local ADR 0038 §3).
/// </para>
/// </summary>
/// <param name="Component">The component's name, as the model spells it.</param>
/// <param name="Folders">The repository-relative code folders the types were read
/// from.</param>
/// <param name="Types">The types, in the order the reader listed them.</param>
/// <param name="Uses">One type naming another, both of <paramref name="Types"/>.</param>
/// <param name="Decisions">The decision records linked to the component.</param>
public sealed record C4CodeLevel(
    string Component,
    IReadOnlyList<string> Folders,
    IReadOnlyList<C4CodeType> Types,
    IReadOnlyList<C4CodeUse> Uses,
    IReadOnlyList<C4CodeRecord> Decisions)
{
    /// <summary>Why the level could not be read — a folder outside the repository, a
    /// source that would not open — or null when it was.</summary>
    public string? Problem { get; init; }
}

/// <summary>One class box.</summary>
/// <param name="Id">The type's full name; what a <see cref="C4CodeUse"/> names.</param>
/// <param name="Name">The name as declared, type parameters included.</param>
/// <param name="Kind">The kind as a reader says it: "static class", "record".</param>
/// <param name="Stereotype">The line over the name: «sealed class» : IDisposable.</param>
/// <param name="FilePaths">The repository-relative files it is declared in, a partial
/// type's main part first.</param>
/// <param name="PublicMembers">The member lines the Public view shows.</param>
/// <param name="AllMembers">The member lines the All view shows.</param>
public sealed record C4CodeType(
    string Id,
    string Name,
    string Kind,
    string Stereotype,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<string> PublicMembers,
    IReadOnlyList<string> AllMembers);

/// <summary>One type naming another: drawn as an arrow from <paramref name="From"/> to
/// <paramref name="To"/>.</summary>
/// <param name="Inherits">Whether it is named in the base list — drawn dashed and
/// labelled "inherits" — rather than anywhere else.</param>
public sealed record C4CodeUse(string From, string To, bool Inherits);

/// <summary>A record linked to the component, as the type panel lists it.</summary>
/// <param name="Number">Its number, "0004", or empty when the file names none.</param>
/// <param name="Title">Its title with the number and prefix taken off.</param>
/// <param name="Path">The chapter file, spelled as the chapter catalogs spell it; what
/// opening it asks the host for.</param>
public sealed record C4CodeRecord(string Number, string Title, string Path);

/// <summary>
/// What the C4 zoom tab is handed to offer the Code level: which components have code
/// to read, and how to read it. A host with none passes nothing and step 4 stays grey
/// everywhere.
/// </summary>
/// <param name="Has">Whether a component, by its model identifier, has code folders.</param>
/// <param name="Load">Reads one component's Code level. Called each time the level is
/// opened, so a source changed since the last opening is shown as it is now.</param>
public sealed record C4ZoomCode(
    Func<string, bool> Has,
    Func<string, CancellationToken, Task<C4CodeLevel>> Load)
{
    /// <summary>Opens a repository-relative file in the reader's editor and answers
    /// with why it could not, or null. Null where there is no repository to open a file
    /// from, and then the type panel offers no "Open in editor". A host that cannot
    /// start an editor — the web harness — still offers it and answers why, as its
    /// open-folder button does.</summary>
    public Func<string, Task<string?>>? OpenInEditor { get; init; }

    /// <summary>Opens a linked record's chapter. Null leaves the records as text.</summary>
    public Func<C4CodeRecord, Task>? OpenRecord { get; init; }
}
