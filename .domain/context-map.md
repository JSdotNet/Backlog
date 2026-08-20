# Context Map: Backlog

```meta
status: draft
order: ["inbox", "capture", "backlog", "roadmap", "second-brain", "productivity", "environment", "repository-management", "dev-pc-management", "sessions", "monitoring", "technology-stack"]
```

> Strategic DDD view of the Backlog product domain: bounded-context roles,
> subdomain classification, and the relationships that carry work, knowledge,
> standards, and signals across the system.

> See also: `.arc42/03-context-and-scope.md` for the system-level context this
> map's bounded contexts sit inside, and `.arc42/05-building-block-view.md` for
> how these contexts are realized across the desktop, mobile, IDE, and cloud
> containers.

## Subdomain landscape

| Bounded context | Subdomain type | Why |
|---|---|---|
| Capture | Core | It is the front door for turning raw external input into normalized intent for the product. |
| Inbox | Core | It owns triage and the decision point where captured input becomes work, knowledge, deferral, or archive. |
| Backlog Management | Core | It owns the durable work model, prioritization, and multi-repository execution planning. |
| Roadmap Planning | Core | It owns the forward plan — what is intended, when, in what order — and is the only context that holds a dependency between two pieces of planned work. Sequencing intent across repositories is a judgement the product exists to support, not a report over work someone else owns. |
| Second Brain | Core | It owns durable knowledge linked to work and makes knowledge reusable across projects. |
| Productivity | Supporting | It turns AI-assisted work activity into personal productivity insight without owning the work items or execution tools. |
| Environment | Supporting | It provides quick access to named local, cloud, and project environments without becoming the authority for repository or health data. |
| Monitoring & Dashboard | Supporting | It observes the core flow and turns signals into visibility and follow-up actions rather than owning the work itself. |
| Technology Stack | Supporting | It supplies portfolio-wide standards, baselines, and deprecation policy to the core work contexts. |
| Repository Management | Supporting | It provides repository inventory, health, and dependency visibility used by the core work contexts. |
| Dev PC Management | Supporting | It provides machine inventory, compliance, and operator support capabilities for the work system. |
| Sessions | Supporting | It records what the AI coding agents have been doing on each environment, without owning the work, the machines, or the repositories those sessions touch. |

## Context map

```mermaid
flowchart LR
    Capture[Capture]
    Inbox[Inbox]
    Backlog[Backlog Management]
    Roadmap[Roadmap Planning]
    Brain[Second Brain]
    Productivity[Productivity]
    Environment[Environment]
    Monitor[Monitoring & Dashboard]
    Tech[Technology Stack]
    Repo[Repository Management]
    DevPC[Dev PC Management]
    Sessions[Sessions]

    Capture -->|OHS + Published Language<br/>ItemCaptured| Inbox
    Inbox -->|OHS + Published Language<br/>ItemTriaged| Backlog
    Inbox -->|OHS + Published Language<br/>ItemTriaged| Brain
    Backlog <-->|Partnership<br/>Cross-link by id| Brain
    Backlog <-->|Partnership<br/>Optional cross-link by id| Roadmap

    Roadmap -->|OHS + Published Language<br/>RoadmapItemScheduled| Monitor

    Backlog -->|OHS + Published Language<br/>StatusChanged / EntryProjected / EntryCompleted| Monitor
    Inbox -->|Customer/Supplier<br/>Queue-health feed| Monitor
    Repo -->|Customer/Supplier<br/>Health and scan feed| Monitor
    DevPC -->|OHS + Published Language<br/>MachineStatusChanged / ComplianceUpdated| Monitor
    Productivity -->|OHS + Published Language<br/>ProductivityRecorded| Monitor
    Environment -->|Customer/Supplier<br/>Environment availability feed| Monitor
    Monitor -->|OHS + Published Language<br/>FollowUpCaptured| Inbox

    Tech -->|Customer/Supplier<br/>Technology baselines| Repo
    Tech -->|Customer/Supplier<br/>Team tools baseline| DevPC
    Repo -->|Customer/Supplier<br/>Adoption and technology scans| Tech
    DevPC -->|Customer/Supplier<br/>Tool-version reports| Tech

    Backlog -->|OHS + Published Language<br/>AIWorkLogged| Productivity
    Monitor -->|OHS + Published Language<br/>CopilotSession signal| Productivity
    Productivity -->|Customer/Supplier<br/>Productivity summaries| Backlog

    Repo -->|Customer/Supplier<br/>Repository/environment registry lookup| Environment
    Environment -->|Customer/Supplier<br/>Launchable environment links| Backlog
    Environment -->|Customer/Supplier<br/>Environment shortcuts| DevPC

    %% Sessions. Dashed edges are named and not built: see the strategic rules
    %% below and .domain/sessions/dependencies.md.
    DevPC -.->|Customer/Supplier (not built)<br/>Machine identity for an environment| Sessions
    Sessions -.->|Customer/Supplier (not built)<br/>Agent session facts| Productivity
    Sessions -.->|Customer/Supplier (not built)<br/>Stalled-session observation| Monitor

    Repo -->|Customer/Supplier<br/>Repo registry lookup| Backlog
    Repo -->|Customer/Supplier<br/>Repo registry lookup by alias| Roadmap
```

