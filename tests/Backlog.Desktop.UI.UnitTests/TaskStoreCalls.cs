using System.Reflection;
using System.Runtime.ExceptionServices;

using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Counts every call the list state makes into the Tasks use cases, at the
/// moment the call is made rather than when it finishes.
/// <para>
/// That timing is the point. A debounce that fires when it should not starts
/// its save on the thread that moved the clock, but the save's store I/O comes
/// back on its own: a test that looked for the saved row right after moving the
/// clock would pass whether the save had been wrongly started or not. The call
/// into the use cases is made synchronously inside the timer callback, so a
/// count read straight after <c>Advance</c> already includes it.
/// </para>
/// </summary>
internal sealed class TaskStoreCalls
{
    private int _count;

    /// <summary>Calls made so far, of any kind — a save, and the read a save
    /// rebasing on the store makes first.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>The same use cases, with every call through them counted.</summary>
    public ITaskItems Watch(ITaskItems inner)
    {
        var proxy = DispatchProxy.Create<ITaskItems, CountingProxy>();
        var counting = (CountingProxy)(object)proxy;
        counting.Inner = inner;
        counting.Calls = this;
        return proxy;
    }

    private void Record() => Interlocked.Increment(ref _count);

    /// <summary>Public and unsealed because <see cref="DispatchProxy"/> derives
    /// the proxy type from it.</summary>
    public class CountingProxy : DispatchProxy
    {
        internal ITaskItems Inner { get; set; } = null!;

        internal TaskStoreCalls Calls { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            Calls.Record();

            try
            {
                return targetMethod.Invoke(Inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Throw(exception.InnerException);
                throw;
            }
        }
    }
}
