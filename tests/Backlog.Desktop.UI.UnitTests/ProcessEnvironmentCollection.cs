namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Tests that set a process-wide environment variable, and must therefore not
/// share a process with tests doing unrelated work at the same time.
///
/// <para>The environment block belongs to the whole process, and every child
/// process a sibling test starts inherits it. A test that sets a variable and
/// restores it in a <c>finally</c> is only tidy for itself: anything running
/// beside it can see the value appear or vanish mid-test. The variable is real
/// on purpose — it is what proves the code under test never reads it — which is
/// why this is a collection and not a fake lookup in its place.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "Process environment";
}
