# Productivity

```meta
type: naming
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface names
> it is also known by are recorded in the `aliases` metadata field so a synonym
> can always be resolved back to one canonical concept.

## Productivity Ledger

```meta
type: term
status: draft
aliases: [ProductivityLedger]
related: [.domain/productivity/domain.md#productivity-ledger]
```

The append-only personal record of productivity-relevant activity.

## Productivity Entry

```meta
type: term
status: draft
aliases: [ProductivityEntry, productivity_entry_id]
related: [.domain/productivity/domain.md#productivity-entry]
```

One recorded activity in the ledger.

## AI Productivity Tracking

```meta
type: term
status: draft
aliases: [AI productivity, AI-assisted productivity]
related: [.domain/productivity/features.md#ai-productivity-tracking]
```

The product capability for understanding how AI-assisted work contributes to the
person's outcomes.

## Activity Kind

```meta
type: term
status: draft
aliases: [ActivityKind]
related: [.domain/productivity/domain.md#activity-kind]
```

The category used to group productivity activity.

## Work Subject Ref

```meta
type: term
status: draft
aliases: [WorkSubjectRef, subject_ref]
related: [.domain/productivity/domain.md#work-subject-ref]
```

An opaque link to the backlog item, session, issue, pull request, commit, or note
that the productivity entry is about.