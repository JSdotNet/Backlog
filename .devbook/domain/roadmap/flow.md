# Roadmap Planning

```meta
type: flow
status: draft
related: [.devbook/domain/roadmap/domain.md#roadmap-plan, .devbook/domain/roadmap/domain.md#plan-sequencing]
```

> Lifecycle and process flows for this bounded context: how a plan changes and
> how a proposed change is accepted or refused. Complementary to `model.md`
> (structure) and `domain.md` (responsibilities/invariants).

## Rescheduling flow

A reschedule starts as a *proposal*, not a change. The view that draws the plan
never edits it. It reports where something was dropped, the plan decides whether
that placement stands, and the view redraws from what it gets back. Nothing is
persisted until the plan has accepted it.

```mermaid
sequenceDiagram
    participant Reader
    participant View as Timeline view
    participant Plan as Roadmap Plan
    participant Store as Plan storage
    participant Monitor as Monitoring

    Reader->>View: drag a span, or move it with the keyboard
    View->>View: snap to the week, preview at the target placement
    Reader->>View: drop
    View->>Plan: propose PlannedWindow (and lane, if it moved)
    Plan->>Plan: validate the window (end >= start, both inclusive)
    alt window is valid
        Plan->>Store: persist the whole plan atomically
        Plan-->>Monitor: RoadmapItemScheduled (with the previous window)
        Plan-->>View: accepted plan
        View->>Reader: redraw from the accepted plan
    else window is not valid
        Plan-->>View: rejected, with the reason
        View->>Reader: put it back, and say why
    end
```

- [A drop that moves nothing proposes nothing](requirements.md#requirement-a-drop-that-moves-nothing-proposes-nothing).
  Picking something up and putting it down must not rewrite a plan the reader
  only wanted to look at.
- [A reschedule leaves the dependencies alone](domain.invariants.md#invariant-a-reschedule-leaves-the-dependencies-alone),
  and dependent work is not dragged along. The plan is allowed to contradict
  itself, and that contradiction is the reader's cue (see below).
- The event carries the previous window, so a consumer can tell a first plan from
  a change without keeping its own history.

## Who placed an item's window

```mermaid
stateDiagram-v2
    state "effort" as Effort
    state "due-date" as DueDate
    state "absent (the person's)" as Person

    [*] --> Effort : import, no due date
    [*] --> DueDate : import, with a due date
    [*] --> Person : created by hand, or dropped on a Calendar day
    Effort --> Effort : keep-up re-lays it, re-import, or lane drop
    DueDate --> DueDate : re-import of the document
    Effort --> Person : hand move
    DueDate --> Person : hand move
    Person --> Person : any later import leaves the dates alone
```

The states are the values of `placed_by_import` on a
[Roadmap Item](domain.md#roadmap-item), and that chapter defines a
[hand move](domain.md#roadmap-item). The two transitions to the person's state are
[a hand move](domain.invariants.md#invariant-a-hand-move-hands-the-window-to-the-person),
and no transition leaves it, because
[the importer never moves a window a person placed](domain.invariants.md#invariant-the-importer-never-moves-a-window-a-person-placed)
and an import never deletes an item. A drop on a Calendar day still places the
window by the `effort` rule, from the day dropped on. The window is the person's
because they chose that day.

## Dependency validation flow

```mermaid
flowchart TD
    Add["Add dependency: node waits on target"] --> Exists{"Target exists<br/>in this plan?"}
    Exists -- no --> RejectMissing["Rejected: unknown node"]
    Exists -- yes --> Self{"Target is the<br/>node itself?"}
    Self -- yes --> RejectSelf["Rejected: cannot wait on itself"]
    Self -- no --> Cycle{"Would it close<br/>a cycle?"}
    Cycle -- yes --> RejectCycle["Rejected: plan left untouched"]
    Cycle -- no --> Accept["Accepted and stored"]
    Accept --> Contradiction{"Does the successor open<br/>before the predecessor closes?"}
    Contradiction -- yes --> Report["Reported as a contradiction<br/>drawn as a doubling-back arrow"]
    Contradiction -- no --> Clean["Drawn as a plain arrow"]
```

- The three rejections are refusals to store, not warnings: an
  [unknown node](domain.invariants.md#invariant-a-dependency-names-a-node-in-the-plan),
  [a node waiting for itself](domain.invariants.md#invariant-nothing-waits-for-itself)
  and [a cycle](domain.invariants.md#invariant-a-cycle-is-refused).
- The contradiction check is the opposite:
  [it never refuses anything](domain.invariants.md#invariant-a-contradiction-is-reported-never-corrected).
  Dates that do not fit are a normal, temporary state of a plan being worked out.
  The arrow that has to double back through the gutter is how the reader sees it.
- Cycle detection walks the whole graph reachable from the target. It is an
  invariant of the plan rather than of a node, which is why it is checked on the
  root before the edge is accepted.

## Reading the plan by repository

```mermaid
flowchart LR
    Plan["Roadmap Plan"] --> Items["Items and milestones"]
    Items --> Scope["Repository Scope aliases"]
    Scope --> Resolve["Repository Scope Resolution"]
    Registry["Repository Registry<br/>(supplier)"] -.-> Resolve
    Resolve --> Known["Band per configured repository"]
    Resolve --> Unresolved["No-repository band:<br/>no alias, or alias no longer configured"]
    Known --> Lanes["Planning lanes inside each band"]
    Unresolved --> Lanes
    Lanes --> Draw["Spans, milestones and dependency arrows placed by date"]
```

- Resolution happens on the **read** path only, and
  [an alias that no longer resolves is kept](domain.invariants.md#invariant-an-unresolved-scope-alias-is-kept),
  so a repository removed today and configured again tomorrow leaves the plan
  exactly as it was.
- An item naming several repositories is drawn in the [band](domain.md#band) of
  each configured repository it names. Each band draws only that repository's
  [part](domain.md#part) of the item, so the parts can carry different dates
  ([Placing a plan in time](features.md#placing-a-plan-in-time)). Only the item's
  one window is stored.
- Dragging any part moves the whole item, because a part has no dates of its own
  to keep. The move is a [hand move](domain.md#roadmap-item), so the item is no
  longer placed by its effort, and every part then draws over its one moved
  window.
- The filter is built from what an item names rather than from where it was
  drawn, so the item stays findable under any of its repositories.

## Gathering an item's work and totalling its effort

```mermaid
flowchart TD
    Item["Roadmap Item<br/>(tag, task_id, knowledge_refs)"]
    Item --> Named["By name:<br/>the named task + listed knowledge_refs"]
    Item --> Tagged["By tag:<br/>tasks filed under the tag + chapters whose roadmap list names it"]
    Tasks["Tasks<br/>(supplier)"] -.-> Tagged
    Tasks -.-> Named
    Devbook["Devbook<br/>(supplier)"] -.-> Tagged
    Devbook -.-> Named
    Named --> Dedup["Merge: reached both ways is counted once,<br/>and recorded as reached both ways"]
    Tagged --> Dedup
    Dedup --> Total["Total registered effort:<br/>sum of registered story points only"]
    Dedup --> Unestimated["Count of gathered things that registered no estimate"]
    Total --> Report["Reported together, side by side"]
    Unestimated --> Report
```

The rules behind each box are in
[Roadmap Item Gathering](domain.md#roadmap-item-gathering) and
[its invariants](domain.invariants.md#roadmap-item-gathering). Gathering happens
on the **read** path only and
[writes nothing back](domain.invariants.md#invariant-gathering-never-writes).