## Published languages and contracts

| Contract owner | Published language / contract | Used by |
|---|---|---|
| Capture | `.domain/capture/domain.md#domain-event-itemcaptured` | Inbox |
| Inbox | `.domain/inbox/domain.md#domain-event-itemtriaged` | Backlog, Second Brain |
| Backlog Management | `.domain/backlog/domain.md#domain-event-statuschanged`, `.domain/backlog/domain.md#domain-event-entryprojected`, `.domain/backlog/domain.md#domain-event-entrycompleted` | Monitoring & Dashboard |
| Backlog Management | `.domain/backlog/domain.md#domain-event-aiworklogged` | Productivity |
| Roadmap Planning | `.domain/roadmap/domain.md#domain-event-roadmapitemscheduled` | Monitoring & Dashboard |
| Roadmap Planning | `.domain/roadmap/naming.md#term-roadmap-item` | Backlog Management |
| Monitoring & Dashboard | `.domain/monitoring/domain.md#domain-event-followupcaptured` | Inbox |
| Dev PC Management | `.domain/dev-pc-management/domain.md#domain-event-machinestatuschanged`, `.domain/dev-pc-management/domain.md#domain-event-complianceupdated` | Monitoring & Dashboard |
| Productivity | `.domain/productivity/domain.md#domain-event-productivityrecorded` | Monitoring & Dashboard |
| Environment | `.domain/environment/domain.md#domain-service-environment-shortcut-resolution` | Backlog Management, Dev PC Management |
| Technology Stack | `Technology Baseline` / `BaselineProvided` contract in `.domain/technology-stack/domain.md#domain-service-deprecation-management` | Dev PC Management, Repository Management |

## Strategic rules

- Only the owning context defines a published language. Consumers conform to that
  contract and do not reach into the supplier's internal aggregate shape.
- `Backlog Management` and `Roadmap Planning` split the word *priority* on
  purpose, and the split is load-bearing: Backlog owns an entry's **status and
  execution priority**, Roadmap owns **planning priority and sequence**. Neither
  writes the other's value, and neither derives its own from the other. A roadmap
  item may name the entry that executes it, but a plan must be able to hold work
  that has no entry yet — which is why the plan is stored rather than projected.
  This supersedes the earlier reading, in which the roadmap was a view over
  Backlog Entries.
- `Backlog Management` and `Second Brain` are a deliberate `Partnership`: both
  sides keep only foreign ids and the link semantics are coordinated through the
  Cross-Linking service rather than a shared aggregate.
- `Technology Stack` is the standards supplier for both `Repository Management`
  and `Dev PC Management`; those contexts may cache or copy baselines locally,
  but they do not become the authority for baseline meaning.
- `Monitoring & Dashboard` is an observer/read context. Its only write-back into
  the core flow is the published `FollowUpCaptured` contract into `Inbox`.
- `Productivity` measures personal and AI-assisted work from published activity
  signals. It never changes backlog status, Copilot sessions, or repository state.
- `Environment` owns the user's launchable environment shortcuts and access
  preferences. It resolves repository and health facts through suppliers instead
  of duplicating Repository Management or Monitoring data.
- `Sessions` is the single owner of the agent-session record. `Dev PC Management`
  modelled Copilot sessions on the `Machine` while Copilot was the only agent the
  machines ran; those chapters were removed rather than left as a second model of
  the same subject, and `Monitoring`'s own `copilot_session` signal kind is
  superseded by this context the moment the two are wired together.
- `Sessions` publishes no contract yet, which is why all three of its edges are
  dashed and why it appears in no row of the published-language table above. Its
  only consumer today is its own surface; the day `Productivity` or `Monitoring`
  consumes it, the event belongs in `.domain/sessions/domain.md` and in that table
  in the same change.
- A `Sessions` **Environment** is not an `Environment`-context Environment, and not
  a `Dev PC Management` **Machine**. It is wherever an agent ran. The two words
  collide across three contexts and the concepts do not; each context defines its own
  in its `naming.md`, and no aggregate holds another context's identity for it.