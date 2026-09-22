namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The line a copied entry opens with. The pane tests
/// (<see cref="TasksCopyForPromptTests"/>) prove the line reaches the clipboard
/// with the entry under it; this pins the line itself, which is a slash command
/// the host resolves before any skill reads it — so its spelling is the
/// contract, down to the plugin prefix and the closing colon.
/// </summary>
public sealed class EntryRunMarkerTests
{
    [Fact]
    public void The_line_invokes_the_run_skill_with_the_stored_id()
    {
        var id = Guid.Parse("0f3f1b1a-6a2c-4d0e-9b3f-8a1c2d3e4f50");

        Assert.Equal(
            "/backlog-tools:backlog-run-plan-item entry `0f3f1b1a-6a2c-4d0e-9b3f-8a1c2d3e4f50`:",
            EntryRunMarker.Build(id));
    }
}
