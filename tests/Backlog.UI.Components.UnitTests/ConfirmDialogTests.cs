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
