using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Disposal of the explorer around the moments interop is not there.
/// <para>
/// Under Blazor Server a page is prerendered first, and that static pass never
/// runs <c>OnAfterRender</c> — yet its request scope still disposes every
/// component when the response completes. A dispose that asks JS to tear down a
/// viewer that was never attached hits a runtime with no circuit behind it, and
/// Kestrel logs the <see cref="InvalidOperationException"/> as an unhandled
/// application error on every page load. The same shape appears when the circuit
/// drops while a call is in flight. None of it is an error worth surfacing: there
/// is nothing left to detach from.
/// </para>
/// </summary>
public sealed class C4ExplorerDisposalTests
{
    private const string Source = """
        workspace "Backlog" "For the disposal tests" {
            model {
                me = person "ME" "The owner"
                backlog = softwareSystem "Prompt Backlog" "The system"
                me -> backlog "Uses"
            }
            views {
                systemLandscape "Landscape" {
                    include *
                }
            }
        }
        """;

    private static C4Workspace Workspace() => C4DslReader.Read(Source);

    [Fact]
    public async Task Disposing_before_the_first_render_issues_no_interop()
    {
        // The sharpest form of the prerender case: a component that never rendered
        // has never attached a viewer, so there is nothing to ask JS to release —
        // and no runtime that could take the call.
        var explorer = new C4Explorer();

        var exception = await Record.ExceptionAsync(async () => await explorer.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Disposing_from_a_static_render_does_not_throw()
    {
        // What the prerender scope actually does: the component rendered once, no
        // OnAfterRender ever attached anything, and the runtime it was handed refuses
        // every call because there is no circuit yet.
        using var context = new BunitContext();
        var runtime = new StaticRenderRuntime();
        context.Services.AddSingleton<IJSRuntime>(runtime);

        context.Render<C4Explorer>(parameters => parameters.Add(c => c.Workspace, C4Workspace.Empty));

        var exception = await Record.ExceptionAsync(context.DisposeComponentsAsync);

        Assert.Null(exception);
        Assert.Equal(0, runtime.Calls);
    }

    [Fact]
    public async Task Disposing_after_the_viewer_attached_detaches_it_once()
    {
        // The guard must not over-suppress: once a viewer is attached, disposal
        // still releases it.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Render<C4Explorer>(parameters => parameters.Add(c => c.Workspace, Workspace()));
        await context.DisposeComponentsAsync();

        Assert.Single(context.JSInterop.Invocations["backlogC4Explorer.dispose"]);
    }

    [Fact]
    public async Task Losing_the_circuit_while_disposing_is_not_an_error()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupVoid("backlogC4Explorer.dispose", _ => true)
            .SetException(new JSDisconnectedException("The circuit is gone."));

        context.Render<C4Explorer>(parameters => parameters.Add(c => c.Workspace, Workspace()));

        var exception = await Record.ExceptionAsync(context.DisposeComponentsAsync);

        Assert.Null(exception);
    }

    [Fact]
    public void Losing_the_circuit_while_attaching_is_not_an_error()
    {
        // The attach runs from OnAfterRender; a circuit that drops mid-call must
        // degrade the same way a missing viewer script does, not fault the circuit.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupVoid("backlogC4Explorer.attach", _ => true)
            .SetException(new JSDisconnectedException("The circuit is gone."));

        var exception = Record.Exception(() =>
            context.Render<C4Explorer>(parameters => parameters.Add(c => c.Workspace, Workspace())));

        Assert.Null(exception);
    }

    /// <summary>
    /// What <c>RemoteJSRuntime</c> is before a circuit exists: every call is refused
    /// with the message Kestrel logged.
    /// </summary>
    private sealed class StaticRenderRuntime : IJSRuntime
    {
        public int Calls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => Refuse<TValue>();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => Refuse<TValue>();

        private ValueTask<TValue> Refuse<TValue>()
        {
            Calls++;
            throw new InvalidOperationException(
                "JavaScript interop calls cannot be issued at this time. This is because the component is being statically rendered. " +
                "When prerendering is enabled, JavaScript interop calls can only be performed during the OnAfterRenderAsync lifecycle method.");
        }
    }
}
