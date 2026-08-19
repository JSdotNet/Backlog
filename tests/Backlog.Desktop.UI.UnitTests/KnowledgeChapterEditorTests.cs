using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The shared knowledge editing surface: when it offers a way in, and every
/// trigger that gets a pending save onto disk.
/// <para>
/// The triggers are the point. A debounce that only ever fires on its own timer
/// loses whatever was typed in the last 750 ms before a blur, a Done, or a jump
/// to another chapter — and it loses it silently, which is why the flush paths
/// are tested one by one rather than trusted to the timer.
/// </para>
/// </summary>
public sealed class KnowledgeChapterEditorTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public void A_chapter_that_could_not_be_resolved_offers_no_way_in()
    {
        using var context = NewContext();

        var component = context.Render<KnowledgeChapterEditor>(parameters => parameters
            .Add(editor => editor.Chapter, null)
            .Add(editor => editor.InitialText, "# Notes\n\nProse.\n")
            .Add(editor => editor.Title, "Notes"));

        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public void A_resolved_chapter_offers_a_way_in()
    {
        using var context = NewContext();
        var (_, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");

        var component = Render(context, chapter, "# Notes\n\nProse.\n");

        Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public void Blur_flushes_the_pending_body_without_leaving_the_editor()
    {
        using var context = NewContext();
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");
        var component = Render(context, chapter, "# Notes\n\nProse.\n");

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input("# Notes\n\nTyped while blurring.\n");
        component.Find("textarea").Blur();

        component.WaitForAssertion(
            () => Assert.Contains("Typed while blurring.", File.ReadAllText(Path.Combine(root, "notes.md")), StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // OnBlur means "flush now", never "leave the editor" — the caret comes
        // back to a textarea, not to a rendered body it has to click into again.
        Assert.NotEmpty(component.FindAll("textarea"));
    }

    [Fact]
    public void Done_flushes_the_pending_body_and_returns_to_reading()
    {
        using var context = NewContext();
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");
        var component = Render(context, chapter, "# Notes\n\nProse.\n");

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input("# Notes\n\nTyped before Done.\n");
        component.Find("[data-testid='knowledge-chapter-done']").Click();

        component.WaitForAssertion(
            () => Assert.Contains("Typed before Done.", File.ReadAllText(Path.Combine(root, "notes.md")), StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Empty(component.FindAll("textarea"));
    }

    [Fact]
    public void Typing_and_waiting_saves_on_its_own()
    {
        using var context = NewContext();
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");
        var component = Render(context, chapter, "# Notes\n\nProse.\n");

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input("# Notes\n\nTyped and left alone.\n");

        // No gesture at all: the debounce is the save, which is what makes the
        // absence of a save button honest rather than merely a missing button.
        component.WaitForAssertion(
            () => Assert.Contains("Typed and left alone.", File.ReadAllText(Path.Combine(root, "notes.md")), StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Moving_to_another_chapter_flushes_the_one_being_left()
    {
        using var context = NewContext();
        var (root, first) = Chapter("first.md", "# First\n\nProse.\n");
        var second = new KnowledgeChapterRef("arc42", root, "second.md");
        File.WriteAllText(Path.Combine(root, "second.md"), "# Second\n\nOther prose.\n");

        var component = Render(context, first, "# First\n\nProse.\n");
        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input("# First\n\nTyped before navigating.\n");

        component.Render(parameters => parameters
            .Add(editor => editor.Chapter, second)
            .Add(editor => editor.InitialText, "# Second\n\nOther prose.\n"));

        component.WaitForAssertion(
            () => Assert.Contains("Typed before navigating.", File.ReadAllText(Path.Combine(root, "first.md")), StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal("# Second\n\nOther prose.\n", File.ReadAllText(Path.Combine(root, "second.md")));
    }

    [Fact]
    public void A_write_that_fails_says_so_rather_than_looking_saved()
    {
        using var context = NewContext();
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");
        var component = Render(context, chapter, "# Notes\n\nProse.\n");

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        File.Delete(Path.Combine(root, "notes.md"));
        component.Find("textarea").Input("# Notes\n\nTyped into a file that went away.\n");
        component.Find("textarea").Blur();

        component.WaitForAssertion(
            () => Assert.Contains("Could not save", component.Find("[data-testid='knowledge-chapter-save-state']").TextContent, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.NotEmpty(component.FindAll("[data-testid='knowledge-chapter-save-error']"));
    }

    [Fact]
    public void A_saved_chapter_says_so()
    {
        using var context = NewContext();
        var (_, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");
        var component = Render(context, chapter, "# Notes\n\nProse.\n");

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input("# Notes\n\nTyped.\n");
        component.Find("textarea").Blur();

        // Saving... first, then Saved: the indicator is the only thing that ever
        // reports persistence, so it has to arrive at the resting state on its
        // own rather than being left mid-transition.
        component.WaitForAssertion(
            () => Assert.Equal("Saved", component.Find("[data-testid='knowledge-chapter-save-state']").TextContent.Trim()),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_reload_of_the_same_chapter_does_not_take_the_text_being_typed()
    {
        using var context = NewContext();
        var (_, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");
        var component = Render(context, chapter, "# Notes\n\nProse.\n");

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input("# Notes\n\nHalf a sentence");

        // A panel reloading after a status change hands the same chapter back
        // with the text as the file had it. Adopting that mid-sentence is how an
        // editor eats a keystroke.
        component.Render(parameters => parameters
            .Add(editor => editor.Chapter, chapter)
            .Add(editor => editor.InitialText, "# Notes\n\n```meta\nstatus: active\n```\n\nProse.\n"));

        Assert.Equal("# Notes\n\nHalf a sentence", component.Find("textarea").TextContent);
    }

    [Fact]
    public async Task A_write_that_lands_after_the_next_keystroke_leaves_the_newer_text_alone()
    {
        using var context = NewContext();
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");
        var component = Render(context, chapter, "# Notes\n\nProse.\n");

        const string First = "# Notes\n\nFirst pass.\n";
        const string Second = "# Notes\n\nTyped while the first write was still in flight.\n";

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input(First);

        // Both halves run on the renderer's dispatcher, which makes this a
        // sequence rather than a race: the flush reaches its first await and
        // queues the rest of itself behind whatever the dispatcher is doing, the
        // author types on, and only then can the write's continuation run. That is
        // the shape of the real thing — a save whose write is in flight while
        // somebody keeps typing.
        var saving = component.InvokeAsync(() =>
        {
            var flush = component.Instance.FlushAsync();
            component.Find("textarea").Input(Second);
            return flush;
        });

        await saving.WaitAsync(TimeSpan.FromSeconds(5));

        // The write that just landed was about the first pass, and the buffer has
        // moved on: adopting its text here would take the newer sentence off the
        // screen, and the save that follows would then persist the reverted body.
        Assert.Equal(Second, component.Find("textarea").TextContent);
        await component.WaitForAssertionAsync(
            () => Assert.Equal(Second, File.ReadAllText(Path.Combine(root, "notes.md"))),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_debounced_save_that_fails_unexpectedly_says_so_rather_than_saving_forever()
    {
        using var context = NewContext();
        var (_, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");

        var component = context.Render<KnowledgeChapterEditor>(parameters => parameters
            .Add(editor => editor.Chapter, chapter)
            .Add(editor => editor.InitialText, "# Notes\n\nProse.\n")
            .Add(editor => editor.Title, "Notes")
            // A failure of a type the save's filter never listed. The debounce is
            // the one save trigger nobody awaits, so this used to be observed by
            // nothing at all: the indicator stayed on "Saving..." and the reason
            // was collected by the garbage collector.
            .Add(editor => editor.OnSaved, () => throw new NotSupportedException("The panel refused to reload.")));

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input("# Notes\n\nTyped and left alone.\n");

        component.WaitForAssertion(
            () => Assert.Equal("Could not save", component.Find("[data-testid='knowledge-chapter-save-state']").TextContent.Trim()),
            TimeSpan.FromSeconds(5));
        Assert.Contains(
            "The panel refused to reload.",
            component.Find("[data-testid='knowledge-chapter-save-error']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_that_fails_on_the_way_out_is_reported_to_the_host()
    {
        using var context = NewContext();
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n");

        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var component = context.Render<KnowledgeChapterEditor>(parameters => parameters
            .Add(editor => editor.Chapter, chapter)
            .Add(editor => editor.InitialText, "# Notes\n\nProse.\n")
            .Add(editor => editor.Title, "Notes")
            .Add(editor => editor.OnSaveFailed, message => reported.TrySetResult(message)));

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input("# Notes\n\nTyped, and then the file went away.\n");

        // Disposal writes the last 750 ms of typing, and it is the one save whose
        // failure this surface cannot show anybody — the indicator and the alert
        // go away with it. So it says so to whoever is left instead of closing
        // quietly on a chapter that never reached disk.
        File.Delete(Path.Combine(root, "notes.md"));
        await context.DisposeComponentsAsync();

        var message = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("notes.md", message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static IRenderedComponent<KnowledgeChapterEditor> Render(BunitContext context, KnowledgeChapterRef chapter, string text) =>
        context.Render<KnowledgeChapterEditor>(parameters => parameters
            .Add(editor => editor.Chapter, chapter)
            .Add(editor => editor.InitialText, text)
            .Add(editor => editor.Title, "Notes"));

    private static BunitContext NewContext()
    {
        var context = new BunitContext();

        // The markdown editor watches its textarea through interop for the
        // highlight layer. None of that is what these tests are about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        return context;
    }

    private (string Root, KnowledgeChapterRef Chapter) Chapter(string relativePath, string markdown)
    {
        var root = Path.Combine(Path.GetTempPath(), "knowledge-chapter-editor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempDirs.Add(root);
        File.WriteAllText(Path.Combine(root, relativePath), markdown);

        return (root, new KnowledgeChapterRef("arc42", root, relativePath));
    }
}
