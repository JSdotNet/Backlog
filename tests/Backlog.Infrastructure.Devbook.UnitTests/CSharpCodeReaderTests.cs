using Backlog.Infrastructure.Devbook.Code;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The C4 zoom tab's Code level read from syntax trees (local ADR 0039 §3),
/// against <c>Fixtures/CodeReader</c>: a small shop whose <c>Order</c> is split
/// over two partial files, with a positional record, a record struct, a nested
/// type, a generic type, an interface, an enum and a delegate. Each test copies
/// the fixture into a repository folder of its own, under <c>src/Shop</c>, so a
/// test that edits it never reaches another.
/// </summary>
public sealed class CSharpCodeReaderTests : IDisposable
{
    private const string Folder = "src/Shop";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-code-reader-tests", Guid.NewGuid().ToString("n"));

    public CSharpCodeReaderTests() =>
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CodeReader"), Path.Combine(_root, "src", "Shop"));

    [Fact]
    public void Every_type_in_the_folder_is_read_with_its_repository_relative_file()
    {
        var model = new CSharpCodeReader().Read(_root, Folder);

        Assert.Equal(Folder, model.Folder);
        Assert.Equal(
            [
                "Shop.Orders.Order",
                "Shop.Orders.OrderLine",
                "Shop.Orders.OrderService",
                "Shop.Orders.OrderService.Cache",
                "Shop.Shared.EntityBase",
                "Shop.Shared.IAuditable",
                "Shop.Shared.Money",
                "Shop.Shared.OrderHandler",
                "Shop.Shared.Repository<T>",
                "Shop.Shared.Status",
            ],
            model.Types.Select(type => type.Id));

        Assert.Equal(["src/Shop/Orders/OrderLine.cs"], Type(model, "Shop.Orders.OrderLine").FilePaths);
    }

    [Fact]
    public void Files_under_bin_and_obj_are_not_read()
    {
        Write("src/Shop/bin/Debug/Generated.cs", "namespace Shop; public class FromBin { }");
        Write("src/Shop/obj/Debug/Generated.cs", "namespace Shop; public class FromObj { }");
        Write("src/Shop/Orders/obj/Nested.cs", "namespace Shop; public class FromNestedObj { }");
        Write("src/Shop/Orders/Notes.txt", "public class FromText { }");

        var model = new CSharpCodeReader().Read(_root, Folder);

        Assert.DoesNotContain(model.Types, type => type.Name.StartsWith("From", StringComparison.Ordinal));
    }

    [Fact]
    public void A_partial_type_is_one_type_with_the_files_modifiers_bases_and_members_of_every_part()
    {
        var order = Type(new CSharpCodeReader().Read(_root, Folder), "Shop.Orders.Order");

        Assert.Equal(CodeTypeKind.Class, order.Kind);
        Assert.Equal("Order", order.Name);
        Assert.Equal("Shop.Orders", order.Namespace);
        Assert.Equal(CodeAccessibility.Public, order.Accessibility);
        Assert.Equal(["public", "partial", "sealed"], order.Modifiers);
        Assert.Equal(["EntityBase", "IAuditable"], order.BaseList);
        Assert.Equal(["src/Shop/Orders/Order.cs", "src/Shop/Orders/Order.Totals.cs"], order.FilePaths);
        Assert.Contains(order.Members, member => member.Name == "Total" && member.FilePath == "src/Shop/Orders/Order.Totals.cs");
        Assert.Contains(order.Members, member => member.Name == "Add" && member.FilePath == "src/Shop/Orders/Order.cs");
    }

    [Fact]
    public void The_public_view_keeps_the_public_members_and_the_all_view_keeps_every_member()
    {
        var order = Type(new CSharpCodeReader().Read(_root, Folder), "Shop.Orders.Order");

        Assert.Equal(["Id", "Modified", "Add", "Total"], order.PublicMembers.Select(member => member.Name));
        Assert.Equal(["_lines", "Id", "Modified", "Add", "Count", "OnChanged", "Total", "Sum"], order.Members.Select(member => member.Name));

        Assert.Equal(CodeAccessibility.Private, Member(order, "_lines").Accessibility);
        Assert.Equal(CodeAccessibility.Internal, Member(order, "Count").Accessibility);
        Assert.Equal(CodeAccessibility.Protected, Member(order, "OnChanged").Accessibility);
        Assert.Equal(["private", "static"], Member(order, "Sum").Modifiers);
    }

