using System.Reflection;

namespace Backlog.Infrastructure.DevPc.UnitTests;

/// <summary>
/// The catalog-only configuration answers every port member, and the adapter
/// hands every one of them to it.
///
/// <para>The first half is the compiler's: <see cref="CatalogOnlyDevTools"/>
/// implements <see cref="IDevToolService"/>. The second half is not — each member
/// of <see cref="DevToolService"/> opens with a guard written by hand, and a new
/// port member that forgot it would compile and then run the CLIs from the web
/// harness. This suite walks the port by reflection, so a member added to it is
/// checked here without anybody adding a test for it.</para>
/// </summary>
public class CatalogOnlyDelegationTests
{
    public static TheoryData<string> PortMembers()
    {
        var members = new TheoryData<string>();
        foreach (var method in typeof(IDevToolService).GetMethods())
        {
            members.Add(method.Name);
        }

        return members;
    }

    [Fact]
    public void The_port_is_walked_whole()
    {
        var names = PortMembers().Select(row => row.Data).ToList();

        // A reflection walk that found nothing would pass every theory below by
        // running none of them.
        Assert.Contains(nameof(IDevToolService.ListAsync), names);
        Assert.Contains("add_Changed", names);
        Assert.Contains("remove_Changed", names);
    }

    [Fact]
    public void The_catalog_only_configuration_implements_the_port_itself()
    {
        Assert.True(typeof(IDevToolService).IsAssignableFrom(typeof(CatalogOnlyDevTools)));
    }

    [Theory]
    [MemberData(nameof(PortMembers))]
    public async Task Every_port_member_is_forwarded_in_the_catalog_only_configuration(string member)
    {
        var recorder = DispatchProxy.Create<IDevToolService, Recorder>();
        var service = new DevToolService(recorder);
        var method = typeof(IDevToolService).GetMethod(member)!;

        // Cancelled, so that a member which failed to forward gives up at its
        // first cancellation check instead of running anything.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var arguments = method.GetParameters().Select(parameter => ArgumentFor(parameter, cancelled.Token)).ToArray();

        try
        {
            if (method.Invoke(service, arguments) is Task task)
            {
                await task;
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is Forwarded)
        {
        }
        catch (Forwarded)
        {
        }
        catch (Exception ex) when (ex is OperationCanceledException
            || ex.InnerException is OperationCanceledException
            || ex is TargetInvocationException)
        {
            // Not forwarded: the assertion below says which member.
        }

        Assert.Equal([member], ((Recorder)(object)recorder).Calls);
    }

    private static object? ArgumentFor(ParameterInfo parameter, CancellationToken cancelled) =>
        parameter.ParameterType switch
        {
            var type when type == typeof(CancellationToken) => cancelled,
            var type when type == typeof(string) => "plugin:sample",
            var type when type == typeof(bool) => true,
            var type when type == typeof(Action) => (Action)(() => { }),
            _ => null
        };

    /// <summary>Records which port member reached it, then stops the call there,
    /// so nothing past the forward can run.</summary>
    public class Recorder : DispatchProxy
    {
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            throw new Forwarded();
        }
    }

    private sealed class Forwarded : Exception;
}
