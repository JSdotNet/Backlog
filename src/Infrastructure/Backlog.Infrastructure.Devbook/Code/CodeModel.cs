namespace Backlog.Infrastructure.Devbook.Code;

/// <summary>
/// What the C4 zoom tab's Code level shows for one component (local ADR 0042 §3):
/// the types declared under one repository folder, their members, and the uses
/// between them that can be seen by name. Read from syntax trees alone, so it is
/// a picture of the declarations as written — a partial type merged by name, a
/// member's type as spelled — and never a semantic model.
/// </summary>
/// <param name="Folder">The repository-relative folder it was read from,
/// <c>/</c>-separated.</param>
/// <param name="Types">Every type declared there, nested ones included, ordered
/// by <see cref="CodeType.Id"/>.</param>
/// <param name="Uses">The uses between those types, ordered by the using type,
/// inheritance first, then the used type.</param>
public sealed record CodeModel(string Folder, IReadOnlyList<CodeType> Types, IReadOnlyList<CodeTypeUse> Uses)
{
    /// <summary>The model of a folder with no C# in it, or none at all.</summary>
    public static CodeModel Empty(string folder) => new(folder, [], []);
}

/// <summary>One type, every partial part of it merged into one.</summary>
/// <param name="Id">The namespace, the containing types and the name, joined with
/// dots — <c>Shop.Orders.OrderService.Cache</c>, <c>Shop.Shared.Repository&lt;T&gt;</c>.
/// What a <see cref="CodeTypeUse"/> names.</param>
/// <param name="Name">The name as declared, type parameters included.</param>
/// <param name="Namespace">The namespace it is declared in; empty for the global
/// namespace.</param>
/// <param name="ContainingType">The <see cref="Id"/> of the type it is nested in,
/// or <see langword="null"/> for a type declared in a namespace.</param>
/// <param name="Kind">What kind of type it is.</param>
/// <param name="Accessibility">Its declared accessibility, or the default C#
/// gives it: internal in a namespace, private in a type.</param>
/// <param name="Modifiers">The modifiers of every part as written, each once, in
/// the order first seen.</param>
/// <param name="BaseList">The base types and interfaces of every part as
/// written, each once.</param>
/// <param name="FilePaths">The repository-relative files its parts are declared
/// in.</param>
/// <param name="Members">Its members of every accessibility, part by part in
/// file order: what an "All" view shows.</param>
public sealed record CodeType(
    string Id,
    string Name,
    string Namespace,
    string? ContainingType,
    CodeTypeKind Kind,
    CodeAccessibility Accessibility,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<string> BaseList,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<CodeMember> Members)
{
    /// <summary>The members declared public, or public by default as interface
    /// and enum members are: what the default view shows.</summary>
    public IReadOnlyList<CodeMember> PublicMembers { get; } =
        [.. Members.Where(member => member.Accessibility == CodeAccessibility.Public)];
}

/// <summary>One member of a type.</summary>
/// <param name="Name">Its name: a constructor's is the type's, an operator's
/// <c>operator +</c>, an indexer's <c>this[]</c>.</param>
/// <param name="Kind">What kind of member it is.</param>
/// <param name="Accessibility">Its declared accessibility, or the default: public
/// in an interface or an enum and for a record's positional property, private
/// elsewhere. An explicit interface implementation names none and reads as
/// private.</param>
/// <param name="Modifiers">Its modifiers as written.</param>
/// <param name="Signature">A one-line signature with every type as written:
/// <c>Add(OrderLine line) : void</c>, <c>Id : Guid</c>.</param>
/// <param name="FilePath">The repository-relative file it is declared in.</param>
public sealed record CodeMember(
    string Name,
    CodeMemberKind Kind,
    CodeAccessibility Accessibility,
    IReadOnlyList<string> Modifiers,
    string Signature,
    string FilePath);

/// <summary>One type naming another.</summary>
/// <param name="From">The <see cref="CodeType.Id"/> of the type whose declaration
/// names the other.</param>
/// <param name="To">The <see cref="CodeType.Id"/> it names.</param>
/// <param name="Kind">Whether it is named in the base list or anywhere else.</param>
public sealed record CodeTypeUse(string From, string To, CodeUseKind Kind);

/// <summary>How one type uses another.</summary>
public enum CodeUseKind
{
    /// <summary>Named in the base list: a base class or an interface.</summary>
    Inherits,

    /// <summary>Named anywhere else in the declaration: a member's type, a
    /// parameter, a constraint, an expression.</summary>
    References
}

/// <summary>What kind of type a declaration is.</summary>
public enum CodeTypeKind
{
    Class,
    Struct,
    Interface,
    Record,
    RecordStruct,
    Enum,
    Delegate
}

/// <summary>What kind of member a declaration is.</summary>
public enum CodeMemberKind
{
    Field,
    Property,
    Method,
    Constructor,
    Finalizer,
    Event,
    Indexer,
    Operator,
    Conversion,
    EnumMember
}

/// <summary>A declared accessibility, or the default C# gives one left out.</summary>
public enum CodeAccessibility
{
    Public,
    ProtectedInternal,
    Internal,
    Protected,
    PrivateProtected,
    Private
}
