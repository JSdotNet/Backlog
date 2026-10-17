using System.Text.Json;

using Backlog.Infrastructure.FileSystem;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The store decides which surface the shell reopens on. The answer has to
/// survive a restart, and a missing or corrupt file must never stop the app
/// from opening — it just means there is nothing to restore yet.
/// </summary>
public sealed class ShellNavigationStoreTests
{
    [Fact]
    public void Nothing_is_remembered_before_anything_is_set()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            Assert.Null(store.LastSurface);
            Assert.Equal("Tasks", store.LastView);
            Assert.Equal(path, store.SettingsPath);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The header used to hold a pin on each pane, and the file carried the pinned
    /// subset under its own key. Pins are gone, and so are the side panes (local ADR
    /// 0022, amendment of 2026-10-15), so a file from before still reads — the
    /// Devbook it had open beside the view comes back as the Devbook surface — and
    /// the next save writes it with neither key rather than carrying dead state
    /// forward.
    /// </summary>
    [Fact]
    public void A_file_from_before_pins_left_still_reads_and_is_rewritten_without_them()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "lastSurface": "Workspace",
                  "lastEnabledPanes": ["Backlog", "Devbook"],
                  "lastPinnedPanes": ["Backlog"]
                }
                """);

            var store = new ShellNavigationStore(path);

            Assert.Equal("Devbook", store.LastSurface);

            store.SetLastView("Board");

            using var document = JsonDocument.Parse(Written(path));
            Assert.False(document.RootElement.TryGetProperty("lastPinnedPanes", out _));
            Assert.False(document.RootElement.TryGetProperty("lastEnabledPanes", out _));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The regression this pins: an earlier version of this store wrote the
    /// surface and the side panes through two separate serializations, each holding
    /// only its own field — so setting one silently erased whatever the other
    /// had just remembered. The panes are gone; the surface and the view it sits
    /// over have to survive being set in either order.
    /// </summary>
    [Fact]
    public void Setting_the_surface_does_not_erase_the_remembered_view_or_the_reverse()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            store.SetLastView("Board");
            store.SetLastSurface("Dashboard");

            Assert.Equal("Dashboard", store.LastSurface);
            Assert.Equal("Board", store.LastView);

            var restarted = new ShellNavigationStore(path);

            Assert.Equal("Dashboard", restarted.LastSurface);
            Assert.Equal("Board", restarted.LastView);

            restarted.SetLastSurface("Workspace");

            Assert.Equal("Board", restarted.LastView);

            var restartedAgain = new ShellNavigationStore(path);

            Assert.Equal("Workspace", restartedAgain.LastSurface);
            Assert.Equal("Board", restartedAgain.LastView);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void The_last_surface_survives_a_restart()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            store.SetLastSurface("Sessions");

            var restarted = new ShellNavigationStore(path);

            Assert.Equal("Sessions", restarted.LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Setting_the_same_surface_again_is_a_no_op()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            store.SetLastSurface("Tools");

            var changes = 0;
            store.Changed += () => changes++;
            store.SetLastSurface("Tools");

            Assert.Equal(0, changes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Changing_the_surface_says_so()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            var changes = 0;
            store.Changed += () => changes++;

            store.SetLastSurface("Dashboard");
            store.SetLastSurface("Workspace");

            Assert.Equal(2, changes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>A file nobody can read must never stop the app from opening, and
    /// the shell falls back to its own default rather than a stale or broken
    /// surface name.</summary>
    [Fact]
    public void A_corrupt_file_falls_back_to_nothing_remembered()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not json");

            var store = new ShellNavigationStore(path);

            Assert.Null(store.LastSurface);
            Assert.Equal("Tasks", store.LastView);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void A_missing_file_is_read_back_as_nothing_remembered()
    {
        var path = NewSettingsPath();

        try
        {
            Assert.False(File.Exists(path));
            Assert.Null(new ShellNavigationStore(path).LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void The_file_holds_the_surface_under_its_own_key_and_no_pane_list()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            store.SetLastSurface("Tools");

            using var document = JsonDocument.Parse(Written(path));

            Assert.Equal("Tools", document.RootElement.GetProperty("lastSurface").GetString());
            Assert.False(document.RootElement.TryGetProperty("lastEnabledPanes", out _));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The roadmap's Hours switch (local ADR 0019, §4) is on until somebody turns it off,
    /// and off survives a restart. The key is written only while it is off, so a file
    /// from before the switch, and one with it on, keep the shape they had.
    /// </summary>
    [Fact]
    public void The_roadmap_hours_switch_is_on_until_turned_off_and_off_survives_a_restart()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            Assert.True(store.RoadmapHoursShown);

            store.SetLastSurface("Tools");
            using (var on = JsonDocument.Parse(Written(path)))
            {
                Assert.False(on.RootElement.TryGetProperty("roadmapHoursShown", out _));
            }

            store.SetRoadmapHoursShown(false);

            var restarted = new ShellNavigationStore(path);
            Assert.False(restarted.RoadmapHoursShown);
            Assert.Equal("Tools", restarted.LastSurface);

            restarted.SetRoadmapHoursShown(true);
            Assert.True(new ShellNavigationStore(path).RoadmapHoursShown);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The roadmap's Completed switch shows finished work until somebody turns it off,
    /// and off survives a restart. Like Hours, the key is written only while it is off.
    /// </summary>
    [Fact]
    public void The_roadmap_completed_switch_is_on_until_turned_off_and_off_survives_a_restart()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            Assert.True(store.RoadmapCompletedShown);

            store.SetLastSurface("Tools");
            using (var on = JsonDocument.Parse(Written(path)))
            {
                Assert.False(on.RootElement.TryGetProperty("roadmapCompletedShown", out _));
            }

            store.SetRoadmapCompletedShown(false);
            Assert.False(new ShellNavigationStore(path).RoadmapCompletedShown);

            store.SetRoadmapCompletedShown(true);
            Assert.True(new ShellNavigationStore(path).RoadmapCompletedShown);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The Calendar's "Show plans" box is off on a device that never chose, and the key
    /// is written only while it is on — so a missing setting reads as off.
    /// </summary>
    [Fact]
    public void Calendar_plans_are_off_until_turned_on_and_only_on_writes_a_key()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            Assert.False(store.CalendarPlansShown);

            store.SetLastSurface("Tools");
            using (var off = JsonDocument.Parse(Written(path)))
            {
                Assert.False(off.RootElement.TryGetProperty("calendarPlansShown", out _));
            }

            store.SetCalendarPlansShown(true);
            using (var on = JsonDocument.Parse(Written(path)))
            {
                Assert.True(on.RootElement.GetProperty("calendarPlansShown").GetBoolean());
            }

            var restarted = new ShellNavigationStore(path);
            Assert.True(restarted.CalendarPlansShown);
            Assert.Equal("Tools", restarted.LastSurface);

            restarted.SetCalendarPlansShown(false);
            Assert.False(new ShellNavigationStore(path).CalendarPlansShown);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// A file from when the box was on by default says <c>false</c> once the reader
    /// turned it off; that choice stays off.
    /// </summary>
    [Fact]
    public void A_stored_calendar_plans_off_from_the_old_default_stays_off()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{"lastView":"Calendar","calendarPlansShown":false}""");

            var store = new ShellNavigationStore(path);

            Assert.False(store.CalendarPlansShown);
            Assert.Equal("Calendar", store.LastView);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The roadmap bands folded to one lane survive a restart. The key is written only
    /// while some band is folded, so a file from before folds were kept keeps its shape.
    /// </summary>
    [Fact]
    public void Folded_roadmap_bands_survive_a_restart_and_none_folded_writes_no_key()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            Assert.Empty(store.RoadmapCollapsedGroups);

            store.SetLastSurface("Tools");
            using (var none = JsonDocument.Parse(Written(path)))
            {
                Assert.False(none.RootElement.TryGetProperty("roadmapCollapsedGroups", out _));
            }

            store.SetRoadmapCollapsedGroups(["JSdotNet/Backlog", "docs"]);

            var restarted = new ShellNavigationStore(path);
            Assert.Equal(["JSdotNet/Backlog", "docs"], restarted.RoadmapCollapsedGroups);
            Assert.Equal("Tools", restarted.LastSurface);

            restarted.SetRoadmapCollapsedGroups([]);
            Assert.Empty(new ShellNavigationStore(path).RoadmapCollapsedGroups);
            using var cleared = JsonDocument.Parse(Written(path));
            Assert.False(cleared.RootElement.TryGetProperty("roadmapCollapsedGroups", out _));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void The_last_view_survives_a_restart_under_its_own_key()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            store.SetLastView("Roadmap");

            Assert.Equal("Roadmap", new ShellNavigationStore(path).LastView);
            using var document = JsonDocument.Parse(Written(path));
            Assert.Equal("Roadmap", document.RootElement.GetProperty("lastView").GetString());
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The Board's Columns choice survives a restart beside the view. The key is
    /// written only while it is not the default, so a file from before the Board
    /// keeps its shape.
    /// </summary>
    [Fact]
    public void The_board_columns_survive_a_restart_and_the_default_writes_no_key()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            Assert.Equal("Status", store.BoardColumns);

            store.SetLastView("Board");
            using (var none = JsonDocument.Parse(Written(path)))
            {
                Assert.False(none.RootElement.TryGetProperty("boardColumns", out _));
            }

            store.SetBoardColumns("Plan");

            var restarted = new ShellNavigationStore(path);
            Assert.Equal("Plan", restarted.BoardColumns);
            Assert.Equal("Board", restarted.LastView);

            restarted.SetBoardColumns("Status");
            Assert.Equal("Status", new ShellNavigationStore(path).BoardColumns);
            using var cleared = JsonDocument.Parse(Written(path));
            Assert.False(cleared.RootElement.TryGetProperty("boardColumns", out _));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The Calendar's Month | Week range survives a restart. Month is the default and
    /// writes no key, so a file from before the Week view keeps its shape.
    /// </summary>
    [Fact]
    public void The_calendar_range_survives_a_restart_and_month_writes_no_key()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            Assert.Equal("Month", store.CalendarRange);

            store.SetLastView("Calendar");
            using (var none = JsonDocument.Parse(Written(path)))
            {
                Assert.False(none.RootElement.TryGetProperty("calendarRange", out _));
            }

            store.SetCalendarRange("Week");
            using (var week = JsonDocument.Parse(Written(path)))
            {
                Assert.Equal("Week", week.RootElement.GetProperty("calendarRange").GetString());
            }

            var restarted = new ShellNavigationStore(path);
            Assert.Equal("Week", restarted.CalendarRange);
            Assert.Equal("Calendar", restarted.LastView);

            restarted.SetCalendarRange("Month");
            Assert.Equal("Month", new ShellNavigationStore(path).CalendarRange);
            using var cleared = JsonDocument.Parse(Written(path));
            Assert.False(cleared.RootElement.TryGetProperty("calendarRange", out _));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The header's repository scope survives a restart in the order it was chosen, so
    /// its anchor — the first alias — stays first. An empty scope is every repository
    /// and writes no key.
    /// </summary>
    [Fact]
    public void The_repository_scope_survives_a_restart_in_order_and_empty_writes_no_key()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            Assert.Empty(store.RepositoryScope);

            store.SetLastSurface("Tools");
            using (var none = JsonDocument.Parse(Written(path)))
            {
                Assert.False(none.RootElement.TryGetProperty("repositoryScope", out _));
            }

            store.SetRepositoryScope(["docs", "backlog"]);
            using (var scoped = JsonDocument.Parse(Written(path)))
            {
                Assert.Equal(
                    ["docs", "backlog"],
                    scoped.RootElement.GetProperty("repositoryScope").EnumerateArray().Select(e => e.GetString()));
            }

            var restarted = new ShellNavigationStore(path);
            Assert.Equal(["docs", "backlog"], restarted.RepositoryScope);

            restarted.SetRepositoryScope([]);
            Assert.Empty(new ShellNavigationStore(path).RepositoryScope);
            using var cleared = JsonDocument.Parse(Written(path));
            Assert.False(cleared.RootElement.TryGetProperty("repositoryScope", out _));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The Brain rides the scope as its reserved empty value, in its place, through a
    /// restart (local ADR 0027, §5): the store keeps it as written and never drops it.
    /// </summary>
    [Fact]
    public void The_brain_survives_a_restart_as_the_empty_value_in_its_place()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            store.SetRepositoryScope([BrainScope.Value, "backlog"]);

            using (var scoped = JsonDocument.Parse(Written(path)))
            {
                Assert.Equal(
                    ["", "backlog"],
                    scoped.RootElement.GetProperty("repositoryScope").EnumerateArray().Select(e => e.GetString()));
            }

            Assert.Equal([BrainScope.Value, "backlog"], new ShellNavigationStore(path).RepositoryScope);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The retired "No repo" chip's state, under the name it went by, reopens as the
    /// Brain alone, whatever scope sat beside it — and the next save writes the old key
    /// no more.
    /// </summary>
    [Fact]
    public void A_remembered_no_repository_only_state_migrates_to_the_brain()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "lastView": "Tasks",
                  "repositoryScope": ["backlog"],
                  "noRepositoryOnly": true
                }
                """);

            var store = new ShellNavigationStore(path);
            Assert.Equal([BrainScope.Value], store.RepositoryScope);

            store.SetLastView("Board");

            Assert.DoesNotContain("noRepositoryOnly", Written(path), StringComparison.Ordinal);
            Assert.Equal([BrainScope.Value], new ShellNavigationStore(path).RepositoryScope);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>The old flag turned off is no scope at all: the scope beside it stands.</summary>
    [Fact]
    public void A_remembered_no_repository_state_that_was_off_leaves_the_scope_alone()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{ "repositoryScope": ["backlog"], "noRepositoryOnly": false }""");

            Assert.Equal(["backlog"], new ShellNavigationStore(path).RepositoryScope);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// A file written before any of this release's keys reads as their defaults —
    /// Month, Needs you first on, finished today off, every repository — and the next
    /// save adds none of them.
    /// </summary>
    [Fact]
    public void A_file_without_the_new_keys_reads_as_their_defaults_and_is_saved_without_them()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "lastSurface": "Workspace",
                  "lastView": "Board",
                  "lastEnabledPanes": ["Devbook"],
                  "boardColumns": "Plan"
                }
                """);

            var store = new ShellNavigationStore(path);

            Assert.Equal("Month", store.CalendarRange);
            Assert.Empty(store.RepositoryScope);
            Assert.Equal("Plan", store.BoardColumns);

            store.SetLastView("Tasks");

            using var document = JsonDocument.Parse(Written(path));
            Assert.False(document.RootElement.TryGetProperty("calendarRange", out _));
            Assert.False(document.RootElement.TryGetProperty("repositoryScope", out _));
            Assert.Equal("Plan", document.RootElement.GetProperty("boardColumns").GetString());
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>A blank stored range is no range at all and reads as Month; a name the
    /// shell does not know is kept for the shell to read as Month.</summary>
    [Fact]
    public void A_blank_calendar_range_reads_as_month()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{"calendarRange":"  "}""");

            Assert.Equal("Month", new ShellNavigationStore(path).CalendarRange);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>Each new choice is announced when it changes and is silent when set to
    /// what it already is, so the shell is not woken by a write that changed nothing.</summary>
    [Fact]
    public void Setting_a_new_choice_to_its_current_value_is_a_no_op_and_changing_it_says_so()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            var changes = 0;
            store.Changed += () => changes++;

            store.SetCalendarRange("Month");
            store.SetRepositoryScope([]);
            Assert.Equal(0, changes);
            Assert.False(File.Exists(path));

            store.SetCalendarRange("Week");
            store.SetRepositoryScope(["backlog", "docs"]);
            Assert.Equal(2, changes);

            store.SetRepositoryScope(["backlog", "docs"]);
            Assert.Equal(2, changes);

            // The order is the anchor, so the same aliases in another order are a change.
            store.SetRepositoryScope(["docs", "backlog"]);
            Assert.Equal(3, changes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Setting_the_same_view_again_is_a_no_op()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            store.SetLastView("Roadmap");

            var changes = 0;
            store.Changed += () => changes++;
            store.SetLastView("Roadmap");

            Assert.Equal(0, changes);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// A file written before the view switch carries no <c>lastView</c>: the reader
    /// was looking at the panes, and the panes' main view was the task list.
    /// </summary>
    [Fact]
    public void A_file_without_a_last_view_reads_as_the_tasks_view()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "lastSurface": "Dashboard",
                  "lastEnabledPanes": ["Tasks", "Devbook"]
                }
                """);

            var store = new ShellNavigationStore(path);

            Assert.Equal("Tasks", store.LastView);
            Assert.Equal("Dashboard", store.LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The roadmap was a takeover before it was a view, and a file from then names it
    /// as the surface. It reopens as the Roadmap view over the workspace, and the next
    /// save writes the view — never the old surface name.
    /// </summary>
    [Fact]
    public void A_roadmap_surface_from_before_the_view_switch_reads_as_the_roadmap_view()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "lastSurface": "Roadmap",
                  "lastEnabledPanes": ["Inbox"]
                }
                """);

            var store = new ShellNavigationStore(path);

            Assert.Equal("Roadmap", store.LastView);
            Assert.Equal("Workspace", store.LastSurface);

            // The roadmap takeover covered the Inbox pane, so the Inbox was not on
            // screen: it does not become the surface, and leaves the pane list.
            store.SetBoardColumns("Plan");

            using var document = JsonDocument.Parse(Written(path));
            Assert.Equal("Workspace", document.RootElement.GetProperty("lastSurface").GetString());
            Assert.Equal("Roadmap", document.RootElement.GetProperty("lastView").GetString());
            Assert.False(document.RootElement.TryGetProperty("lastEnabledPanes", out _));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>Roadmap stays readable as a surface name and is never written as one:
    /// a caller that still hands it over is heard as the view.</summary>
    [Fact]
    public void Roadmap_handed_over_as_a_surface_is_remembered_as_the_view()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            store.SetLastSurface("Roadmap");

            Assert.Equal("Workspace", store.LastSurface);
            Assert.Equal("Roadmap", store.LastView);
            using var document = JsonDocument.Parse(Written(path));
            Assert.Equal("Workspace", document.RootElement.GetProperty("lastSurface").GetString());
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The Inbox was a side pane beside the main view before it took the whole
    /// screen. A file from then with it open over the workspace reopens on the Inbox
    /// surface, with the view underneath kept, and the pane list loses the name. A
    /// Devbook open beside it does not win the screen: from when the Inbox took the
    /// screen until the Devbook did, the Inbox surface covered the Devbook pane.
    /// </summary>
    [Theory]
    [InlineData("\"lastSurface\": \"Workspace\",")]
    [InlineData("")]
    public void An_inbox_pane_open_over_the_workspace_reads_as_the_inbox_surface(string surfaceLine)
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $$"""
                {
                  {{surfaceLine}}
                  "lastView": "Board",
                  "lastEnabledPanes": ["Inbox", "Devbook"]
                }
                """);

            var store = new ShellNavigationStore(path);

            Assert.Equal("Inbox", store.LastSurface);
            Assert.Equal("Board", store.LastView);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>A takeover stored over the Inbox pane was what the reader saw, so it
    /// stays the surface; the Inbox name is dropped and nothing else changes.</summary>
    [Fact]
    public void An_inbox_pane_under_another_surface_is_dropped_and_the_surface_kept()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "lastSurface": "Dashboard",
                  "lastEnabledPanes": ["Inbox"]
                }
                """);

            var store = new ShellNavigationStore(path);

            Assert.Equal("Dashboard", store.LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>The migration is what the next save writes: the Inbox as the surface,
    /// and never again among the panes.</summary>
    [Fact]
    public void A_migrated_inbox_pane_is_written_back_as_the_surface()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                { "lastSurface": "Workspace", "lastEnabledPanes": ["Inbox", "Devbook"] }
                """);

            var store = new ShellNavigationStore(path);
            store.SetLastView("Calendar");

            using var document = JsonDocument.Parse(Written(path));
            Assert.Equal("Inbox", document.RootElement.GetProperty("lastSurface").GetString());
            Assert.False(document.RootElement.TryGetProperty("lastEnabledPanes", out _));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void The_inbox_surface_survives_a_restart()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            store.SetLastSurface("Inbox");

            Assert.Equal("Inbox", new ShellNavigationStore(path).LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The Devbook was a side pane beside the main view before it took the whole
    /// screen (local ADR 0022, amendment of 2026-10-15), stored as <c>Devbook</c> or,
    /// before the context was renamed, <c>Knowledge</c>. A file from then with it
    /// open over the workspace reopens on the Devbook surface, the view underneath
    /// kept.
    /// </summary>
    [Theory]
    [InlineData("Devbook", "\"lastSurface\": \"Workspace\",")]
    [InlineData("Devbook", "")]
    [InlineData("Knowledge", "\"lastSurface\": \"Workspace\",")]
    [InlineData("Knowledge", "")]
    public void A_devbook_pane_open_over_the_workspace_reads_as_the_devbook_surface(string pane, string surfaceLine)
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $$"""
                {
                  {{surfaceLine}}
                  "lastView": "Board",
                  "lastEnabledPanes": ["{{pane}}"]
                }
                """);

            var store = new ShellNavigationStore(path);

            Assert.Equal("Devbook", store.LastSurface);
            Assert.Equal("Board", store.LastView);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>A takeover stored over the Devbook pane was what the reader saw, so it
    /// stays the surface — the roadmap takeover as the Roadmap view, as ever.</summary>
    [Theory]
    [InlineData("Devbook", "Dashboard", "Dashboard", "Calendar")]
    [InlineData("Knowledge", "Sessions", "Sessions", "Calendar")]
    [InlineData("Devbook", "Tools", "Tools", "Calendar")]
    [InlineData("Knowledge", "Roadmap", "Workspace", "Roadmap")]
    public void A_devbook_pane_under_another_surface_leaves_that_surface(
        string pane, string storedSurface, string surface, string view)
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $$"""
                {
                  "lastSurface": "{{storedSurface}}",
                  "lastView": "Calendar",
                  "lastEnabledPanes": ["{{pane}}"]
                }
                """);

            var store = new ShellNavigationStore(path);

            Assert.Equal(surface, store.LastSurface);
            Assert.Equal(view, store.LastView);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>The migration is what the next save writes: the Devbook as the
    /// surface, and no pane list at all — that key is never written again.</summary>
    [Fact]
    public void A_migrated_devbook_pane_is_written_back_as_the_surface_and_the_pane_list_never_again()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                { "lastSurface": "Workspace", "lastEnabledPanes": ["Tasks", "Knowledge"] }
                """);

            var store = new ShellNavigationStore(path);
            store.SetLastView("Calendar");

            using var document = JsonDocument.Parse(Written(path));
            Assert.Equal("Devbook", document.RootElement.GetProperty("lastSurface").GetString());
            Assert.False(document.RootElement.TryGetProperty("lastEnabledPanes", out _));

            store.SetLastSurface("Workspace");

            using var after = JsonDocument.Parse(Written(path));
            Assert.Equal("Workspace", after.RootElement.GetProperty("lastSurface").GetString());
            Assert.False(after.RootElement.TryGetProperty("lastEnabledPanes", out _));
            Assert.Equal("Workspace", new ShellNavigationStore(path).LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void The_devbook_surface_survives_a_restart()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);

            store.SetLastSurface("Devbook");

            Assert.Equal("Devbook", new ShellNavigationStore(path).LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The tools pane's tab and host filter survive a restart, and neither key is
    /// written until the pane has been on a tab or a filter, so an older file keeps
    /// its shape.
    /// </summary>
    [Fact]
    public void The_tools_tab_and_host_filter_survive_a_restart_and_write_no_key_until_chosen()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new ShellNavigationStore(path);
            Assert.Null(store.ToolsTab);
            Assert.Null(store.ToolsHostFilter);

            store.SetLastSurface("Tools");
            using (var none = JsonDocument.Parse(Written(path)))
            {
                Assert.False(none.RootElement.TryGetProperty("toolsTab", out _));
                Assert.False(none.RootElement.TryGetProperty("toolsHostFilter", out _));
            }

            store.SetToolsTab("checklist");
            store.SetToolsHostFilter("claude");

            var restarted = new ShellNavigationStore(path);
            Assert.Equal("checklist", restarted.ToolsTab);
            Assert.Equal("claude", restarted.ToolsHostFilter);
            Assert.Equal("Tools", restarted.LastSurface);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The setters are on the click path, so the file is written off it, and a
    /// burst of presses — the view options pressed in quick succession — still
    /// leaves the last one on disk, never an earlier one that finished later.
    /// </summary>
    [Fact]
    public void A_burst_of_choices_leaves_the_last_one_written()
    {
        var path = NewSettingsPath();
        try
        {
            var store = new ShellNavigationStore(path);
            string[] views = ["Board", "Calendar", "Roadmap", "InProgress", "Tasks", "Board", "Calendar"];

            foreach (var view in views)
            {
                store.SetLastView(view);
                Assert.Equal(view, store.LastView);
            }

            store.Flush();

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("Calendar", document.RootElement.GetProperty("lastView").GetString());
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>A shell that remounts — Settings and back — or a second store on
    /// the same file reads the choice just made, even while its write is still
    /// in the pool's hands.</summary>
    [Fact]
    public void A_store_opened_on_the_path_reads_the_choice_just_made()
    {
        var path = NewSettingsPath();
        try
        {
            var store = new ShellNavigationStore(path);

            for (var press = 0; press < 20; press++)
            {
                var surface = press % 2 == 0 ? "Sessions" : "Dashboard";
                store.SetLastSurface(surface);

                Assert.Equal(surface, new ShellNavigationStore(path).LastSurface);
            }
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>The file as written once every queued write has landed. The
    /// setters return before the write, so a test that reads the file rather than
    /// the properties waits for it first.</summary>
    internal static string Written(string path)
    {
        ShellNavigationStore.WaitForPendingWrites(path);
        return File.ReadAllText(path);
    }

    private static string NewSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "backlog-shell-navigation-tests", Guid.NewGuid().ToString("n"), "shell-navigation.json");

    private static void DeleteSettingsDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return;

        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }
}
