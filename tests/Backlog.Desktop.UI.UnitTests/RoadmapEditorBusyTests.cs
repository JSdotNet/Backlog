using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.UI;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The roadmap dialogs' Save and Delete while the band answers them
/// (.devbook/design/interaction-guidelines.md, Busy Actions): the pressed button is
/// busy from the press until the band has answered — focusable, its label kept — the
/// other write waits on it, and a second press does not write twice.
/// </summary>
public sealed class RoadmapEditorBusyTests : IDisposable
{
    private readonly BunitContext _context = new();

    public RoadmapEditorBusyTests()
    {
        // The modal reaches for JS to trap focus; nothing asserted here depends on it.
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public void Dispose() => _context.Dispose();

    private static RoadmapItemDto Item() =>
        new(
            Guid.NewGuid(),
            "Ship the thing",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            PlanningPriority.Medium,
            [],
            Lane: null,
            TaskId: null,
            DependsOn: [],
            Tag: "sync");

    [Fact]
    public void Save_is_busy_until_the_band_has_answered_and_a_second_press_is_dropped()
    {
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = 0;

        var editor = _context.Render<RoadmapItemEditor>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Item, Item())
            .Add(p => p.OnSave, (RoadmapEditorSubmission _) =>
            {
                Interlocked.Increment(ref saves);
                return answer.Task;
            }));

        editor.Find("[data-testid='roadmap-editor-save']").Click();

        editor.WaitForAssertion(() =>
        {
            var save = editor.Find("[data-testid='roadmap-editor-save']");
            Assert.Equal("true", save.GetAttribute("aria-busy"));
            Assert.Equal("true", save.GetAttribute("aria-disabled"));
            Assert.False(save.HasAttribute("disabled"));
            Assert.Contains("Save", save.TextContent, StringComparison.Ordinal);

            // Blocked, but still in the tab order.
            var delete = editor.Find("[data-testid='roadmap-editor-delete']");
            Assert.Equal("true", delete.GetAttribute("aria-disabled"));
            Assert.False(delete.HasAttribute("disabled"));
            Assert.Equal(1, Volatile.Read(ref saves));
        });

        editor.Find("[data-testid='roadmap-editor-save']").Click();
        answer.SetResult();

        editor.WaitForAssertion(() =>
        {
            Assert.Null(editor.Find("[data-testid='roadmap-editor-save']").GetAttribute("aria-busy"));
            Assert.Null(editor.Find("[data-testid='roadmap-editor-delete']").GetAttribute("aria-disabled"));
        });
        Assert.Equal(1, Volatile.Read(ref saves));
    }

    [Fact]
    public void Delete_is_busy_until_the_band_has_answered_and_save_waits_on_it()
    {
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deletes = 0;
        var saves = 0;

        var editor = _context.Render<RoadmapItemEditor>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Item, Item())
            .Add(p => p.OnSave, (RoadmapEditorSubmission _) => Interlocked.Increment(ref saves))
            .Add(p => p.OnDelete, (Guid _) =>
            {
                Interlocked.Increment(ref deletes);
                return answer.Task;
            }));

        editor.Find("[data-testid='roadmap-editor-delete']").Click();

        editor.WaitForAssertion(() =>
        {
            var delete = editor.Find("[data-testid='roadmap-editor-delete']");
            Assert.Equal("true", delete.GetAttribute("aria-busy"));
            Assert.False(delete.HasAttribute("disabled"));
            var save = editor.Find("[data-testid='roadmap-editor-save']");
            Assert.Equal("true", save.GetAttribute("aria-disabled"));
            Assert.False(save.HasAttribute("disabled"));
        });

        // A second Delete, and a Save while Delete runs, are both dropped.
        editor.Find("[data-testid='roadmap-editor-delete']").Click();
        editor.Find("[data-testid='roadmap-editor-save']").Click();
        answer.SetResult();

        editor.WaitForAssertion(() =>
            Assert.Null(editor.Find("[data-testid='roadmap-editor-delete']").GetAttribute("aria-busy")));
        Assert.Equal(1, Volatile.Read(ref deletes));
        Assert.Equal(0, Volatile.Read(ref saves));
    }

    [Fact]
    public void A_dates_save_is_busy_until_the_band_has_answered()
    {
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = 0;

        var editor = _context.Render<RoadmapMilestoneEditor>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Today, new DateOnly(2026, 3, 2))
            .Add(p => p.OnSave, (RoadmapMilestoneSubmission _) =>
            {
                Interlocked.Increment(ref saves);
                return answer.Task;
            }));

        editor.Find("[data-testid='roadmap-milestone-editor-title'] input").Input("1.0");
        editor.Find("[data-testid='roadmap-milestone-editor-save']").Click();

        editor.WaitForAssertion(() =>
        {
            var save = editor.Find("[data-testid='roadmap-milestone-editor-save']");
            Assert.Equal("true", save.GetAttribute("aria-busy"));
            Assert.False(save.HasAttribute("disabled"));
        });

        editor.Find("[data-testid='roadmap-milestone-editor-save']").Click();
        answer.SetResult();

        editor.WaitForAssertion(() =>
            Assert.Null(editor.Find("[data-testid='roadmap-milestone-editor-save']").GetAttribute("aria-busy")));
        Assert.Equal(1, Volatile.Read(ref saves));
    }
}
