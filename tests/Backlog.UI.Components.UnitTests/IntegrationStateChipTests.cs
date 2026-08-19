namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The chip exists so that a slug, a word and a glyph cannot be mismatched. Most
/// of what is asserted here is therefore that all three arrived, together, from
/// one enum.
/// </summary>
public sealed class IntegrationStateChipTests
{
    [Theory]
    [InlineData(IntegrationArtifactState.Open, "open", "Open")]
    [InlineData(IntegrationArtifactState.Draft, "draft", "Draft")]
    [InlineData(IntegrationArtifactState.Merged, "merged", "Merged")]
    [InlineData(IntegrationArtifactState.Closed, "closed", "Closed")]
    [InlineData(IntegrationArtifactState.Unknown, "unknown", "Not checked")]
    public void An_artifact_state_carries_its_slug_its_word_and_its_glyph(
        IntegrationArtifactState state, string slug, string label)
    {
        // Colour MUST NOT be the only carrier of state. Here that is structural:
        // there is no parameter that renders one of the three without the others.
        using var context = new BunitContext();

        var chip = context.Render<IntegrationStateChip>(parameters => parameters
            .Add(c => c.Artifact, state)
            .Add(c => c.TestId, "chip")
            .Add(c => c.IconTestId, "glyph")
            .Add(c => c.LabelTestId, "label"));

        Assert.Contains($"badge--integration-{slug}", chip.Find("[data-testid='chip']").ClassList);
        Assert.Equal(label, chip.Find("[data-testid='label']").TextContent);
        Assert.Single(chip.FindAll("[data-testid='glyph'] svg"));
    }

    [Fact]
    public void Merged_is_not_closed()
    {
        // For a pull request they mean opposite things — the distinction the
        // product's own GitHub vocabulary already draws.
        using var context = new BunitContext();

        var merged = context.Render<IntegrationStateChip>(p => p.Add(c => c.Artifact, IntegrationArtifactState.Merged));
        var closed = context.Render<IntegrationStateChip>(p => p.Add(c => c.Artifact, IntegrationArtifactState.Closed));

        Assert.NotEqual(merged.Markup, closed.Markup);
    }

    [Fact]
    public void The_glyph_changes_with_the_kind_as_well_as_the_state()
    {
        // At Compact density, where the label is gone, this difference is the
        // only thing separating an open issue from an open pull request.
        using var context = new BunitContext();

        var issue = context.Render<IntegrationStateChip>(parameters => parameters
            .Add(c => c.Artifact, IntegrationArtifactState.Open)
            .Add(c => c.Kind, IntegrationLinkKind.Issue)
            .Add(c => c.IconTestId, "glyph"));

        var pull = context.Render<IntegrationStateChip>(parameters => parameters
            .Add(c => c.Artifact, IntegrationArtifactState.Open)
            .Add(c => c.Kind, IntegrationLinkKind.PullRequest)
            .Add(c => c.IconTestId, "glyph"));

        Assert.NotEqual(
            issue.Find("[data-testid='glyph']").InnerHtml,
            pull.Find("[data-testid='glyph']").InnerHtml);
    }

    [Theory]
    [InlineData(IntegrationSessionState.Waiting, "waiting", "Waiting for you")]
    [InlineData(IntegrationSessionState.Stalled, "stalled", "Stalled")]
    public void Waiting_and_stalled_are_kept_apart(
        IntegrationSessionState state, string slug, string label)
    {
        // Both look like nothing happening; one is correct and the other is what
        // the monitoring rules raise a signal on. Collapsing them would make the
        // signal unexplainable.
        using var context = new BunitContext();

        var chip = context.Render<IntegrationStateChip>(parameters => parameters
            .Add(c => c.Session, state)
            .Add(c => c.TestId, "chip")
            .Add(c => c.LabelTestId, "label"));

        Assert.Contains($"badge--integration-{slug}", chip.Find("[data-testid='chip']").ClassList);
        Assert.Equal(label, chip.Find("[data-testid='label']").TextContent);
    }

    [Fact]
    public void Icon_only_hides_the_word_without_removing_it()
    {
        // A chip that was only a colour and a shape is exactly what the
        // accessibility rules forbid.
        using var context = new BunitContext();

        var chip = context.Render<IntegrationStateChip>(parameters => parameters
            .Add(c => c.Session, IntegrationSessionState.Running)
            .Add(c => c.IconOnly, true)
            .Add(c => c.TestId, "chip")
            .Add(c => c.LabelTestId, "label"));

        var label = chip.Find("[data-testid='label']");

        Assert.Equal("Running", label.TextContent);
        Assert.Contains("sr-only", label.ClassList);

        // And the word reaches a pointer too, in front of the sentence.
        Assert.StartsWith("Running", chip.Find("[data-testid='chip']").GetAttribute("title") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void No_state_at_all_renders_nothing()
    {
        // Which is what makes it safe to write a drift chip unconditionally
        // beside a link that usually agrees with us.
        using var context = new BunitContext();

        var chip = context.Render<IntegrationStateChip>(parameters => parameters
            .Add(c => c.Drift, IntegrationDrift.None));

        Assert.Equal(string.Empty, chip.Markup.Trim());
    }

    [Theory]
    [InlineData(IntegrationDrift.LocalAhead, "local-ahead", "Still open")]
    [InlineData(IntegrationDrift.RemoteAhead, "remote-ahead", "Already closed")]
    [InlineData(IntegrationDrift.Detached, "detached", "Missing")]
    public void Drift_gets_three_words_rather_than_one(
        IntegrationDrift drift, string slug, string label)
    {
        // The reader's next move differs in each case, and a single "Mismatch"
        // would make them look interchangeable.
        using var context = new BunitContext();

        var chip = context.Render<IntegrationStateChip>(parameters => parameters
            .Add(c => c.Drift, drift)
            .Add(c => c.TestId, "chip")
            .Add(c => c.LabelTestId, "label"));

        Assert.Contains($"badge--integration-{slug}", chip.Find("[data-testid='chip']").ClassList);
        Assert.Equal(label, chip.Find("[data-testid='label']").TextContent);
    }

    [Fact]
    public void Text_rewords_a_state_without_recolouring_it()
    {
        // The same split StatusBadge draws between its Text and its Status.
        using var context = new BunitContext();

        var chip = context.Render<IntegrationStateChip>(parameters => parameters
            .Add(c => c.Artifact, IntegrationArtifactState.Open)
            .Add(c => c.Text, "Still open upstream")
            .Add(c => c.TestId, "chip")
            .Add(c => c.LabelTestId, "label"));

        Assert.Equal("Still open upstream", chip.Find("[data-testid='label']").TextContent);
        Assert.Contains("badge--integration-open", chip.Find("[data-testid='chip']").ClassList);
    }
}
