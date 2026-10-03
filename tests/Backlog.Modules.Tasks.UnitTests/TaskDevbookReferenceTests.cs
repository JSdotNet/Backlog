using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// A task's pointer at a Devbook page or chapter, and the list of them the task
/// holds.
/// <para>
/// Written by hand, pasted out of a browser, emitted by a plan generator — so the
/// reference is normalised to one spelling before anything compares it, and a
/// value that names no page at all is refused rather than stored for the panel to
/// show as a link to nowhere. Whether the page exists is deliberately not asked:
/// a chapter renamed after the link was made is a broken link to show, not a
/// write to refuse.
/// </para>
/// </summary>
public sealed class TaskDevbookReferenceTests
{
    [Theory]
    [InlineData(".devbook/domain/tasks/domain.md", ".devbook/domain/tasks/domain.md", null)]
    [InlineData(".devbook/domain/tasks/domain.md#task", ".devbook/domain/tasks/domain.md", "task")]
    [InlineData("[.devbook/domain/tasks/domain.md#task]", ".devbook/domain/tasks/domain.md", "task")]
    [InlineData(@".devbook\domain\tasks\domain.md", ".devbook/domain/tasks/domain.md", null)]
    [InlineData("./.devbook/arc42/adr/0007.md", ".devbook/arc42/adr/0007.md", null)]
    [InlineData("/.devbook/arc42/adr/0007.md", ".devbook/arc42/adr/0007.md", null)]
    [InlineData("  .devbook/design/README.md#tokens  ", ".devbook/design/README.md", "tokens")]
    [InlineData("README.md", "README.md", null)]
    [InlineData(".devbook/domain/tasks", ".devbook/domain/tasks", null)]
    [InlineData(".devbook/domain/tasks/domain.md#", ".devbook/domain/tasks/domain.md", null)]
    [InlineData("domain/tasks/domain.md#task", ".devbook/domain/tasks/domain.md", "task")]
    [InlineData("arc42/09-decisions.md", ".devbook/arc42/09-decisions.md", null)]
    [InlineData("./tech/shared.md", ".devbook/tech/shared.md", null)]
    [InlineData("Design/tokens.md", ".devbook/Design/tokens.md", null)]
    [InlineData("ai/stages.md", ".devbook/ai/stages.md", null)]
    [InlineData(".domain/tasks/domain.md", ".domain/tasks/domain.md", null)]
    [InlineData("docs/handbook.md", "docs/handbook.md", null)]
    [InlineData("backlog/plan.md", "backlog/plan.md", null)]
    public void A_reference_is_normalised_to_one_spelling(string written, string path, string? anchor)
    {
        var reference = TaskDevbookReference.Parse(written);

        Assert.Equal(path, reference.Path);
        Assert.Equal(anchor, reference.Anchor);
        Assert.Equal(anchor is null ? path : $"{path}#{anchor}", reference.Value);
        Assert.Equal(reference.Value, reference.ToString());
    }

    /// <summary>A path with no folder and no Markdown extension names no page —
    /// <c>task</c> on its own is a word, not a chapter.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("task")]
    [InlineData("#task")]
    [InlineData("domain#task")]
    [InlineData("./")]
    public void A_value_that_names_no_page_is_refused(string written)
    {
        Assert.Throws<ArgumentException>(() => TaskDevbookReference.Parse(written));
        Assert.False(TaskDevbookReference.TryParse(written, out var reference));
        Assert.Null(reference);
    }

    [Fact]
    public void Null_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TaskDevbookReference.Parse(null));
        Assert.False(TaskDevbookReference.TryParse(null, out _));
    }

    [Fact]
    public void Two_spellings_of_one_reference_are_equal()
    {
        Assert.Equal(
            TaskDevbookReference.Parse(@"[./.devbook\domain\tasks\domain.md#task]"),
            TaskDevbookReference.Parse(".devbook/domain/tasks/domain.md#task"));
    }

    // --- On the aggregate ---------------------------------------------------

    [Fact]
    public void A_new_task_has_no_references()
    {
        var task = new TaskItem("Link the chapter", string.Empty, EntryType.Task);

        Assert.Empty(task.DevbookReferences);
    }

    [Fact]
    public void The_list_is_set_whole_normalised_and_in_order()
    {
        var task = new TaskItem("Link the chapter", string.Empty, EntryType.Task);

        task.SetDevbookReferences([
            "[.devbook/domain/tasks/domain.md#task]",
            @".devbook\arc42\adr\0007.md",
        ]);

        Assert.Equal([".devbook/domain/tasks/domain.md#task", ".devbook/arc42/adr/0007.md"], task.DevbookReferences);
    }

    /// <summary>Duplicates are compared after normalisation, so two spellings of
    /// one chapter are one reference — and ordinally, because a different anchor
    /// casing is a different heading slug.</summary>
    [Fact]
    public void A_repeated_reference_is_kept_once()
    {
        var task = new TaskItem("Link the chapter", string.Empty, EntryType.Task);

        task.SetDevbookReferences([
            ".devbook/domain/tasks/domain.md#task",
            "./.devbook/domain/tasks/domain.md#task",
            ".devbook/domain/tasks/domain.md#Task",
        ]);

        Assert.Equal([".devbook/domain/tasks/domain.md#task", ".devbook/domain/tasks/domain.md#Task"], task.DevbookReferences);
    }

    [Fact]
    public void Setting_a_new_list_replaces_the_old_one()
    {
        var task = new TaskItem("Link the chapter", string.Empty, EntryType.Task);
        task.SetDevbookReferences([".devbook/domain/tasks/domain.md#task"]);

        task.SetDevbookReferences([".devbook/arc42/adr/0007.md"]);

        Assert.Equal([".devbook/arc42/adr/0007.md"], task.DevbookReferences);
    }

    [Fact]
    public void Null_and_empty_both_clear_the_list()
    {
        var task = new TaskItem("Link the chapter", string.Empty, EntryType.Task);
        task.SetDevbookReferences([".devbook/domain/tasks/domain.md#task"]);

        task.SetDevbookReferences(null);
        Assert.Empty(task.DevbookReferences);

        task.SetDevbookReferences([".devbook/domain/tasks/domain.md#task"]);
        task.SetDevbookReferences([]);
        Assert.Empty(task.DevbookReferences);
    }

    /// <summary>One refused value refuses the whole list, and leaves the list the
    /// task already had: half a replacement is a list nobody asked for.</summary>
    [Fact]
    public void A_list_holding_a_refused_value_is_refused_whole()
    {
        var task = new TaskItem("Link the chapter", string.Empty, EntryType.Task);
        task.SetDevbookReferences([".devbook/domain/tasks/domain.md#task"]);

        Assert.Throws<ArgumentException>(() => task.SetDevbookReferences([".devbook/arc42/adr/0007.md", "task"]));

        Assert.Equal([".devbook/domain/tasks/domain.md#task"], task.DevbookReferences);
    }

    /// <summary>The references are the task's own field, never the text: neither
    /// the body nor the title gains a token when they are set.</summary>
    [Fact]
    public void Setting_references_leaves_the_text_alone()
    {
        var task = new TaskItem("Link the chapter", "The body.", EntryType.Task);

        task.SetDevbookReferences([".devbook/domain/tasks/domain.md#task"]);

        Assert.Equal("Link the chapter", task.Title);
        Assert.Equal("The body.", task.ContentMd);
    }
}
