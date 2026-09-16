using Backlog.Modules.Tasks.Services;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The signal the repository raises on a local write and the sync loop hears.
/// What matters is the silence: the merge applies the other machines' documents
/// through the same repository and must not be heard as a local edit, or every
/// cycle that receives anything starts another on its heels.
/// </summary>
public sealed class TaskChangeSignalTests
{
    [Fact]
    public void Raising_reaches_the_listener()
    {
        var signal = new TaskChangeSignal();
        var heard = 0;
        signal.Changed += () => heard++;

        signal.Raise();

        Assert.Equal(1, heard);
    }

    [Fact]
    public void A_raise_inside_a_suppression_is_not_heard_and_one_after_it_is()
    {
        var signal = new TaskChangeSignal();
        var heard = 0;
        signal.Changed += () => heard++;

        using (signal.Suppress())
        {
            signal.Raise();
        }

        Assert.Equal(0, heard);

        signal.Raise();

        Assert.Equal(1, heard);
    }

    /// <summary>The merge wraps each write; a caller wrapping the whole page must
    /// not have its scope ended by the inner one.</summary>
    [Fact]
    public void Suppressions_nest()
    {
        var signal = new TaskChangeSignal();
        var heard = 0;
        signal.Changed += () => heard++;

        using (signal.Suppress())
        {
            using (signal.Suppress())
            {
            }

            signal.Raise();
        }

        Assert.Equal(0, heard);
    }

    /// <summary>The scope follows the awaiting flow and nothing else: a write on
    /// another flow while a suppression is open is still a local write.</summary>
    [Fact]
    public async Task A_suppression_on_one_flow_does_not_silence_another()
    {
        var signal = new TaskChangeSignal();
        var heard = 0;
        signal.Changed += () => Interlocked.Increment(ref heard);

        var suppressedFlowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherFlowDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var suppressed = Task.Run(async () =>
        {
            using (signal.Suppress())
            {
                suppressedFlowStarted.SetResult();
                await otherFlowDone.Task;
                signal.Raise();
            }
        });

        await suppressedFlowStarted.Task;

        // A different flow, started outside the suppression.
        await Task.Run(() => signal.Raise());
        otherFlowDone.SetResult();
        await suppressed;

        Assert.Equal(1, Volatile.Read(ref heard));
    }
}
