using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Backlog.Infrastructure.Devbook.Code;

/// <summary>
/// Turns a folder's C# files into a <see cref="CodeModel"/> from their syntax
/// trees alone.
/// <para>
/// <b>Types.</b> Every class, struct, interface, record, record struct, enum and
/// delegate declaration, nested ones as types of their own. Parts of a partial
/// type are merged by their namespace, containing types and name — the identity
/// the compiler would give them, read without asking it.
/// </para>
/// <para>
/// <b>Uses.</b> A type uses another when a name in its declaration — outside the
/// types nested in it, which use for themselves — is the name of a type in the
/// folder, with the same number of type arguments. A name declared in more than
/// one namespace is resolved the way C# looks it up as far as syntax can follow:
/// a type nested in the user or its containers first, then the user's namespace
/// and the ones enclosing it, then the namespaces the file imports with
/// <c>using</c>, global usings included. A name still ambiguous after that is
/// left out rather than guessed. A member that shares a type's name is counted
/// as a use: telling them apart needs the semantic model this reader never builds.
/// </para>
/// </summary>
internal static partial class CSharpSyntaxModelBuilder
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview, DocumentationMode.None);

    /// <summary>The model of <paramref name="files"/>. <paramref name="complete"/>
    /// is false when a file could not be read — locked mid-save, say — so the
    /// caller can decline to remember a model missing it.</summary>
    public static CodeModel Build(string folder, IEnumerable<(string RelativePath, string FullPath)> files, out bool complete)
    {
        complete = true;
        var parts = new List<TypePart>();
        var globalUsings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (relativePath, fullPath) in files)
        {
            string text;
            try
            {
                text = File.ReadAllText(fullPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                complete = false;
                continue;
            }

            var root = CSharpSyntaxTree.ParseText(text, ParseOptions, relativePath).GetCompilationUnitRoot();
            foreach (var directive in root.Usings.Where(directive => directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)))
            {
                if (ImportedNamespace(directive) is { } imported) globalUsings.Add(imported);
            }

            CollectParts(root.Members, relativePath, @namespace: string.Empty, containing: null, Usings(root.Usings), parts);
        }

        var types = Merge(parts);
        var uses = Uses(parts, globalUsings);
        return new CodeModel(folder, types, uses);
    }

    private static void CollectParts(
        SyntaxList<MemberDeclarationSyntax> members,
        string filePath,
        string @namespace,
        TypePart? containing,
        IReadOnlyList<string> usings,
        List<TypePart> parts)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax declaration:
                    var name = Clean(declaration.Name.ToString());
                    var inner = @namespace.Length == 0 ? name : $"{@namespace}.{name}";
                    CollectParts(declaration.Members, filePath, inner, containing, [.. usings, .. Usings(declaration.Usings, inner)], parts);
                    break;

                case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
                    // An extension block (C# 14) is a type declaration in the tree
                    // but declares no type: its members extend another one.
                    if (Part(member, filePath, @namespace, containing, usings) is not { } part) break;
                    parts.Add(part);
                    if (member is TypeDeclarationSyntax type)
                    {
                        CollectParts(type.Members, filePath, @namespace, part, usings, parts);
                    }

                    break;
            }
        }
    }

    private static TypePart? Part(
        MemberDeclarationSyntax declaration,
        string filePath,
        string @namespace,
        TypePart? containing,
        IReadOnlyList<string> usings)
    {
        (string Identifier, TypeParameterListSyntax? TypeParameters, CodeTypeKind Kind)? shape = declaration switch
        {
            RecordDeclarationSyntax record => (record.Identifier.Text, record.TypeParameterList,
                record.IsKind(SyntaxKind.RecordStructDeclaration) ? CodeTypeKind.RecordStruct : CodeTypeKind.Record),
            ClassDeclarationSyntax @class => (@class.Identifier.Text, @class.TypeParameterList, CodeTypeKind.Class),
            StructDeclarationSyntax @struct => (@struct.Identifier.Text, @struct.TypeParameterList, CodeTypeKind.Struct),
            InterfaceDeclarationSyntax @interface => (@interface.Identifier.Text, @interface.TypeParameterList, CodeTypeKind.Interface),
            EnumDeclarationSyntax @enum => (@enum.Identifier.Text, null, CodeTypeKind.Enum),
            DelegateDeclarationSyntax @delegate => (@delegate.Identifier.Text, @delegate.TypeParameterList, CodeTypeKind.Delegate),
            _ => null,
        };
        if (shape is not var (identifier, typeParameters, kind)) return null;

        var name = typeParameters is { Parameters.Count: > 0 }
            ? $"{identifier}<{string.Join(", ", typeParameters.Parameters.Select(parameter => parameter.Identifier.Text))}>"
            : identifier;
        var id = containing is not null
            ? $"{containing.Id}.{name}"
            : @namespace.Length == 0 ? name : $"{@namespace}.{name}";

        var modifiers = declaration.Modifiers.Select(modifier => modifier.Text).ToList();
        var accessibility = AccessibilityOf(declaration.Modifiers) ?? containing?.Kind switch
        {
            null => CodeAccessibility.Internal,
            CodeTypeKind.Interface => CodeAccessibility.Public,
            _ => CodeAccessibility.Private,
        };
        var baseList = (declaration as BaseTypeDeclarationSyntax)?.BaseList?.Types.Select(type => Clean(type.Type.ToString())).ToList() ?? [];

        return new TypePart(
            id,
            name,
            identifier,
            typeParameters?.Parameters.Count ?? 0,
            @namespace,
            containing,
            kind,
            accessibility,
            modifiers,
            baseList,
            filePath,
            Members(declaration, kind, identifier, filePath),
            declaration,
            usings);
    }

    private static List<CodeMember> Members(MemberDeclarationSyntax declaration, CodeTypeKind kind, string typeName, string filePath)
    {
        var members = new List<CodeMember>();

        if (declaration is EnumDeclarationSyntax @enum)
        {
            foreach (var value in @enum.Members)
            {
                var signature = value.EqualsValue is { } equals ? $"{value.Identifier.Text} = {Clean(equals.Value.ToString())}" : value.Identifier.Text;
                members.Add(new CodeMember(value.Identifier.Text, CodeMemberKind.EnumMember, CodeAccessibility.Public, [], signature, filePath));
            }

            return members;
        }

        if (declaration is not TypeDeclarationSyntax type) return members;

        // A record's positional parameters are public properties; a class's or a
        // struct's primary constructor parameters are not members at all.
        if (type is RecordDeclarationSyntax { ParameterList: { } positional })
        {
            foreach (var parameter in positional.Parameters)
            {
                members.Add(new CodeMember(
                    parameter.Identifier.Text,
                    CodeMemberKind.Property,
                    CodeAccessibility.Public,
                    [],
                    $"{parameter.Identifier.Text} : {TypeText(parameter.Type)}",
                    filePath));
            }
        }

        var defaultAccessibility = kind == CodeTypeKind.Interface ? CodeAccessibility.Public : CodeAccessibility.Private;

        foreach (var member in type.Members)
        {
            var accessibility = AccessibilityOf(member.Modifiers) ?? defaultAccessibility;
            var modifiers = member.Modifiers.Select(modifier => modifier.Text).ToList();

            void Add(string name, CodeMemberKind memberKind, string signature) =>
                members.Add(new CodeMember(name, memberKind, accessibility, modifiers, signature, filePath));

            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables)
                    {
                        Add(variable.Identifier.Text, CodeMemberKind.Field, $"{variable.Identifier.Text} : {TypeText(field.Declaration.Type)}");
                    }

                    break;

                case EventFieldDeclarationSyntax eventField:
                    foreach (var variable in eventField.Declaration.Variables)
                    {
                        Add(variable.Identifier.Text, CodeMemberKind.Event, $"{variable.Identifier.Text} : {TypeText(eventField.Declaration.Type)}");
                    }

                    break;

                case EventDeclarationSyntax @event:
                    var eventName = Explicit(@event.ExplicitInterfaceSpecifier) + @event.Identifier.Text;
                    Add(eventName, CodeMemberKind.Event, $"{eventName} : {TypeText(@event.Type)}");
                    break;

                case PropertyDeclarationSyntax property:
                    var propertyName = Explicit(property.ExplicitInterfaceSpecifier) + property.Identifier.Text;
                    Add(propertyName, CodeMemberKind.Property, $"{propertyName} : {TypeText(property.Type)}");
                    break;

                case IndexerDeclarationSyntax indexer:
                    var indexerName = Explicit(indexer.ExplicitInterfaceSpecifier) + "this";
                    Add($"{indexerName}[]", CodeMemberKind.Indexer, $"{indexerName}[{Parameters(indexer.ParameterList.Parameters)}] : {TypeText(indexer.Type)}");
                    break;

                case MethodDeclarationSyntax method:
                    var methodName = Explicit(method.ExplicitInterfaceSpecifier) + method.Identifier.Text;
                    var typeParameterList = method.TypeParameterList is { Parameters.Count: > 0 } generic
                        ? $"<{string.Join(", ", generic.Parameters.Select(parameter => parameter.Identifier.Text))}>"
                        : string.Empty;
                    Add(methodName, CodeMemberKind.Method, $"{methodName}{typeParameterList}({Parameters(method.ParameterList.Parameters)}) : {TypeText(method.ReturnType)}");
                    break;

                case ConstructorDeclarationSyntax constructor:
                    Add(typeName, CodeMemberKind.Constructor, $"{typeName}({Parameters(constructor.ParameterList.Parameters)})");
                    break;

                case DestructorDeclarationSyntax:
                    Add($"~{typeName}", CodeMemberKind.Finalizer, $"~{typeName}()");
                    break;

                case OperatorDeclarationSyntax @operator:
                    var operatorName = $"operator {@operator.OperatorToken.Text}";
                    Add(operatorName, CodeMemberKind.Operator, $"{operatorName}({Parameters(@operator.ParameterList.Parameters)}) : {TypeText(@operator.ReturnType)}");
                    break;

                case ConversionOperatorDeclarationSyntax conversion:
                    var conversionName = $"{conversion.ImplicitOrExplicitKeyword.Text} operator {TypeText(conversion.Type)}";
                    Add(conversionName, CodeMemberKind.Conversion, $"{conversionName}({Parameters(conversion.ParameterList.Parameters)})");
                    break;
            }
        }

        return members;
    }

    /// <summary>The <c>IFoo.</c> an explicit interface implementation is named
    /// with, so it never reads as a private member of the same name.</summary>
    private static string Explicit(ExplicitInterfaceSpecifierSyntax? specifier) =>
        specifier is null ? string.Empty : $"{TypeText(specifier.Name)}.";

    private static List<CodeType> Merge(List<TypePart> parts) =>
        [.. parts
            .GroupBy(part => part.Id, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return new CodeType(
                    first.Id,
                    first.Name,
                    first.Namespace,
                    first.Containing?.Id,
                    first.Kind,
                    group.Select(part => AccessibilityOf(part.Declaration.Modifiers)).FirstOrDefault(explicitly => explicitly is not null) ?? first.Accessibility,
                    [.. group.SelectMany(part => part.Modifiers).Distinct(StringComparer.Ordinal)],
                    [.. group.SelectMany(part => part.BaseList).Distinct(StringComparer.Ordinal)],
                    [.. group.Select(part => part.FilePath).Distinct(StringComparer.Ordinal)],
                    [.. group.SelectMany(part => part.Members)]);
            })
            .OrderBy(type => type.Id, StringComparer.Ordinal)];

    private static List<CodeTypeUse> Uses(List<TypePart> parts, IReadOnlySet<string> globalUsings)
    {
        var byName = parts
            .GroupBy(part => (part.Identifier, part.Arity))
            .ToDictionary(
                group => group.Key,
                group => group.DistinctBy(part => part.Id, StringComparer.Ordinal).ToList());

        var uses = new Dictionary<(string From, string To), CodeUseKind>();

        foreach (var part in parts)
        {
            var baseTypes = (part.Declaration as BaseTypeDeclarationSyntax)?.BaseList?.Types.Select(type => type.Type).ToList() ?? [];
            var names = part.Declaration
                .DescendantNodes(node => node == part.Declaration || node is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
                .OfType<SimpleNameSyntax>();

            foreach (var name in names)
            {
                var arity = name is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments.Count : 0;
                if (!byName.TryGetValue((name.Identifier.Text, arity), out var candidates)) continue;
                var qualifier = name.Parent is QualifiedNameSyntax qualified && qualified.Right == name ? Clean(qualified.Left.ToString()) : null;
                if (Resolve(candidates, part, qualifier, globalUsings) is not { } target || target.Id == part.Id) continue;

                var kind = baseTypes.Any(type => NamesType(type, name)) ? CodeUseKind.Inherits : CodeUseKind.References;
                var key = (part.Id, target.Id);
                if (kind == CodeUseKind.Inherits || !uses.ContainsKey(key)) uses[key] = kind;
            }
        }

        return [.. uses
            .Select(use => new CodeTypeUse(use.Key.From, use.Key.To, use.Value))
            .OrderBy(use => use.From, StringComparer.Ordinal)
            .ThenBy(use => use.Kind)
            .ThenBy(use => use.To, StringComparer.Ordinal)];
    }

    /// <summary>Whether <paramref name="name"/> is the type <paramref name="type"/>
    /// names — <c>Money</c> in <c>Money</c> or <c>Shop.Money</c> — rather than one
    /// of its type arguments, as in <c>IEquatable&lt;Money&gt;</c>.</summary>
    private static bool NamesType(TypeSyntax type, SimpleNameSyntax name) => type switch
    {
        SimpleNameSyntax simple => simple == name,
        QualifiedNameSyntax qualified => qualified.Right == name,
        AliasQualifiedNameSyntax aliased => aliased.Name == name,
        _ => false,
    };

    /// <summary>The one type a name in <paramref name="user"/> can mean, or
    /// <see langword="null"/> when syntax cannot tell.</summary>
    private static TypePart? Resolve(List<TypePart> candidates, TypePart user, string? qualifier, IReadOnlySet<string> globalUsings)
    {
        if (candidates.Count == 1) return candidates[0];

        // Written qualified — Billing.Money — the qualifier picks the namespace or
        // the containing type, as far as it is spelled.
        if (qualifier is not null)
        {
            var scope = qualifier.Replace("global::", string.Empty, StringComparison.Ordinal);
            return Single(candidates, candidate =>
            {
                var container = candidate.Containing?.Id ?? candidate.Namespace;
                return container == scope || container.EndsWith("." + scope, StringComparison.Ordinal);
            });
        }

        // A type nested in the user or in one of its containers.
        for (var container = user; container is not null; container = container.Containing)
        {
            if (Single(candidates, candidate => candidate.Containing?.Id == container.Id) is { } nested) return nested;
        }

        // The user's own namespace, then each one enclosing it.
        var @namespace = user.Namespace;
        while (true)
        {
            var current = @namespace;
            if (Single(candidates, candidate => candidate.Containing is null && candidate.Namespace == current) is { } local) return local;
            if (current.Length == 0) break;

            var dot = current.LastIndexOf('.');
            @namespace = dot < 0 ? string.Empty : current[..dot];
        }

        // The namespaces the file imports.
        return Single(candidates, candidate =>
            candidate.Containing is null
            && (user.Usings.Contains(candidate.Namespace, StringComparer.Ordinal) || globalUsings.Contains(candidate.Namespace)));
    }

    private static TypePart? Single(List<TypePart> candidates, Func<TypePart, bool> predicate)
    {
        TypePart? found = null;
        foreach (var candidate in candidates)
        {
            if (!predicate(candidate)) continue;
            if (found is not null) return null;
            found = candidate;
        }

        return found;
    }

    /// <summary>The namespaces <paramref name="directives"/> import. Inside
    /// <c>namespace Shop</c>, <c>using Shared;</c> may mean <c>Shop.Shared</c>,
    /// so a using there is also read relative to each enclosing namespace.</summary>
    private static List<string> Usings(SyntaxList<UsingDirectiveSyntax> directives, string enclosing = "")
    {
        var imported = new List<string>();
        foreach (var name in directives.Select(ImportedNamespace).OfType<string>())
        {
            imported.Add(name);
            for (var scope = enclosing; scope.Length > 0; scope = scope.LastIndexOf('.') is var dot and >= 0 ? scope[..dot] : string.Empty)
            {
                imported.Add($"{scope}.{name}");
            }
        }

        return imported;
    }

    /// <summary>The namespace a plain <c>using</c> imports; an alias or a
    /// <c>using static</c> imports none.</summary>
    private static string? ImportedNamespace(UsingDirectiveSyntax directive) =>
        directive.Alias is null && !directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && directive.Name is { } name
            ? Clean(name.ToString()).Replace("global::", string.Empty, StringComparison.Ordinal)
            : null;

    private static CodeAccessibility? AccessibilityOf(SyntaxTokenList modifiers)
    {
        var @public = modifiers.Any(SyntaxKind.PublicKeyword);
        var @protected = modifiers.Any(SyntaxKind.ProtectedKeyword);
        var @internal = modifiers.Any(SyntaxKind.InternalKeyword);
        var @private = modifiers.Any(SyntaxKind.PrivateKeyword);

        return (@public, @protected, @internal, @private) switch
        {
            (true, _, _, _) => CodeAccessibility.Public,
            (_, true, true, _) => CodeAccessibility.ProtectedInternal,
            (_, true, _, true) => CodeAccessibility.PrivateProtected,
            (_, true, _, _) => CodeAccessibility.Protected,
            (_, _, true, _) => CodeAccessibility.Internal,
            (_, _, _, true) => CodeAccessibility.Private,
            _ => null,
        };
    }

    private static string Parameters(SeparatedSyntaxList<ParameterSyntax> parameters) =>
        string.Join(", ", parameters.Select(parameter =>
        {
            var prefix = string.Concat(parameter.Modifiers.Select(modifier => modifier.Text + " "));
            return parameter.Type is null
                ? prefix + parameter.Identifier.Text
                : $"{prefix}{TypeText(parameter.Type)} {parameter.Identifier.Text}";
        }));

    private static string TypeText(TypeSyntax? type) => type is null ? string.Empty : Clean(type.ToString());

    /// <summary>Source text on one line: a line break or a run of spaces inside
    /// a type name becomes one space.</summary>
    private static string Clean(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed record TypePart(
        string Id,
        string Name,
        string Identifier,
        int Arity,
        string Namespace,
        TypePart? Containing,
        CodeTypeKind Kind,
        CodeAccessibility Accessibility,
        List<string> Modifiers,
        List<string> BaseList,
        string FilePath,
        List<CodeMember> Members,
        MemberDeclarationSyntax Declaration,
        IReadOnlyList<string> Usings);
}
