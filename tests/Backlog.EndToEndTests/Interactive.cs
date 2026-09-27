using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// Acting on a Blazor Server page before and after it becomes interactive.
///
/// <para>Both harnesses prerender. The first HTML a page answers with is inert:
/// a value typed into it is replaced when the circuit attaches and renders the
/// component again, and a click on it goes nowhere. Nothing on the page marks the
/// moment it becomes interactive, so rather than guess a delay these helpers
/// repeat the act until the page shows it took, the way a person would press a
/// button again that did not respond.</para>
/// </summary>
internal static class Interactive
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Types into a field until the page reacts the way
    /// <paramref name="took"/> says it should.</summary>
    public static Task FillAsync(ILocator input, string value, Func<Task<bool>> took) =>
        RepeatAsync(() => input.FillAsync(value), took, $"typing '{value}' into {input}");

    /// <summary>Types into a field until the field keeps what was typed.</summary>
    public static Task FillAsync(ILocator input, string value) =>
        FillAsync(input, value, async () => await input.InputValueAsync() == value);

    /// <summary>Clicks until <paramref name="took"/> is visible.</summary>
    public static Task ClickAsync(ILocator target, ILocator took) =>
        RepeatAsync(() => target.ClickAsync(), () => took.IsVisibleAsync(), $"clicking {target}");

    public static async Task RepeatAsync(Func<Task> act, Func<Task<bool>> took, string what)
    {
        var until = DateTime.UtcNow + Patience;
        while (true)
        {
            await act();
            for (var settle = 0; settle < 5; settle++)
            {
                await Task.Delay(200);
                if (await took()) return;
            }

            if (DateTime.UtcNow > until) throw new TimeoutException($"The page never reacted to {what}.");
        }
    }

    /// <summary>Polls until a condition holds — for state that arrives on its own
    /// schedule, such as an outbox flush, rather than because of an act.</summary>
    public static async Task EventuallyAsync(Func<Task<bool>> condition, TimeSpan within, string what)
    {
        var until = DateTime.UtcNow + within;
        while (!await condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException($"Timed out waiting for {what}.");
            await Task.Delay(500);
        }
    }
}
