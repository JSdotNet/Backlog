# Naming: Productivity

```meta
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface names
> it is also known by are recorded in the `aliases` metadata field so a synonym
> can always be resolved back to one canonical concept.

## Term: Productivity Ledger

```meta
status: draft
aliases: [ProductivityLedger]
related: [.domain/productivity/domain.md#aggregate-productivity-ledger]
```

The append-only personal record of productivity-relevant activity.

## Term: Productivity Entry

```meta
status: draft
aliases: [ProductivityEntry, productivity_entry_id]
related: [.domain/productivity/domain.md#productivity-entry]
```

One recorded activity in the ledger.

## Term: AI Productivity Tracking

```meta
status: draft
aliases: [AI productivity, AI-assisted productivity]
related: [.domain/productivity/features.md#feature-ai-productivity-tracking]
```

The product capability for understanding how AI-assisted work contributes to the
person's outcomes.

## Term: Activity Kind

```meta
status: draft
aliases: [ActivityKind]
related: [.domain/productivity/domain.md#activity-kind]
```

The category used to group productivity activity.

## Term: Work Subject Ref

```meta
status: draft
aliases: [WorkSubjectRef, subject_ref]
related: [.domain/productivity/domain.md#work-subject-ref]
```

An opaque link to the backlog item, session, issue, pull request, commit, or note
that the productivity entry is about.