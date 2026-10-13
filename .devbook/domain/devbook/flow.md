# Devbook

```meta
type: flow
status: draft
```

> Lifecycle and process flows for this bounded context. Flows describe how a
> knowledge note moves through its workflow phases over time — complementary to
> `model.md` (structure) and `domain.md` (responsibilities/invariants).

## Knowledge note lifecycle

```mermaid
stateDiagram-v2
    [*] --> Created : Routed To Brain from the Inbox, or captured in the Brain
    Created --> Organized : Moved out of the inbox area into another area
    Organized --> Linked : Linked to tasks or other notes
    Linked --> Organized : Links updated
    Organized --> Archived : Note no longer active
    Linked --> Archived : Note no longer active
    Archived --> Organized : Restored
```

- The lifecycle states (Created/Organized/Linked/Archived) are workflow phases,
  not a stored enum field. The persisted state is the note's area: a note in the
  Brain's inbox area (`_inbox` under the PARA default) is Created, and a note in
  its archive area is Archived. A Brain that declares its own areas without an
  archive area has no Archived state.
