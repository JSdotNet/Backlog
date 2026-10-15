namespace Backlog.UI.Components.UnitTests;

public sealed class ConfirmDialogTests
{
    [Fact]
    public void Confirm_comes_from_the_confirm_button()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var confirmed = 0;
        var cancelled = 0;

        var dialog = context.Render<ConfirmDialog>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.Message, "Delete this entry?")
            .Add(d => d.ConfirmTestId, "confirm")
            .Add(d => d.OnConfirm, () => confirmed++)
            .Add(d => d.OnCancel, () => cancelled++));

        dialog.Find("[data-testid='confirm']").Click();

        Assert.Equal(1, confirmed);
        Assert.Equal(0, cancelled);
    }

    /// <summary>Closing first left the reader on the screen they came from with
    /// nothing saying the act was still running (interaction-guidelines.md#busy-actions).</summary>
    [Fact]
    public async Task Confirm_stays_open_and_busy_until_the_act_has_finished()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var confirmed = 0;
        var cancelled = 0;
        var act = new TaskCompletionSource();

        var dialog = context.Render<ConfirmDialog>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.Message, "Delete this entry?")
            .Add(d => d.ConfirmTestId, "confirm")
            .Add(d => d.CancelTestId, "cancel")
            .Add(d => d.OnConfirm, () => { confirmed++; return act.Task; })
            .Add(d => d.OnCancel, () => cancelled++));

        _ = dialog.Find("[data-testid='confirm']").ClickAsync(new MouseEventArgs());

        dialog.WaitForAssertion(() =>
        {
            var confirm = dialog.Find("[data-testid='confirm']");
            Assert.Equal("true", confirm.GetAttribute("aria-busy"));
            Assert.False(confirm.HasAttribute("disabled"));
        });

        // A second press and a cancel both do nothing while the act runs.
        await dialog.Find("[data-testid='confirm']").ClickAsync(new MouseEventArgs());
        Assert.True(dialog.Find("[data-testid='cancel']").HasAttribute("disabled"));
        await dialog.Find("[role='dialog']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(1, confirmed);
        Assert.Equal(0, cancelled);
        Assert.NotEmpty(dialog.FindAll("[role='dialog']"));

        await dialog.InvokeAsync(act.SetResult);

        dialog.WaitForAssertion(() => Assert.Empty(dialog.FindAll("[role='dialog']")));
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public async Task A_failed_confirm_leaves_the_dialog_open_and_confirm_usable_again()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var attempts = 0;

        var dialog = context.Render<ConfirmDialog>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.ConfirmTestId, "confirm")
            .Add(d => d.OnConfirm, () =>
            {
                attempts++;
                return Task.FromException(new InvalidOperationException("The file is locked."));
            }));

        // The host's failure is the host's to report, so it goes on past the
        // dialog; the dialog only restores itself.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dialog.Find("[data-testid='confirm']").ClickAsync(new MouseEventArgs()));

        Assert.NotEmpty(dialog.FindAll("[role='dialog']"));
        Assert.False(dialog.Find("[data-testid='confirm']").HasAttribute("aria-busy"));
        Assert.Equal(1, attempts);
    }

    /// <summary>The fault arrives after the act has awaited, which is the shape a
    /// real file or network failure takes. ComponentBase renders after an
    /// awaited handler only when it completes, so without the dialog rendering
    /// itself as the act settles, it stayed drawn busy — Cancel and the close
    /// disabled, Escape off — with no way out.</summary>
    [Fact]
    public async Task A_confirm_that_fails_after_an_await_restores_every_way_out()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var act = new TaskCompletionSource();
        var cancelled = 0;

        var dialog = context.Render<ConfirmDialog>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.Title, "Delete entry")
            .Add(d => d.ConfirmTestId, "confirm")
            .Add(d => d.CancelTestId, "cancel")
            .Add(d => d.OnConfirm, () => act.Task)
            .Add(d => d.OnCancel, () => cancelled++));

        var click = dialog.Find("[data-testid='confirm']").ClickAsync(new MouseEventArgs());

        dialog.WaitForAssertion(() =>
            Assert.Equal("true", dialog.Find("[data-testid='confirm']").GetAttribute("aria-busy")));
        Assert.True(dialog.Find(".modal__close").HasAttribute("disabled"));

        await dialog.InvokeAsync(() => act.SetException(new InvalidOperationException("The file is locked.")));

        try
        {
            await click;
        }
        catch (InvalidOperationException)
        {
            // The host's failure goes on past the dialog; that is not under test here.
        }

        Assert.NotEmpty(dialog.FindAll("[role='dialog']"));
        Assert.False(dialog.Find("[data-testid='confirm']").HasAttribute("aria-busy"));
        Assert.False(dialog.Find("[data-testid='cancel']").HasAttribute("disabled"));
        Assert.False(dialog.Find(".modal__close").HasAttribute("disabled"));

        dialog.Find("[data-testid='cancel']").Click();
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void Cancel_comes_from_the_cancel_button()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var confirmed = 0;
        var cancelled = 0;

        var dialog = context.Render<ConfirmDialog>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.Message, "Delete this entry?")
            .Add(d => d.CancelTestId, "cancel")
            .Add(d => d.OnConfirm, () => confirmed++)
            .Add(d => d.OnCancel, () => cancelled++));

        dialog.Find("[data-testid='cancel']").Click();

        Assert.Equal(1, cancelled);
        Assert.Equal(0, confirmed);
    }

    [Fact]
    public void A_destructive_confirm_is_the_danger_button()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var safe = context.Render<ConfirmDialog>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.ConfirmTestId, "confirm"));
        var destructive = context.Render<ConfirmDialog>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.Destructive, true)
            .Add(d => d.ConfirmTestId, "confirm"));

        Assert.Contains("btn--primary", safe.Find("[data-testid='confirm']").ClassList);
        Assert.Contains("btn--danger", destructive.Find("[data-testid='confirm']").ClassList);
    }

    [Fact]
    public void Dismissing_the_dialog_counts_as_cancelling()
    {
        // Escape and the backdrop both mean "no", so a destructive action can
        // never be taken by dismissing the question.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var cancelled = 0;
        var confirmed = 0;

        var dialog = context.Render<ConfirmDialog>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.OnCancel, () => cancelled++)
            .Add(d => d.OnConfirm, () => confirmed++));

        dialog.Find("[role='dialog']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, cancelled);
        Assert.Equal(0, confirmed);
    }
}
