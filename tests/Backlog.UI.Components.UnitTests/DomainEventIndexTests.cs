using Backlog.UI.Components.Diagrams;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Events tab's readings out of the chapters as written: when an event fires,
/// who consumes it, its payload as rows, and what a context listens to from its
/// dependency tables.
/// </summary>
public sealed class DomainEventIndexTests
{
    [Fact]
    public void A_context_listens_to_the_published_languages_another_context_supplies()
    {
        const string dependencies = """
            ## Outbound dependencies

            | Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
            |---|---|---|---|---|
            | [Repository Management](../x.md) | Customer/Supplier (Tasks = customer) | Lookup by id | `x` | Resolves repos. |
            | [Sync](../sync/context.md) | OHS + Published Language (Sync = supplier) | The `TaskSyncWorker` pushes and pulls the task feed | `TaskChange` | A task reaches the other devices. |
            | [Productivity](../p.md) | OHS + Published Language (Tasks = supplier) | Publishes `AIWorkLogged` | `y` | Analysis. |

            ## Inbound dependents (known)

            | Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
            |---|---|---|---|---|
            | [Inbox](../inbox/domain.md) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` | `z` | Relies on Tasks creating a draft task. |
            """;

        var listens = DomainEventIndex.Listens(dependencies, "Tasks");
        var monitoring = DomainEventIndex.Listens(dependencies.Replace("Inbox = supplier", "Monitoring = supplier"), "Monitoring & Dashboard", "monitoring");

        Assert.Equal(["Sync", "Inbox"], listens.Select(l => l.Context));
        Assert.Null(listens[0].Name);
        Assert.Equal("The TaskSyncWorker pushes and pulls the task feed", listens[0].How);
        Assert.Equal("A task reaches the other devices.", listens[0].Why);
        Assert.Equal("ItemTriaged", listens[1].Name);
        Assert.Equal("Relies on Tasks creating a draft task.", listens[1].Why);

        // A table that shortens the context's own name is still its own.
        Assert.Equal(["Sync", "Tasks"], monitoring.Select(l => l.Context));
    }

    /// <summary>Only a transition of a lifecycle a unit draws says when an event
    /// fires: the prose naming the move, a flowchart's edge, a note and a legacy
    /// flow.md's lifecycle do not.</summary>
    [Fact]
    public void An_event_fires_on_the_moves_a_lifecycle_raises_it_on()
    {
        var domain = DomainUnits.Parse("""
            # Tasks

            ## Task

            ```meta
            type: aggregate
            ```

            **Task lifecycle**

            ```mermaid
            stateDiagram-v2
                state "In progress" as InProgress
                [*] --> Draft
                Draft --> Ready : refined
                Ready --> InProgress : started (TaskProjected, not yet emitted)
                InProgress --> Done : finished (TaskCompleted, not yet emitted)
                Done --> InProgress : reopened
                note right of Draft : StatusChanged on every move
            ```

            - The `Ready → InProgress` transition would emit `TaskProjected`.

            ## Occurrence Spawning

            ```meta
            type: domain-service
            ```

            ```mermaid
            flowchart TD
                A["ticked"] -->|OccurrenceSpawned| B["next occurrence"]
            ```

            Spawns the next occurrence.
            """, ".devbook/domain/tasks/domain.md");
        var legacy = DomainUnits.Parse("""
            # Flow

            ## Repeat

            ```mermaid
            stateDiagram-v2
                Waiting --> Spawned : ticked (OccurrenceSpawned)
                Spawned --> Waiting : again
            ```
            """, ".devbook/domain/tasks/flow.md");

        var moves = DomainEventIndex.Moves([domain, legacy]);

        Assert.Equal([new DomainEventMove("Ready", "In progress")], moves["TaskProjected"]);
        Assert.Equal([new DomainEventMove("In progress", "Done")], moves["TaskCompleted"]);
        Assert.False(moves.ContainsKey("StatusChanged"));
        // A legacy flow.md is a file like any other since devbook contract 37: its
        // chapters are no unit's, so its lifecycle raises nothing.
        Assert.False(moves.ContainsKey("OccurrenceSpawned"));
    }

    [Fact]
    public void A_wrapped_consumer_bullet_reads_whole_and_a_list_of_names_stays_one_name()
    {
        var contract = new DomainEventContract(
            "PlanChanged", "#planchanged", "draft", "Published when a plan changes.", [],
            null,
            "- [Monitoring & Dashboard](../monitoring/context.md) —\n  compares the plan with execution.\n- Planning, Reporting — read the totals.",
            null, DomainEventDelivery.Documented);

        var consumers = DomainEventIndex.Consumers(contract);

        Assert.Equal(["Monitoring & Dashboard", "Planning, Reporting"], consumers.Select(c => c.Name));
        Assert.Equal("compares the plan with execution", consumers[0].What);
        Assert.Equal("read the totals", consumers[1].What);
    }

    [Fact]
    public void An_event_reads_when_it_fires_who_consumes_it_and_its_payload_as_rows()
    {
        var contract = new DomainEventContract(
            "StatusChanged", "#statuschanged", "draft", "Published when a `Task` changes `Task Status`.", ["Task"],
            "- `task_id` - task identifier.\n- `new_status` - new `Task Status`.",
            "- Monitoring & Dashboard, which turns work-state changes into progress signals.",
            null, DomainEventDelivery.Documented);

        Assert.Equal("a Task changes Task Status", DomainEventIndex.When(contract));
        var consumer = Assert.Single(DomainEventIndex.Consumers(contract));
        Assert.Equal("Monitoring & Dashboard", consumer.Name);
        Assert.Equal("which turns work-state changes into progress signals", consumer.What);
        Assert.Equal(["task_id", "new_status"], DomainEventIndex.Payload(contract)!.Select(f => f.Name));
        Assert.Null(DomainEventIndex.Payload(contract with { Payload = "Free prose about the payload." }));

        // A bullet that wraps is still one row.
        var wrapped = DomainEventIndex.Payload(contract with { Payload = "- `projection_target` - the target type:\n  `issue` or `cli-task`.\n- `external_id` - the id." })!;
        Assert.Equal(["projection_target", "external_id"], wrapped.Select(f => f.Name));
        Assert.Equal("the target type: `issue` or `cli-task`.", wrapped[0].Meaning);
    }
}