    [Fact]
    public void Members_carry_their_kind_and_a_signature_as_written()
    {
        var model = new CSharpCodeReader().Read(_root, Folder);
        var order = Type(model, "Shop.Orders.Order");
        var service = Type(model, "Shop.Orders.OrderService");
        var entity = Type(model, "Shop.Shared.EntityBase");

        Assert.Equal((CodeMemberKind.Method, "Add(OrderLine line) : void"), Shape(Member(order, "Add")));
        Assert.Equal((CodeMemberKind.Property, "Id : Guid"), Shape(Member(order, "Id")));
        Assert.Equal((CodeMemberKind.Field, "_lines : List<OrderLine>"), Shape(Member(order, "_lines")));
        Assert.Equal((CodeMemberKind.Constructor, "OrderService(Repository<Order> orders)"), Shape(Member(service, "OrderService")));
        Assert.Equal((CodeMemberKind.Event, "Changed : EventHandler?"), Shape(Member(entity, "Changed")));
        Assert.Equal(CodeAccessibility.Protected, Member(entity, "EntityBase").Accessibility);
    }

    [Fact]
    public void A_positional_record_lists_its_parameters_as_public_properties()
    {
        var model = new CSharpCodeReader().Read(_root, Folder);
        var line = Type(model, "Shop.Orders.OrderLine");
        var money = Type(model, "Shop.Shared.Money");

        Assert.Equal(CodeTypeKind.Record, line.Kind);
        Assert.Equal(["public", "sealed"], line.Modifiers);
        Assert.Equal(["Sku", "Quantity", "Price", "Subtotal"], line.PublicMembers.Select(member => member.Name));
        Assert.Equal((CodeMemberKind.Property, "Price : Money"), Shape(Member(line, "Price")));

        Assert.Equal(CodeTypeKind.RecordStruct, money.Kind);
        Assert.Equal(["public", "readonly"], money.Modifiers);
        Assert.Equal((CodeMemberKind.Operator, "operator +(Money left, Money right) : Money"), Shape(Member(money, "operator +")));
    }

    [Fact]
    public void Interface_and_enum_members_are_public_without_saying_so_and_types_default_to_internal()
    {
        var model = new CSharpCodeReader().Read(_root, Folder);
        var status = Type(model, "Shop.Shared.Status");

        Assert.Equal(["Modified"], Type(model, "Shop.Shared.IAuditable").PublicMembers.Select(member => member.Name));
        Assert.Equal(CodeTypeKind.Enum, status.Kind);
        Assert.Equal(CodeAccessibility.Internal, status.Accessibility);
        Assert.Equal(["Open", "Closed"], status.PublicMembers.Select(member => member.Name));
        Assert.All(status.Members, member => Assert.Equal(CodeMemberKind.EnumMember, member.Kind));
        Assert.Equal(CodeTypeKind.Delegate, Type(model, "Shop.Shared.OrderHandler").Kind);
    }

    [Fact]
    public void A_nested_type_names_its_container_and_a_generic_type_its_parameters()
    {
        var model = new CSharpCodeReader().Read(_root, Folder);
        var cache = Type(model, "Shop.Orders.OrderService.Cache");
        var repository = Type(model, "Shop.Shared.Repository<T>");

        Assert.Equal("Shop.Orders.OrderService", cache.ContainingType);
        Assert.Equal(CodeAccessibility.Private, cache.Accessibility);
        Assert.Equal(["Last"], cache.PublicMembers.Select(member => member.Name));
        Assert.DoesNotContain(Type(model, "Shop.Orders.OrderService").Members, member => member.Name == "Cache");

        Assert.Equal("Repository<T>", repository.Name);
        Assert.Null(repository.ContainingType);
    }

    [Fact]
    public void The_uses_between_types_are_the_ones_it_can_see_by_name()
    {
        var model = new CSharpCodeReader().Read(_root, Folder);

        Assert.Equal(
            [
                "Shop.Orders.Order -inherits-> Shop.Shared.EntityBase",
                "Shop.Orders.Order -inherits-> Shop.Shared.IAuditable",
                "Shop.Orders.Order -uses-> Shop.Orders.OrderLine",
                "Shop.Orders.Order -uses-> Shop.Shared.Money",
                "Shop.Orders.OrderLine -uses-> Shop.Shared.Money",
                "Shop.Orders.OrderService -uses-> Shop.Orders.Order",
                "Shop.Orders.OrderService -uses-> Shop.Shared.Repository<T>",
                "Shop.Orders.OrderService.Cache -uses-> Shop.Orders.Order",
                "Shop.Shared.OrderHandler -uses-> Shop.Orders.Order",
                "Shop.Shared.Repository<T> -uses-> Shop.Shared.EntityBase",
            ],
            model.Uses.Select(use => $"{use.From} -{(use.Kind == CodeUseKind.Inherits ? "inherits" : "uses")}-> {use.To}"));
    }

