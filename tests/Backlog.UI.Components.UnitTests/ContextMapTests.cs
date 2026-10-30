using Backlog.UI.Components.Diagrams;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Domain view's Context map page (local ADR 0044): the contexts of the map's
/// Mermaid fence classified by its Subdomain landscape table, one line per pair of
/// contexts worded by the source context's dependency row, laid out on a ring that
/// crosses as few lines as it can, and drawn as Map and List with no pattern name.
/// </summary>
public sealed class ContextMapTests
{
    internal const string Map = """
        # Backlog

        ```meta
        type: context-map
        status: draft
        ```

        ## Subdomain landscape

        | Bounded context | Subdomain type | Why |
        |---|---|---|
        | Inbox | Core | It owns triage. |
        | Tasks | Core | It owns the durable work model. |
        | Roadmap Planning | Core | It owns the forward plan. |
        | Sessions | Supporting | It records what the agents did. |
        | Dev PC Management | Supporting | It keeps machine inventory. |

        ## Context map

        ```mermaid
        flowchart LR
            Inbox[Inbox]
            Tasks[Tasks]
            Roadmap[Roadmap Planning]
            Sessions[Sessions]
            DevPC[Dev PC Management]

            %% A comment naming Tasks --> Sessions is not an edge.
            Inbox -->|OHS + Published Language<br/>ItemTriaged| Tasks
            Tasks <-->|Partnership<br/>Optional cross-link by id| Roadmap
            Roadmap -->|Customer/Supplier<br/>Roadmap tag vocabulary| Tasks
            Sessions -->|Customer/Supplier<br/>Linked session state| Tasks
            DevPC -.->|"Customer/Supplier (not built)<br/>Machine identity"| Sessions
        ```

        ## Published languages and contracts

        | Contract owner | Published language / contract | Used by |
        |---|---|---|
        | Inbox | `.devbook/domain/inbox/domain.md#itemtriaged` | Tasks |

        ## Strategic rules

        - Only the owning context defines a published language.
        """;

    private const string InboxDependencies = """
        # Inbox

        ## Outbound dependencies

        | Depends on (context/module) | DDD pattern | Why |
        |---|---|---|
        | [Tasks](../tasks/domain.md#task) | OHS + Published Language (Inbox = supplier) | A triaged item becomes a draft task. The adapter writes the entry text. |
        """;

    private const string TasksDependencies = """
        # Tasks

        ## Outbound dependencies

        | Depends on (context/module) | DDD pattern | Why |
        |---|---|---|
        | [Roadmap Planning](../roadmap/domain.md#roadmap-item) | Customer/Supplier (Tasks = customer) | The tag picker offers every plan's tag. |
        | [Sessions](../sessions/domain.md#session-state) | Customer/Supplier (Tasks = customer) | A task's work badge colours a linked session by its state. |

        ## Inbound dependents (known)

        | Consumer (context/module) | DDD pattern | What it relies on |
        |---|---|---|
        | [Roadmap Planning](../roadmap/domain.md#roadmap-plan) | Partnership | Relies on a task id staying resolvable. |
        """;

    internal static readonly ContextMapSource[] Sources =
    [
        new("inbox", "Inbox", InboxDependencies, ".devbook/domain/inbox/dependencies.md"),
        new("tasks", "Tasks", TasksDependencies, ".devbook/domain/tasks/dependencies.md"),
        new("roadmap", "Roadmap", null, ".devbook/domain/roadmap/context.md"),
        new("sessions", "Sessions", null, ".devbook/domain/sessions/context.md"),
        new("dev-pc-management", "Dev PC Management", null, ".devbook/domain/dev-pc-management/context.md"),
    ];

    [Fact]
    public void Every_context_is_read_in_reading_order_with_its_type_why_and_folder()
    {
        var map = ContextMaps.Read(Map, Sources);

        Assert.Equal(["Inbox", "Tasks", "Roadmap", "Sessions", "DevPC"], map.Contexts.Select(c => c.Id));
        Assert.Equal(["core", "core", "core", "supporting", "supporting"], map.Contexts.Select(c => c.Kind));
        Assert.Equal("Roadmap Planning", map.Context("Roadmap")!.Name);
        Assert.Equal("roadmap", map.Context("Roadmap")!.Slug);
        Assert.Equal("dev-pc-management", map.Context("DevPC")!.Slug);
        Assert.Equal("It owns the forward plan.", map.Context("Roadmap")!.Why);
    }

