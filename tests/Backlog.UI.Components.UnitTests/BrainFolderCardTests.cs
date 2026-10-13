using Backlog.UI.Components.Brain;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a host is promised about the Brain card: an empty field with no default,
/// Browse only where there is a picker, the sync state said as information, the
/// database line always, and Create conventions only while a convention file is
/// missing.
/// </summary>
public sealed class BrainFolderCardTests
{
    [Fact]
    public void With_no_folder_the_field_is_empty_and_only_the_database_line_shows()
    {
        using var context = new BunitContext();

        var card = context.Render<BrainFolderCard>(parameters => parameters.Add(c => c.TestId, "brain"));

        var field = card.Find("[data-testid='brain-folder']");
        Assert.Equal(string.Empty, field.GetAttribute("value") ?? string.Empty);
        Assert.False(string.IsNullOrWhiteSpace(field.GetAttribute("placeholder")));
        Assert.Contains("never synced", card.Find("[data-testid='brain-database']").TextContent, StringComparison.Ordinal);
        Assert.Empty(card.FindAll("[data-testid='brain-synced']"));
        Assert.Empty(card.FindAll("[data-testid='brain-readme']"));
        Assert.Empty(card.FindAll("[data-testid='brain-create-conventions']"));
    }

    [Fact]
    public void Browse_is_drawn_only_where_the_host_has_a_picker()
    {
        using var context = new BunitContext();

        var without = context.Render<BrainFolderCard>(parameters => parameters.Add(c => c.TestId, "brain"));
        Assert.Empty(without.FindAll("[data-testid='brain-browse']"));

        var browsed = 0;
        var with = context.Render<BrainFolderCard>(parameters => parameters
            .Add(c => c.TestId, "brain")
            .Add(c => c.CanBrowse, true)
            .Add(c => c.OnBrowse, () => browsed++));

        with.Find("[data-testid='brain-browse']").Click();
        Assert.Equal(1, browsed);
    }

    /// <summary>A synced folder is the expected home of a Brain, so it is said
    /// as a status line, never as an alert.</summary>
    [Fact]
    public void A_synced_folder_is_information_not_a_warning()
    {
        using var context = new BunitContext();

        var card = context.Render<BrainFolderCard>(parameters => parameters
            .Add(c => c.TestId, "brain")
            .Add(c => c.Folder, @"C:\Users\sam\OneDrive\Brain")
            .Add(c => c.Status, new BrainFolderCardStatus("OneDrive", HasReadme: true, HasConventions: true)));

        var synced = card.Find("[data-testid='brain-synced']");
        Assert.Contains("Synced by OneDrive", synced.TextContent, StringComparison.Ordinal);
        Assert.NotNull(synced.QuerySelector(".brain-card__dot--info"));
        Assert.Empty(card.FindAll("[role='alert']"));
    }

    [Fact]
    public void Create_conventions_is_offered_while_either_file_is_missing()
    {
        using var context = new BunitContext();
        var created = 0;

        var card = context.Render<BrainFolderCard>(parameters => parameters
            .Add(c => c.TestId, "brain")
            .Add(c => c.Folder, @"D:\Notes\Brain")
            .Add(c => c.Status, new BrainFolderCardStatus(null, HasReadme: true, HasConventions: false))
            .Add(c => c.OnCreateConventions, () => created++));

        Assert.Contains("found", card.Find("[data-testid='brain-readme']").TextContent, StringComparison.Ordinal);
        var conventions = card.Find("[data-testid='brain-conventions']");
        Assert.Contains("missing", conventions.TextContent, StringComparison.Ordinal);
        Assert.NotNull(conventions.QuerySelector(".brain-card__dot--missing"));

        card.Find("[data-testid='brain-create-conventions']").Click();
        Assert.Equal(1, created);

        card.Render(parameters => parameters.Add(c => c.Status, new BrainFolderCardStatus(null, HasReadme: true, HasConventions: true)));
        Assert.Empty(card.FindAll("[data-testid='brain-create-conventions']"));
    }

    [Fact]
    public void Typing_hands_the_draft_back_and_leaving_the_field_commits()
    {
        using var context = new BunitContext();
        string? draft = null;
        string? committed = null;

        var card = context.Render<BrainFolderCard>(parameters => parameters
            .Add(c => c.TestId, "brain")
            .Add(c => c.FolderChanged, value => draft = value)
            .Add(c => c.OnCommit, value => committed = value));

        var field = card.Find("[data-testid='brain-folder']");
        field.Input(@"D:\Notes\Brain");
        Assert.Equal(@"D:\Notes\Brain", draft);
        Assert.Null(committed);

        // The commit carries the field's own value, not the last draft.
        card.Find("[data-testid='brain-folder']").Change(@"D:\Notes\Other");
        Assert.Equal(@"D:\Notes\Other", committed);
    }

    [Fact]
    public void A_refusal_is_an_alert_the_field_points_at()
    {
        using var context = new BunitContext();

        var card = context.Render<BrainFolderCard>(parameters => parameters
            .Add(c => c.TestId, "brain")
            .Add(c => c.Folder, "notes")
            .Add(c => c.Error, "Use a full path."));

        var alert = card.Find("[data-testid='brain-error']");
        Assert.Contains("Use a full path.", alert.TextContent, StringComparison.Ordinal);
        Assert.Contains("brain-folder-error", card.Find("[data-testid='brain-folder']").GetAttribute("aria-describedby"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_carries_the_id_a_link_scrolls_to()
    {
        using var context = new BunitContext();

        var card = context.Render<BrainFolderCard>();

        Assert.Equal("brain-folder", card.Find("section").Id);
        Assert.Equal("brain-folder-input", card.Find("input").Id);
    }
}