    [Fact]
    public void A_name_two_namespaces_declare_is_resolved_by_the_files_usings_or_left_out()
    {
        Write("src/Shop/Billing/Money.cs", "namespace Shop.Billing; public sealed class Money { }");
        Write("src/Shop/Billing/Invoice.cs", "namespace Shop.Billing; public sealed class Invoice { public Money Due { get; } = new(); }");
        Write("src/Shop/Reports/Report.cs", "using Shop.Shared; namespace Shop.Reports; public sealed class Report { public Money Sum; }");
        Write("src/Shop/Reports/Guess.cs", "namespace Shop.Reports; public sealed class Guess { public Money Sum; }");

        var uses = new CSharpCodeReader().Read(_root, Folder).Uses;

        Assert.Contains(uses, use => use is { From: "Shop.Billing.Invoice", To: "Shop.Billing.Money" });
        Assert.Contains(uses, use => use is { From: "Shop.Reports.Report", To: "Shop.Shared.Money" });
        Assert.DoesNotContain(uses, use => use.From == "Shop.Reports.Guess");
    }

    [Fact]
    public void A_qualified_name_or_a_relative_using_picks_the_namespace_it_spells()
    {
        Write("src/Shop/Billing/Money.cs", "namespace Shop.Billing; public sealed class Money { }");
        Write("src/Shop/Reports/Ledger.cs", "namespace Shop.Reports; public sealed class Ledger { public Billing.Money Due; }");
        Write("src/Shop/Reports/Book.cs", "namespace Shop { using Shared; namespace Reports { public sealed class Book { public Money Sum; } } }");

        var uses = new CSharpCodeReader().Read(_root, Folder).Uses;

        Assert.Contains(uses, use => use is { From: "Shop.Reports.Ledger", To: "Shop.Billing.Money" });
        Assert.Contains(uses, use => use is { From: "Shop.Reports.Book", To: "Shop.Shared.Money" });
    }

    [Fact]
    public void A_type_argument_in_the_base_list_is_a_use_not_a_base()
    {
        Write("src/Shop/Shared/Price.cs", "namespace Shop.Shared; public sealed class Price : IEquatable<Money>, IAuditable { }");

        var uses = new CSharpCodeReader().Read(_root, Folder).Uses.Where(use => use.From == "Shop.Shared.Price");

        Assert.Equal(
            [("Shop.Shared.IAuditable", CodeUseKind.Inherits), ("Shop.Shared.Money", CodeUseKind.References)],
            uses.Select(use => (use.To, use.Kind)));
    }

    [Fact]
    public void An_explicit_interface_implementation_is_named_with_its_interface()
    {
        Write("src/Shop/Shared/Stamp.cs", "namespace Shop.Shared; public sealed class Stamp : IAuditable { DateTimeOffset IAuditable.Modified => default; private int Modified; }");

        var stamp = Type(new CSharpCodeReader().Read(_root, Folder), "Shop.Shared.Stamp");

        Assert.Equal(["IAuditable.Modified", "Modified"], stamp.Members.Select(member => member.Name));
        Assert.Equal("IAuditable.Modified : DateTimeOffset", stamp.Members[0].Signature);
    }

    [Fact]
    public void A_type_nested_in_an_interface_is_public_and_an_extension_block_is_no_type()
    {
        Write(
            "src/Shop/Shared/Extensions.cs",
            "namespace Shop.Shared; public interface IHolder { class Slot { } } public static class MoneyExtensions { extension(Money money) { public bool IsZero => money.Amount == 0; } public static Money Twice(this Money money) => money + money; }");

        var model = new CSharpCodeReader().Read(_root, Folder);

        Assert.Equal(CodeAccessibility.Public, Type(model, "Shop.Shared.IHolder.Slot").Accessibility);
        Assert.Equal(["Twice"], Type(model, "Shop.Shared.MoneyExtensions").Members.Select(member => member.Name));
        Assert.DoesNotContain(model.Types, type => type.Id.StartsWith("Shop.Shared.MoneyExtensions.", StringComparison.Ordinal));
    }

