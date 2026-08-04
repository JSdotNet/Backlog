# Flow: Second Brain

```meta
status: draft
```

> Lifecycle and process flows for this bounded context. Flows describe how a
> knowledge note moves through its workflow phases over time — complementary to
> `model.md` (structure) and `domain.md` (responsibilities/invariants).

## Knowledge note lifecycle

```mermaid
stateDiagram-v2
    [*] --> Created : Captured from Inbox or manually
    Created --> Organized : Assigned topic and PARA category
    Organized --> Linked : Linked to backlog entries or other notes
    Linked --> Organized : Links updated
    Organized --> Archived : Note no longer active
    Linked --> Archived : Note no longer active
    Archived --> Organized : Restored
```

- The lifecycle states (Created/Organized/Linked/Archived) are workflow phases,
  not a stored enum field; `PARA Category` = `archive` is the persisted archived
  state.