    [Fact]
    public void One_line_per_pair_with_two_heads_when_it_points_both_ways_and_dashed_when_not_built()
    {
        var map = ContextMaps.Read(Map, Sources);

        Assert.Equal(4, map.Lines.Count); // Inbox–Tasks, Tasks–Roadmap, Sessions–Tasks, DevPC–Sessions
        var roadmap = map.Lines.Single(l => l.Touches("Roadmap"));
        Assert.True(roadmap.BothWays);
        Assert.True(roadmap.Built);

        var inbox = map.Lines.Single(l => l.Touches("Inbox"));
        Assert.False(inbox.BothWays);
        Assert.Equal(("Inbox", "Tasks"), (inbox.From, inbox.To));

        var devPc = map.Lines.Single(l => l.Touches("DevPC"));
        Assert.False(devPc.Built);

        Assert.True(map.IsCoreLine(inbox));
        Assert.False(map.IsCoreLine(devPc));
        Assert.Equal(3, map.LinesTouching("Tasks"));
    }

    [Fact]
    public void A_lines_wording_is_the_source_contexts_why_cell_and_never_its_pattern()
    {
        var map = ContextMaps.Read(Map, Sources);

        var inbox = map.Lines.Single(l => l.Touches("Inbox"));
        Assert.Equal("A triaged item becomes a draft task. The adapter writes the entry text.", inbox.Wording);
        Assert.Equal("Inbox to Tasks: A triaged item becomes a draft task. The adapter writes the entry text.", map.Describe(inbox));

        // Sessions keeps no table: the other side's row names it.
        var sessions = map.Lines.Single(l => l.Touches("Sessions") && l.Touches("Tasks"));
        Assert.Equal("A task's work badge colours a linked session by its state.", sessions.Wording);

        // No row names the pair: the line still says who meets whom.
        var devPc = map.Lines.Single(l => l.Touches("DevPC"));
        Assert.Equal("Dev PC Management to Sessions (not built yet)", map.Describe(devPc));

        foreach (var line in map.Lines)
        {
            Assert.DoesNotContain("Customer/Supplier", map.Describe(line), StringComparison.Ordinal);
            Assert.DoesNotContain("OHS", map.Describe(line), StringComparison.Ordinal);
            Assert.DoesNotContain("Partnership", map.Describe(line), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_published_languages_and_rules_are_kept_as_written()
    {
        var map = ContextMaps.Read(Map, Sources);

        Assert.StartsWith("| Contract owner |", map.Languages, StringComparison.Ordinal);
        Assert.Equal("- Only the owning context defines a published language.", map.Rules);
    }

    private const string Contracts = """
        # Backlog

        ## Context map

        ```mermaid
        flowchart LR
            Capture -->|OHS + Published Language<br/>ItemCaptured| Inbox
            Tasks -->|OHS<br/>StatusChanged / TaskProjected| Monitor
        ```

        ## Published languages and contracts

        | Contract owner | Published language / contract | Used by |
        |---|---|---|
        | Capture | `.devbook/domain/capture/domain.md#itemcaptured` | Inbox |
        | Tasks | `.devbook/domain/tasks/domain.md#statuschanged`, `.devbook/domain/tasks/domain.md#taskprojected` | Monitoring & Dashboard |
        | Roadmap Planning | `.devbook/domain/roadmap/domain.md#roadmap-item` | Tasks |
        | Technology Stack | `Technology Baseline` / `BaselineProvided` contract in `.devbook/domain/technology-stack/domain.md#deprecation-management` | Dev PC Management |
        | Sync | The replica protocol: the routes and DTOs in `Backlog.Modules.Sync.Abstractions` (`SyncRoutes`), under `.devbook/domain/sync/domain.md#change-precedence` | Tasks, Inbox |

        ## Strategic rules

        - Only the owning context defines a published language. Consumers conform to that
          contract.
        - `Tasks` and `Devbook` are a deliberate `Partnership`: both sides keep only foreign ids.
        - A capture reaches the Inbox in one of two ways:
          - **From another device**, on the sync replica.
          - **From a source monitor**, in-process.

          Both are keyed by the same id, so idempotency belongs to the port.
        - `Sync` carries other contexts' documents (see [Owner](sync/domain.md#owner)). It reads no payload field.
        """;

    [Fact]
    public void The_map_is_titled_by_its_heading()
    {
        Assert.Equal("Backlog", ContextMaps.Read(Map, Sources).Title);
        Assert.Equal(string.Empty, ContextMaps.Read("## Context map\n", []).Title);
    }

    [Fact]
    public void Each_contract_is_named_short_by_its_event_or_term_and_never_by_its_path()
    {
        var map = ContextMaps.Read(Contracts, []);

        Assert.Equal(["Capture", "Tasks", "Roadmap Planning", "Technology Stack", "Sync"], map.Contracts.Select(c => c.Owner));
        Assert.Equal(["Inbox", "Monitoring & Dashboard", "Tasks", "Dev PC Management", "Tasks, Inbox"], map.Contracts.Select(c => c.UsedBy));

        // The anchor's casing comes from where the map writes the name: its fence.
        var capture = Assert.Single(map.Contracts[0].Names);
        Assert.Equal(new ContextMapContractName("ItemCaptured", true, ".devbook/domain/capture/domain.md#itemcaptured"), capture);
        Assert.Equal(["StatusChanged", "TaskProjected"], map.Contracts[1].Names.Select(n => n.Name));
        Assert.All(map.Contracts[1].Names, n => Assert.True(n.IsCode));

        // An anchor the map never spells is read back as words, and is a term.
        Assert.Equal(new ContextMapContractName("Roadmap Item", false, ".devbook/domain/roadmap/domain.md#roadmap-item"), Assert.Single(map.Contracts[2].Names));

        // Named code spans win over the path they are in.
        Assert.Equal([("Technology Baseline", false), ("BaselineProvided", true)], map.Contracts[3].Names.Select(n => (n.Name, n.IsCode)));
        Assert.All(map.Contracts[3].Names, n => Assert.Equal(".devbook/domain/technology-stack/domain.md#deprecation-management", n.Reference));

        // A cell written as prose is named by its words before the colon.
        Assert.Equal(new ContextMapContractName("The replica protocol", false, ".devbook/domain/sync/domain.md#change-precedence"), Assert.Single(map.Contracts[4].Names));

        Assert.DoesNotContain(map.Contracts.SelectMany(c => c.Names), n => n.Name.Contains(".devbook", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_strategic_rule_is_a_card_titled_by_its_first_sentence()
    {
        var rules = ContextMaps.Read(Contracts, []).RuleCards;

        Assert.Equal(4, rules.Count);
        Assert.Equal("Only the owning context defines a published language", rules[0].Title);
        Assert.Equal("Consumers conform to that\ncontract.", rules[0].Body);

        // A rule of one sentence is all title.
        Assert.Equal("`Tasks` and `Devbook` are a deliberate `Partnership`: both sides keep only foreign ids", rules[1].Title);
        Assert.Equal(string.Empty, rules[1].Body);

        // A colon closing the line titles the list beneath it, and a paragraph
        // after the list stays with its rule.
        Assert.Equal("A capture reaches the Inbox in one of two ways", rules[2].Title);
        Assert.Equal("- **From another device**, on the sync replica.\n- **From a source monitor**, in-process.\n\nBoth are keyed by the same id, so idempotency belongs to the port.", rules[2].Body);

        // A full stop inside a link or brackets does not end the sentence.
        Assert.Equal("`Sync` carries other contexts' documents (see [Owner](sync/domain.md#owner))", rules[3].Title);
        Assert.Equal("It reads no payload field.", rules[3].Body);
    }

    [Fact]
    public void A_sentence_runs_on_past_an_abbreviation_and_keeps_the_bold_it_ends_in()
    {
        var map = ContextMaps.Read("""
            ## Strategic rules

            - A rule names a context, e.g. the Inbox, by its id. The rest follows.
            - **Only the owner writes.** Everyone else reads.
            """, []);

        Assert.Equal([("A rule names a context, e.g. the Inbox, by its id", "The rest follows."), ("**Only the owner writes**", "Everyone else reads.")],
            map.RuleCards.Select(r => (r.Title, r.Body)));
    }

    [Fact]
    public void A_chapter_list_cannot_read_as_cards_or_a_table_is_kept_as_written()
    {
        var map = ContextMaps.Read("""
            ## Published languages and contracts

            Every contract is versioned.

            | Contract owner | Published language / contract | Used by |
            |---|---|---|
            | Inbox | Events in `.devbook/domain/inbox/domain.md#itemtriaged` | Tasks |

            ## Strategic rules

            These hold everywhere:

            - One rule.
            """, []);

        Assert.Empty(map.Contracts);
        Assert.Empty(map.RuleCards);
        Assert.NotEmpty(map.Languages);
        Assert.NotEmpty(map.Rules);
    }

    [Fact]
    public void A_prose_contract_never_shows_its_path_and_an_acronym_is_not_code()
    {
        var map = ContextMaps.Read("""
            ## Published languages and contracts

            | Contract owner | Published language / contract | Used by |
            |---|---|---|
            | Inbox | Events in `.devbook/domain/inbox/domain.md#itemtriaged` | Tasks |
            | Tasks | `OHS` | Inbox |
            """, []);

        Assert.Equal(new ContextMapContractName("Events in", false, ".devbook/domain/inbox/domain.md#itemtriaged"), Assert.Single(map.Contracts[0].Names));
        Assert.False(Assert.Single(map.Contracts[1].Names).IsCode);
    }

    [Fact]
    public void List_heads_with_the_title_and_shows_contracts_and_rules_as_the_board_does()
    {
        using var context = Context();
        DevbookReference? followed = null;

        var view = context.Render<ContextMapView>(p => p
            .Add(v => v.Map, ContextMaps.Read(Contracts, []))
            .Add(v => v.OnNavigate, reference => followed = reference));

        Assert.Equal("Backlog", view.Find("[data-testid='context-map-page-title']").TextContent);
        Assert.StartsWith("4 bounded contexts and how they rely on each other.", view.Find("[data-testid='context-map-page-intro']").TextContent, StringComparison.Ordinal);

        view.Find("[data-testid='context-map-page-tab-list']").Click();
        Assert.StartsWith("4 bounded contexts and 2 relationships.", view.Find("[data-testid='context-map-page-intro']").TextContent, StringComparison.Ordinal);

        var languages = view.Find("[data-testid='context-map-list-languages']");
        Assert.Equal("Published languages and contracts", languages.QuerySelector("h3")!.TextContent);
        Assert.Equal(["Contract owner", "Published language / contract", "Used by"], languages.QuerySelectorAll("th").Select(th => th.TextContent));
        Assert.DoesNotContain(".devbook/", languages.TextContent, StringComparison.Ordinal);
        Assert.Equal(["ItemCaptured", "StatusChanged", "TaskProjected", "BaselineProvided"], languages.QuerySelectorAll("code").Select(c => c.TextContent));

        languages.QuerySelectorAll("[data-testid='context-map-contract']").First(b => b.TextContent == "ItemCaptured").Click();
        Assert.Equal(".devbook/domain/capture/domain.md", followed?.Path);
        Assert.Equal("itemcaptured", followed?.Slug);

        var rules = view.FindAll("[data-testid='context-map-rule']");
        Assert.Equal(4, rules.Count);
        Assert.Equal("Only the owning context defines a published language", rules[0].QuerySelector("[data-testid='context-map-rule-title']")!.TextContent.Trim());
        Assert.Contains("Consumers conform", rules[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ring_order_removes_every_crossing_a_planar_map_allows()
    {
        // A ring 0-1-2-3-4-5 declared in a scrambled order, plus two chords that
        // only cross when the ring is drawn out of order.
        var pairs = new List<(int, int)> { (0, 3), (3, 1), (1, 4), (4, 2), (2, 5), (5, 0), (0, 1), (0, 4) };

        var order = ContextMapLayout.Order(6, pairs);
        var slots = new int[6];
        for (var slot = 0; slot < order.Length; slot++) slots[order[slot]] = slot;

        Assert.Equal(0, ContextMapLayout.CountCrossings(slots, pairs));
        Assert.True(ContextMapLayout.CountCrossings([0, 1, 2, 3, 4, 5], pairs) > 0);
    }

    [Fact]
    public void The_repositorys_own_map_lays_out_with_fewer_crossings_than_it_is_declared_in()
    {
        var markdown = File.ReadAllText(RepositoryRoot.File(".devbook", "domain", "context-map.md"));
        var map = ContextMaps.Read(markdown, []);

        var layout = ContextMapLayout.Arrange(map);

        Assert.Equal(15, layout.Cards.Count);
        Assert.Equal(map.Lines.Count, layout.Strokes.Count);
        var ids = map.Contexts.Select(c => c.Id).ToList();
        var pairs = map.Lines.Select(l => (ids.IndexOf(l.From), ids.IndexOf(l.To))).ToList();
        Assert.True(layout.Crossings < ContextMapLayout.CountCrossings([.. Enumerable.Range(0, ids.Count)], pairs));

        // Every card sits inside the map, with every line ending outside both its cards.
        foreach (var card in layout.Cards)
        {
            Assert.InRange(card.X - layout.CardWidth / 2, 0, layout.Width);
            Assert.InRange(card.Y + layout.CardHeight / 2, 0, layout.Height);
        }

        // No line runs beneath a card it does not join.
        Assert.Empty(ContextMapLayout.Hidden(layout));

        // Every contract reads by its short name, and every rule is a card.
        Assert.Equal(
            ["ItemCaptured", "ItemTriaged", "StatusChanged", "TaskProjected", "TaskCompleted", "AIWorkLogged", "RoadmapItemScheduled",
             "Roadmap Item", "Roadmap Tag", "FollowUpCaptured", "MachineStatusChanged", "ComplianceUpdated", "ProductivityRecorded",
             "Environment Shortcut Resolution", "Technology Baseline", "BaselineProvided",
             "INotifications", "INotificationAnswerHandler", "NotificationDraft", "Backlog.Modules.Notifications.Abstractions", "The replica protocol"],
            map.Contracts.SelectMany(c => c.Names).Select(n => n.Name));
        Assert.Equal("Backlog", map.Title);
        Assert.True(map.RuleCards.Count >= 6);
        Assert.All(map.RuleCards, rule => Assert.InRange(rule.Title.Length, 1, 400));
    }

    [Fact]
    public void Map_draws_a_card_per_context_and_a_line_per_pair_in_plain_words()
    {
        using var context = Context();

        var view = context.Render<ContextMapDiagram>(p => p
            .Add(v => v.Map, ContextMaps.Read(Map, Sources))
            .Add(v => v.OnOpen, _ => { }));

        var cards = view.FindAll("[data-testid='context-map-card']");
        Assert.Equal(5, cards.Count);
        var tasks = view.Find("[data-testid='context-map-card'][data-context='Tasks']");
        Assert.Equal("core", tasks.QuerySelector("[data-testid='context-map-card-kind']")!.TextContent);
        Assert.Equal("3", tasks.QuerySelector("[data-testid='context-map-card-lines']")!.TextContent);
        Assert.Equal("Tasks, core, 3 lines", tasks.GetAttribute("aria-label"));
        Assert.Equal("1", view.Find("[data-testid='context-map-card'][data-context='DevPC'] [data-testid='context-map-card-lines']").TextContent);
        Assert.Equal("Dev PC Management, supporting, 1 line", view.Find("[data-testid='context-map-card'][data-context='DevPC']").GetAttribute("aria-label"));

        var lines = view.FindAll("[data-testid='context-map-line']");
        Assert.Equal(4, lines.Count);
        var roadmap = view.Find("[data-testid='context-map-line'][data-from='Tasks'][data-to='Roadmap']");
        Assert.NotNull(roadmap.QuerySelector(".context-map__stroke")!.GetAttribute("marker-start"));
        Assert.Contains("context-map__line--core", roadmap.ClassName);
        var devPc = view.Find("[data-testid='context-map-line'][data-from='DevPC']");
        Assert.Contains("context-map__line--not-built", devPc.ClassName);
        Assert.DoesNotContain("context-map__line--core", devPc.ClassName);
        Assert.Null(devPc.QuerySelector(".context-map__stroke")!.GetAttribute("marker-start"));

        var inbox = view.Find("[data-testid='context-map-line'][data-from='Inbox']");
        Assert.Equal("Inbox to Tasks: A triaged item becomes a draft task. The adapter writes the entry text.", inbox.GetAttribute("aria-label"));
        Assert.Equal(inbox.GetAttribute("aria-label"), inbox.QuerySelector("title")!.TextContent);

        foreach (var pattern in new[] { "Customer/Supplier", "OHS", "Partnership", "Published Language" })
        {
            Assert.DoesNotContain(pattern, view.Find("[data-testid='context-map-canvas']").InnerHtml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pointing_at_or_focusing_a_card_fades_every_line_that_does_not_touch_it()
    {
        using var context = Context();

        var view = context.Render<ContextMapDiagram>(p => p.Add(v => v.Map, ContextMaps.Read(Map, Sources)));

        view.Find("[data-testid='context-map-card'][data-context='Inbox']").MouseEnter();
        Assert.Equal(["context-map__line--lit", "context-map__line--faded", "context-map__line--faded", "context-map__line--faded"],
            Marks(view));

        view.Find("[data-testid='context-map-card'][data-context='Inbox']").MouseLeave();
        Assert.DoesNotContain(view.FindAll("[data-testid='context-map-line']"), line => line.ClassName!.Contains("--faded", StringComparison.Ordinal));

        view.Find("[data-testid='context-map-card'][data-context='Sessions']").Focus();
        var faded = view.FindAll("[data-testid='context-map-line']").Where(line => line.ClassName!.Contains("--faded", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, faded.Count);
        Assert.All(faded, line => Assert.NotEqual("Sessions", line.GetAttribute("data-from")));

        view.Find("[data-testid='context-map-card'][data-context='Sessions']").Blur();
        Assert.Null(view.Find("[data-testid='context-map-canvas']").GetAttribute("data-active"));

        static List<string> Marks(IRenderedComponent<ContextMapDiagram> view) =>
        [
            .. view.FindAll("[data-testid='context-map-line']")
                .OrderBy(line => line.GetAttribute("data-from") == "Inbox" ? 0 : 1)
                .Select(line => line.ClassName!.Split(' ').Single(c => c.EndsWith("--lit", StringComparison.Ordinal) || c.EndsWith("--faded", StringComparison.Ordinal)))
        ];
    }

    [Fact]
    public void Picking_a_card_hands_back_its_context_and_a_card_with_no_folder_is_disabled()
    {
        using var context = Context();
        ContextMapContext? opened = null;
        var sources = Sources.Where(s => s.Slug != "dev-pc-management").ToArray();

        var view = context.Render<ContextMapDiagram>(p => p
            .Add(v => v.Map, ContextMaps.Read(Map, sources))
            .Add(v => v.OnOpen, picked => opened = picked));

        view.Find("[data-testid='context-map-card'][data-context='Roadmap']").Click();

        Assert.Equal("roadmap", opened?.Slug);
        Assert.Equal("true", view.Find("[data-testid='context-map-card'][data-context='DevPC']").GetAttribute("aria-disabled"));
        view.Find("[data-testid='context-map-card'][data-context='DevPC']").Click();
        Assert.Equal("roadmap", opened?.Slug);
    }

    [Fact]
    public void List_names_the_core_then_the_supporting_contexts_with_their_why_then_the_languages_and_rules()
    {
        using var context = Context();

        var view = context.Render<ContextMapView>(p => p.Add(v => v.Map, ContextMaps.Read(Map, Sources)));

        Assert.Equal(["Map", "List"], view.FindAll("[role='tab']").Select(tab => tab.TextContent.Trim()));
        Assert.Equal("true", view.Find("[data-testid='context-map-page-tab-map']").GetAttribute("aria-selected"));

        view.Find("[data-testid='context-map-page-tab-list']").Click();

        var groups = view.FindAll("[data-testid='context-map-list-group']");
        Assert.Equal(["core", "supporting"], groups.Select(g => g.GetAttribute("data-kind")));
        Assert.Equal(["Inbox", "Tasks", "Roadmap"], groups[0].QuerySelectorAll("[data-testid='context-map-list-card']").Select(c => c.GetAttribute("data-context")));
        Assert.Equal(["Sessions", "DevPC"], groups[1].QuerySelectorAll("[data-testid='context-map-list-card']").Select(c => c.GetAttribute("data-context")));
        Assert.Equal("It owns triage.", groups[0].QuerySelector("[data-testid='context-map-list-why']")!.TextContent);
        Assert.Equal("ItemTriaged", view.Find("[data-testid='context-map-list-languages'] code").TextContent);
        Assert.Equal("Core · 3", groups[0].QuerySelector(".context-map-list__kind-name")!.TextContent);
        Assert.Equal("1 relationship", groups[0].QuerySelector(".context-map-list__count")!.TextContent.Trim());
        Assert.Contains("Only the owning context defines a published language", view.Find("[data-testid='context-map-list-rules']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fence_is_read_in_the_other_ways_mermaid_writes_an_edge()
    {
        const string map = """
            ## Context map

            ```mermaid
            flowchart LR
                Repo[Repo (legacy)]:::old
                Repo -- Health feed --> Monitor((Monitoring))
                Monitor ---> Inbox([Inbox])
                Inbox -. Follow-ups .-> Repo
                Inbox ==> Tasks
                Tasks --- Inbox
            ```
            """;

        var read = ContextMaps.Read(map, []);

        Assert.Equal(["Repo", "Monitor", "Inbox", "Tasks"], read.Contexts.Select(c => c.Id));
        Assert.Equal(["Repo (legacy)", "Monitoring", "Inbox", "Tasks"], read.Contexts.Select(c => c.Name));
        Assert.Equal(4, read.Lines.Count);
        Assert.False(read.Lines.Single(l => l.Touches("Repo") && l.Touches("Inbox")).Built);
        Assert.DoesNotContain(read.Lines, l => l.BothWays);
    }

    [Fact]
    public void A_map_with_more_contexts_than_the_default_ring_holds_grows_the_ring_and_keeps_the_cards()
    {
        var nodes = string.Join("\n", Enumerable.Range(1, 24).Select(i => $"    C{i}[Context {i}]"));
        var edges = string.Join("\n", Enumerable.Range(1, 23).Select(i => $"    C{i} --> C{i + 1}"));
        var map = ContextMaps.Read($"## Context map\n\n```mermaid\nflowchart LR\n{nodes}\n{edges}\n```\n", []);

        var layout = ContextMapLayout.Arrange(map);

        Assert.True(layout.Width > ContextMapLayout.DefaultWidth);
        Assert.Equal(ContextMapLayout.DefaultCardWidth, layout.CardWidth);
        var cards = layout.Cards.ToList();
        for (var i = 0; i < cards.Count; i++)
        {
            var (a, b) = (cards[i], cards[(i + 1) % cards.Count]);
            Assert.True(Math.Abs(a.X - b.X) >= layout.CardWidth || Math.Abs(a.Y - b.Y) >= layout.CardHeight, $"{a.Context.Id} touches {b.Context.Id}");
        }
    }

    [Fact]
    public void A_focused_card_keeps_its_lines_lit_while_the_pointer_passes_another()
    {
        using var context = Context();

        var view = context.Render<ContextMapDiagram>(p => p.Add(v => v.Map, ContextMaps.Read(Map, Sources)));

        view.Find("[data-testid='context-map-card'][data-context='DevPC']").Focus();
        view.Find("[data-testid='context-map-card'][data-context='Inbox']").MouseEnter();
        Assert.Equal("Inbox", view.Find("[data-testid='context-map-canvas']").GetAttribute("data-active"));

        view.Find("[data-testid='context-map-card'][data-context='Inbox']").MouseLeave();
        Assert.Equal("DevPC", view.Find("[data-testid='context-map-canvas']").GetAttribute("data-active"));
    }

    [Fact]
    public void A_map_with_no_contexts_says_so()
    {
        using var context = Context();

        var view = context.Render<ContextMapDiagram>();

        Assert.Single(view.FindAll("[data-testid='context-map-empty']"));
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