    /// <summary>A file an editor holds mid-save is left out of that read, and is
    /// not left out of the next one just because its stamp has not moved.</summary>
    [Fact]
    public void A_file_that_cannot_be_read_is_not_remembered_as_missing()
    {
        if (!OperatingSystem.IsWindows()) return; // Only Windows enforces FileShare.None.

        Write("src/Shop/Orders/Refund.cs", "namespace Shop.Orders; public sealed class Refund { }");
        var reader = new CSharpCodeReader();

        using (new FileStream(Path.Combine(_root, "src", "Shop", "Orders", "Refund.cs"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.DoesNotContain(reader.Read(_root, Folder).Types, type => type.Id == "Shop.Orders.Refund");
        }

        Assert.Contains(reader.Read(_root, Folder).Types, type => type.Id == "Shop.Orders.Refund");
    }

    [Fact]
    public void A_folder_whose_files_have_not_changed_is_parsed_once()
    {
        var reader = new CSharpCodeReader();

        var first = reader.Read(_root, Folder);
        var second = reader.Read(_root, Folder);

        Assert.Same(first, second);
    }

    [Fact]
    public void A_file_added_changed_or_removed_is_read_again()
    {
        var reader = new CSharpCodeReader();
        var first = reader.Read(_root, Folder);

        Write("src/Shop/Orders/Refund.cs", "namespace Shop.Orders; public sealed class Refund { }");
        var added = reader.Read(_root, Folder);
        Assert.NotSame(first, added);
        Assert.Contains(added.Types, type => type.Id == "Shop.Orders.Refund");

        // A different length is a different stamp whatever the clock did.
        Write("src/Shop/Orders/Refund.cs", "namespace Shop.Orders; public sealed class Refund { public int Amount; }");
        var changed = reader.Read(_root, Folder);
        Assert.Equal(["Amount"], Type(changed, "Shop.Orders.Refund").Members.Select(member => member.Name));

        File.Delete(Path.Combine(_root, "src", "Shop", "Orders", "Refund.cs"));
        Assert.DoesNotContain(reader.Read(_root, Folder).Types, type => type.Id == "Shop.Orders.Refund");
    }

    /// <summary>The requirement's own scenario: the Devbook component's code is
    /// this project, read from this repository as it is now.</summary>
    [Fact]
    public void This_repository_s_devbook_adapter_reads_with_the_reader_in_it()
    {
        var model = new CSharpCodeReader().Read(RepositoryRoot.Root.FullName, "src/Infrastructure/Backlog.Infrastructure.Devbook");

        var reader = Type(model, "Backlog.Infrastructure.Devbook.Code.CSharpCodeReader");
        Assert.Contains(reader.PublicMembers, member => member.Signature == "Read(string repositoryRoot, string folder) : CodeModel");
        Assert.Contains(model.Uses, use => use is { From: "Backlog.Infrastructure.Devbook.Code.CSharpCodeReader", To: "Backlog.Infrastructure.Devbook.Code.CodeModel" });
        Assert.DoesNotContain(model.Types, type => type.FilePaths.Any(path => path.Contains("/obj/", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_folder_that_is_not_there_reads_as_no_types()
    {
        var model = new CSharpCodeReader().Read(_root, "src/Missing");

        Assert.Empty(model.Types);
        Assert.Empty(model.Uses);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("src/../../outside")]
    public void A_folder_outside_the_repository_is_refused(string folder) =>
        Assert.Throws<ArgumentException>(() => new CSharpCodeReader().Read(_root, folder));

    [Fact]
    public void Source_that_does_not_parse_cleanly_still_yields_what_the_tree_holds()
    {
        Write("src/Shop/Broken.cs", "namespace Shop; public class Half { public int Value { get; ");

        var half = Type(new CSharpCodeReader().Read(_root, Folder), "Shop.Half");

        Assert.Equal(["Value"], half.Members.Select(member => member.Name));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless.
        }
    }

    private static CodeType Type(CodeModel model, string id) =>
        Assert.Single(model.Types, type => type.Id == id);

    private static CodeMember Member(CodeType type, string name) =>
        Assert.Single(type.Members, member => member.Name == name);

    private static (CodeMemberKind Kind, string Signature) Shape(CodeMember member) => (member.Kind, member.Signature);

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
