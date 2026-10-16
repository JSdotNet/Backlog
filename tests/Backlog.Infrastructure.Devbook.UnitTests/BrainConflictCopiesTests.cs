using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// Which notes in a Brain are a OneDrive conflict copy of another (local ADR 0026
/// §5): <c>&lt;name&gt;-&lt;MACHINE&gt;.md</c> beside <c>&lt;name&gt;.md</c> in one
/// folder, the machine name in capitals as OneDrive writes it.
/// </summary>
public class BrainConflictCopiesTests
{
    [Fact]
    public void A_machine_named_copy_beside_its_original_is_a_pair()
    {
        var pairs = BrainConflictCopies.Find(["plan.md", "plan-LAPTOP.md"]);

        Assert.Equal([new BrainConflictCopy("plan.md", "plan-LAPTOP.md")], pairs);
    }

    [Fact]
    public void A_copy_without_its_original_is_no_pair()
    {
        Assert.Empty(BrainConflictCopies.Find(["plan-LAPTOP.md"]));
    }

    [Fact]
    public void A_machine_name_with_a_hyphen_still_pairs_in_a_subfolder()
    {
        var pairs = BrainConflictCopies.Find(["notes/plan.md", "notes/plan-DESKTOP-AB12.md"]);

        Assert.Equal([new BrainConflictCopy("notes/plan.md", "notes/plan-DESKTOP-AB12.md")], pairs);
    }

    [Fact]
    public void A_name_that_starts_with_a_hyphen_still_pairs()
    {
        var pairs = BrainConflictCopies.Find(["-draft.md", "-draft-LAPTOP.md"]);

        Assert.Equal([new BrainConflictCopy("-draft.md", "-draft-LAPTOP.md")], pairs);
    }

    [Theory]
    [InlineData("plan-2.md")]
    [InlineData("my-plan.md")]
    [InlineData("plan-draft.md")]
    [InlineData("plan-Laptop.md")]
    [InlineData("plan-ABCDEFGHIJKLMNOP.md")]
    public void An_ordinary_hyphenated_name_is_not_a_copy(string other)
    {
        Assert.Empty(BrainConflictCopies.Find(["plan.md", other]));
    }

    [Fact]
    public void Pairs_are_matched_within_one_folder_only()
    {
        Assert.Empty(BrainConflictCopies.Find(["work/plan.md", "home/plan-LAPTOP.md", "plan-PC.md"]));
    }

    [Fact]
    public void The_extension_matches_whatever_its_case()
    {
        var pairs = BrainConflictCopies.Find([@"work\plan.md", @"work\plan-LAPTOP.MD"]);

        Assert.Equal([new BrainConflictCopy("work/plan.md", "work/plan-LAPTOP.MD")], pairs);
    }

    [Fact]
    public void Several_pairs_come_back_in_path_order()
    {
        var pairs = BrainConflictCopies.Find(["z.md", "a.md", "z-PC.md", "a-PC.md", "a-LAPTOP.md", "readme.txt"]);

        Assert.Equal(
            [new BrainConflictCopy("a.md", "a-LAPTOP.md"), new BrainConflictCopy("a.md", "a-PC.md"), new BrainConflictCopy("z.md", "z-PC.md")],
            pairs);
    }
}
